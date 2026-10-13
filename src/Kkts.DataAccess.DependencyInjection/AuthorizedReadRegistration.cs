using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Kkts.DataAccess.DependencyInjection;

public static class AuthorizedReadServiceCollectionExtensions
{
    public static IServiceCollection AddAuthorizedReads<T>(
        this IServiceCollection services,
        string resourceKey,
        Action<AuthorizedReadResourceBuilder<T>> configure
    )
        where T : class
    {
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        var existing = services
            .Where(service => service.ServiceType == typeof(ResourceRegistration<T>))
            .Select(service => (ResourceRegistration<T>)service.ImplementationInstance!)
            .ToArray();
        if (existing.Any(resource => resource.Key == resourceKey))
            throw ResourceRegistration<T>.Failure(
                ConfigurationErrorCode.DuplicateRegistration,
                "",
                "The entity type and resource key are already registered."
            );
        if (
            existing.Length == 0
            && services.Any(service =>
                service.ServiceType == typeof(IAuthorizedReadRepository<T>)
                && !service.IsKeyedService
            )
        )
            throw ResourceRegistration<T>.Failure(
                ConfigurationErrorCode.DuplicateRegistration,
                "",
                "An unkeyed repository is already registered for this entity type."
            );

        var builder = new AuthorizedReadResourceBuilder<T>();
        configure(builder);
        var registration = new ResourceRegistration<T>(resourceKey, builder);
        services.AddSingleton(registration);
        services.AddSingleton<IResourceRegistration>(registration);
        registration.Register(services);
        services.TryAddScoped<IAuthorizedReadRepository<T>>(provider =>
        {
            var resources = provider.GetServices<ResourceRegistration<T>>().ToArray();
            if (resources.Length != 1)
                throw ResourceRegistration<T>.Failure(
                    ConfigurationErrorCode.AmbiguousResource,
                    "",
                    "Multiple resources use this entity type. Resolve the repository with its explicit resource key."
                );
            return provider.GetRequiredKeyedService<IAuthorizedReadRepository<T>>(resources[0].Key);
        });
        services.TryAddSingleton<
            IValidateOptions<ResourceReadOptions>,
            ResourceReadOptionsValidator
        >();
        services.TryAddSingleton<
            IAuthorizedReadRegistrationValidator,
            AuthorizedReadRegistrationValidator
        >();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, AuthorizedReadStartupValidation>()
        );
        return services;
    }
}

public sealed class AuthorizedReadResourceBuilder<T>
    where T : class
{
    internal ResourceSchema<T>? Schema;
    internal ContextRegistry? Registry;
    internal Condition? Mandatory;
    internal PolicyLimits? PolicyLimits;
    internal string CompilerVersion = "1";
    internal PolicyCompilerCacheOptions? CompilerCacheOptions;
    internal Func<IServiceProvider, IActivePolicySource>? Source;
    internal IActivePolicySource? SourceInstance;
    internal ServiceLifetime SourceLifetime = ServiceLifetime.Scoped;
    internal Func<IServiceProvider, IEntityReadAdapter<T>>? Adapter;
    internal IEntityReadAdapter<T>? AdapterInstance;
    internal ServiceLifetime AdapterLifetime = ServiceLifetime.Scoped;
    internal Func<IServiceProvider, ITrustedContextProvider>? Context;
    internal Func<TrustedContext, string>? Scope;
    internal Func<TrustedContext, string>? Subject;
    internal Func<IServiceProvider, IContinuationTokenProtector>? Protector;
    internal ResourceReadOptions Options = new();
    internal IReadOnlyList<IReadProviderCapabilities> Capabilities =
        Array.Empty<IReadProviderCapabilities>();
    internal Func<IServiceProvider, CancellationToken, ValueTask>? Startup;

    public AuthorizedReadResourceBuilder<T> WithSchema(ResourceSchema<T> schema)
    {
        Schema = schema ?? throw new ArgumentNullException(nameof(schema));
        return this;
    }

    public AuthorizedReadResourceBuilder<T> WithCompiler(
        ContextRegistry registry,
        Condition mandatory,
        PolicyLimits? limits = null,
        string compilerVersion = "1",
        PolicyCompilerCacheOptions? cacheOptions = null
    )
    {
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        Mandatory = mandatory ?? throw new ArgumentNullException(nameof(mandatory));
        PolicyLimits = limits;
        CompilerVersion = compilerVersion;
        CompilerCacheOptions = cacheOptions;
        return this;
    }

    public AuthorizedReadResourceBuilder<T> WithPolicySource(IActivePolicySource source)
    {
        SourceInstance = source ?? throw new ArgumentNullException(nameof(source));
        Source = null;
        return this;
    }

    public AuthorizedReadResourceBuilder<T> WithPolicySource(
        Func<IServiceProvider, IActivePolicySource> factory,
        ServiceLifetime lifetime = ServiceLifetime.Scoped
    )
    {
        DemandFactoryLifetime(lifetime);
        Source = factory ?? throw new ArgumentNullException(nameof(factory));
        SourceInstance = null;
        SourceLifetime = lifetime;
        return this;
    }

    public AuthorizedReadResourceBuilder<T> WithAdapter(IEntityReadAdapter<T> adapter)
    {
        AdapterInstance = adapter ?? throw new ArgumentNullException(nameof(adapter));
        Adapter = null;
        return this;
    }

    public AuthorizedReadResourceBuilder<T> WithAdapter(
        Func<IServiceProvider, IEntityReadAdapter<T>> factory,
        ServiceLifetime lifetime = ServiceLifetime.Scoped
    )
    {
        DemandFactoryLifetime(lifetime);
        Adapter = factory ?? throw new ArgumentNullException(nameof(factory));
        AdapterInstance = null;
        AdapterLifetime = lifetime;
        return this;
    }

    static void DemandFactoryLifetime(ServiceLifetime lifetime)
    {
        if (lifetime is not (ServiceLifetime.Scoped or ServiceLifetime.Transient))
            throw ResourceRegistration<T>.Failure(
                ConfigurationErrorCode.InvalidLifetime,
                "",
                "Use a scoped/transient factory or a host-owned singleton instance; singleton factories must not resolve request services."
            );
    }

    public AuthorizedReadResourceBuilder<T> WithTrustedContext(
        Func<IServiceProvider, ITrustedContextProvider> factory
    )
    {
        Context = factory ?? throw new ArgumentNullException(nameof(factory));
        return this;
    }

    public AuthorizedReadResourceBuilder<T> WithTrustedContext<TProvider>()
        where TProvider : class, ITrustedContextProvider =>
        WithTrustedContext(provider => ActivatorUtilities.CreateInstance<TProvider>(provider));

    public AuthorizedReadResourceBuilder<T> WithScope(Func<TrustedContext, string> scope)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        return this;
    }

    public AuthorizedReadResourceBuilder<T> WithScope(string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        return WithScope(_ => scope);
    }

    public AuthorizedReadResourceBuilder<T> WithCursorSubject(Func<TrustedContext, string> subject)
    {
        Subject = subject ?? throw new ArgumentNullException(nameof(subject));
        return this;
    }

    public AuthorizedReadResourceBuilder<T> WithCursorProtection(
        Func<IServiceProvider, IContinuationTokenProtector> protector
    )
    {
        Protector = protector ?? throw new ArgumentNullException(nameof(protector));
        return this;
    }

    public AuthorizedReadResourceBuilder<T> WithOptions(ResourceReadOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        return this;
    }

    public AuthorizedReadResourceBuilder<T> ValidateWith(
        Func<IServiceProvider, CancellationToken, ValueTask> validation
    )
    {
        Startup = validation ?? throw new ArgumentNullException(nameof(validation));
        return this;
    }

    public AuthorizedReadResourceBuilder<T> WithCapabilities(
        params IReadProviderCapabilities[] profiles
    )
    {
        ArgumentNullException.ThrowIfNull(profiles);
        Capabilities = Array.AsReadOnly(profiles.ToArray());
        return this;
    }
}

internal sealed class ResourceRegistration<T> : IResourceRegistration
    where T : class
{
    readonly AuthorizedReadResourceBuilder<T> configuration;
    readonly object dependencyKey;
    IReadOnlyList<IReadProviderCapabilities> profiles = Array.Empty<IReadProviderCapabilities>();
    volatile bool metadataValidated,
        startupComplete;
    public string Key { get; }

    public ResourceRegistration(string key, AuthorizedReadResourceBuilder<T> builder)
    {
        Key = key;
        dependencyKey = (typeof(T), key);
        // Retaining the mutable builder would let host code change service lifetimes after registration.
        configuration = new()
        {
            Schema = builder.Schema,
            Registry = builder.Registry,
            Mandatory = builder.Mandatory == null ? null : CursorSnapshot(builder.Mandatory),
            PolicyLimits = builder.PolicyLimits,
            CompilerVersion = builder.CompilerVersion,
            CompilerCacheOptions = builder.CompilerCacheOptions,
            Source = builder.Source,
            SourceInstance = builder.SourceInstance,
            SourceLifetime = builder.SourceLifetime,
            Adapter = builder.Adapter,
            AdapterInstance = builder.AdapterInstance,
            AdapterLifetime = builder.AdapterLifetime,
            Context = builder.Context,
            Scope = builder.Scope,
            Subject = builder.Subject,
            Protector = builder.Protector,
            Options = builder.Options with { },
            Startup = builder.Startup,
            Capabilities = Array.AsReadOnly(builder.Capabilities.ToArray()),
        };
    }

    static Condition CursorSnapshot(Condition mandatory) =>
        System.Text.Json.JsonSerializer.Deserialize<Condition>(
            System.Text.Json.JsonSerializer.Serialize(mandatory, PolicyJson.Options),
            PolicyJson.Options
        )!;

    internal static DataAccessConfigurationException Failure(
        ConfigurationErrorCode code,
        string path,
        string message,
        Exception? cause = null
    ) => new(new(code, path, message), cause);

    void DemandConfiguration()
    {
        if (
            configuration.Schema == null
            || configuration.Registry == null
            || configuration.Mandatory == null
            || configuration.Source == null && configuration.SourceInstance == null
            || configuration.Adapter == null && configuration.AdapterInstance == null
            || configuration.Context == null
            || configuration.Scope == null
        )
            throw Failure(
                ConfigurationErrorCode.MissingRegistration,
                "",
                "Schema, compiler/registry/mandatory condition, policy source, adapter, trusted context and scope are required."
            );
        if (string.IsNullOrWhiteSpace(configuration.CompilerVersion))
            throw Failure(
                ConfigurationErrorCode.InvalidOptions,
                "/compilerVersion",
                "Compiler version is required."
            );
        if (
            configuration.Options.Cursor != null
            && (configuration.Subject == null || configuration.Protector == null)
        )
            throw Failure(
                ConfigurationErrorCode.MissingRegistration,
                "/cursor",
                "Cursor protection and a server-derived subject are required."
            );
        if (
            configuration.Options.Cursor == null
            && (configuration.Subject != null || configuration.Protector != null)
        )
            throw Failure(
                ConfigurationErrorCode.InvalidOptions,
                "/cursor",
                "Cursor bindings require cursor options."
            );
        var result = new ResourceReadOptionsValidator().Validate(Key, configuration.Options);
        if (result.Failed)
            throw Failure(
                ConfigurationErrorCode.InvalidOptions,
                "/options",
                string.Join(" ", result.Failures)
            );
    }

    PolicyCompiler<T> Compile()
    {
        DemandConfiguration();
        if (!metadataValidated)
            throw Failure(
                ConfigurationErrorCode.MissingRegistration,
                "/startup",
                "Await registration validation before resolving a resource compiler."
            );
        try
        {
            return new(
                configuration.Schema!,
                configuration.Registry!,
                configuration.Mandatory!,
                configuration.PolicyLimits,
                configuration.CompilerVersion,
                profiles,
                configuration.CompilerCacheOptions
            );
        }
        catch (InvalidRequestException exception)
        {
            throw Failure(
                ConfigurationErrorCode.InvalidOptions,
                "/compiler/mandatory",
                "The mandatory condition is invalid for the registered schema and context registry.",
                exception
            );
        }
    }

    internal void Register(IServiceCollection services)
    {
        services.AddKeyedSingleton<ResourceSchema<T>>(
            Key,
            (_, _) =>
            {
                DemandConfiguration();
                return configuration.Schema!;
            }
        );
        services.AddKeyedSingleton<ContextRegistry>(
            dependencyKey,
            (_, _) =>
            {
                DemandConfiguration();
                return configuration.Registry!;
            }
        );
        services.AddKeyedSingleton<PolicyCompiler<T>>(Key, (_, _) => Compile());
        services.AddKeyedSingleton<ResourceReadOptions>(dependencyKey, configuration.Options);
        if (configuration.SourceInstance is { } source)
            services.AddKeyedSingleton<IActivePolicySource>(dependencyKey, source);
        else
            services.Add(
                ServiceDescriptor.DescribeKeyed(
                    typeof(IActivePolicySource),
                    dependencyKey,
                    (provider, _) =>
                    {
                        DemandConfiguration();
                        return configuration.Source!(provider);
                    },
                    configuration.SourceLifetime
                )
            );
        if (configuration.AdapterInstance is { } adapter)
            services.AddKeyedSingleton<IEntityReadAdapter<T>>(dependencyKey, adapter);
        else
            services.Add(
                ServiceDescriptor.DescribeKeyed(
                    typeof(IEntityReadAdapter<T>),
                    dependencyKey,
                    (provider, _) =>
                    {
                        DemandConfiguration();
                        return configuration.Adapter!(provider);
                    },
                    configuration.AdapterLifetime
                )
            );
        services.AddKeyedScoped<ITrustedContextProvider>(
            dependencyKey,
            (provider, _) =>
            {
                DemandConfiguration();
                return configuration.Context!(provider);
            }
        );
        if (configuration.Protector != null)
            services.AddKeyedScoped<IContinuationTokenProtector>(
                dependencyKey,
                (provider, _) => configuration.Protector(provider)
            );
        services.AddKeyedScoped<IAuthorizedReadRepository<T>>(
            Key,
            (provider, _) =>
            {
                DemandConfiguration();
                if (!startupComplete)
                    throw Failure(
                        ConfigurationErrorCode.MissingRegistration,
                        "/startup",
                        "Await registration validation before resolving a repository."
                    );
                var options = configuration.Options;
                var cursor = options.Cursor is { } settings
                    ? new CursorReadConfiguration(
                        provider.GetRequiredKeyedService<IContinuationTokenProtector>(
                            dependencyKey
                        ),
                        configuration.Subject!,
                        settings.DatasetId,
                        settings.Lifetime,
                        settings.MaxEncodedTokenSize,
                        settings.TimeProvider
                    )
                    : null;
                return new AuthorizedReadRepository<T>(
                    configuration.Schema!,
                    provider.GetRequiredKeyedService<PolicyCompiler<T>>(Key),
                    provider.GetRequiredKeyedService<IActivePolicySource>(dependencyKey),
                    provider.GetRequiredKeyedService<IEntityReadAdapter<T>>(dependencyKey),
                    provider.GetRequiredKeyedService<ITrustedContextProvider>(dependencyKey),
                    trusted =>
                    {
                        var scope = configuration.Scope!(trusted);
                        if (string.IsNullOrWhiteSpace(scope))
                            throw Failure(
                                ConfigurationErrorCode.InvalidOptions,
                                "/scope",
                                "The server-derived policy scope is required."
                            );
                        return scope;
                    },
                    options.Limits,
                    cursor,
                    options.ProjectionMode,
                    options.Export
                );
            }
        );
    }

    public async Task ValidateAsync(IServiceProvider services, CancellationToken cancellation)
    {
        DemandConfiguration();
        foreach (
            var optionsValidator in services.GetServices<IValidateOptions<ResourceReadOptions>>()
        )
        {
            var result = optionsValidator.Validate(Key, configuration.Options);
            if (result.Failed)
                throw Failure(
                    ConfigurationErrorCode.InvalidOptions,
                    "/options",
                    string.Join(" ", result.Failures)
                );
        }
        if (configuration.Options.Cursor != null)
            _ = services.GetRequiredKeyedService<IContinuationTokenProtector>(dependencyKey);
        var adapter = services.GetRequiredKeyedService<IEntityReadAdapter<T>>(dependencyKey);
        if (adapter is IEntityReadRegistrationValidator<T> validator)
        {
            try
            {
                await validator.ValidateAsync(configuration.Schema!, cancellation);
            }
            catch (InvalidOperationException exception)
            {
                throw Failure(
                    ConfigurationErrorCode.InvalidModel,
                    "/adapter",
                    "The registered entity provider/model does not match the resource schema.",
                    exception
                );
            }
            catch (ProviderUnsupportedException exception)
            {
                throw Failure(
                    ConfigurationErrorCode.InvalidModel,
                    "/adapter" + (exception.Diagnostics.FirstOrDefault()?.Path ?? ""),
                    "The model cannot support the declared resource operations.",
                    exception
                );
            }
        }
        var actual = adapter as IReadProviderCapabilitySource;
        if (
            actual != null
            && (
                configuration.Options.Cursor != null
                    && !actual.Operations.HasFlag(ReadOperations.Cursor)
                || configuration.Options.Export.DataConsistency == ExportDataConsistency.Snapshot
                    && !actual.Operations.HasFlag(ReadOperations.Snapshot)
            )
        )
            throw Failure(
                ConfigurationErrorCode.InvalidOptions,
                "/options",
                "Cursor/snapshot options require their declared resource operations."
            );
        var targets = configuration
            .Capabilities.Concat(actual?.Capabilities is { } profile ? [profile] : [])
            .DistinctBy(profile => profile.Identity)
            .OrderBy(profile => profile.Identity, StringComparer.Ordinal)
            .ToArray();
        if (
            metadataValidated
            && !profiles
                .Select(profile => profile.Identity)
                .SequenceEqual(targets.Select(profile => profile.Identity))
        )
            throw Failure(
                ConfigurationErrorCode.InvalidModel,
                "/adapter/profile",
                "The provider/model changed after this resource compiler was validated."
            );
        profiles = Array.AsReadOnly(targets);
        metadataValidated = true;
        _ = services.GetRequiredKeyedService<PolicyCompiler<T>>(Key);
        if (configuration.Startup is { } validate)
            await validate(services, cancellation);
        cancellation.ThrowIfCancellationRequested();
        startupComplete = true;
    }
}
