using Kkts.DataAccess.DependencyInjection;
using Kkts.DataAccess.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Database = Kkts.DataAccess.Tests.FactoryIntegrationTests.Database;
using Employee = Kkts.DataAccess.Tests.CompilerTests.Employee;

namespace Kkts.DataAccess.Tests;

public class DependencyInjectionTests : IAsyncLifetime
{
    readonly string path = Path.Combine(Path.GetTempPath(), $"kda-di-{Guid.NewGuid():N}.db");
    DbContextOptions<Database> Options =>
        new DbContextOptionsBuilder<Database>().UseSqlite($"Data Source={path}").Options;

    public sealed class Identity
    {
        public int Tenant = 1;
        public int Acquisitions;
        public string Subject = "subject-one";
    }

    public sealed class Provider(Identity identity) : ITrustedContextProvider
    {
        public async ValueTask<TrustedContext> GetAsync(CancellationToken cancellation = default)
        {
            await Task.Yield();
            cancellation.ThrowIfCancellationRequested();
            identity.Acquisitions++;
            return CompilerTests.Context(tenant: identity.Tenant);
        }
    }

    static Condition Mandatory => CompilerTests.Reference("TenantId", "tenant");

    ServiceCollection Services(FactoryIntegrationTests.Source source)
    {
        var services = new ServiceCollection();
        services.AddScoped<Identity>();
        services.AddSingleton<IDbContextFactory<Database>>(
            new PooledDbContextFactory<Database>(Options)
        );
        return services;
    }

    static void Resource(
        AuthorizedReadResourceBuilder<Employee> resource,
        FactoryIntegrationTests.Source source
    ) =>
        resource
            .WithSchema(CompilerTests.Schema)
            .WithCompiler(CompilerTests.Registry, Mandatory)
            .WithPolicySource(source)
            .WithAdapter(services =>
                SqliteEntityReads.Create<Employee, Database>(
                    CompilerTests.Schema,
                    services.GetRequiredService<IDbContextFactory<Database>>()
                )
            )
            .WithTrustedContext<Provider>()
            .WithScope(trusted => "tenant-" + trusted.Values["tenant"])
            .WithOptions(new() { Limits = new(2, 100, 1) });

    public Task InitializeAsync() => Task.CompletedTask;

    async Task SeedAsync()
    {
        await using var setup = new Database(Options);
        await setup.Database.EnsureCreatedAsync();
        setup.AddRange(
            new Employee { Id = 1, TenantId = 1 },
            new Employee { Id = 2, TenantId = 1 },
            new Employee { Id = 3, TenantId = 2 }
        );
        await setup.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        File.Delete(path);
        File.Delete(path + "-wal");
        File.Delete(path + "-shm");
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Compiler_cache_capacity_is_captured_by_resource_registration()
    {
        var source = new FactoryIntegrationTests.Source();
        var services = Services(source);
        services.AddAuthorizedReads<Employee>(
            "employees",
            resource =>
            {
                Resource(resource, source);
                resource.WithCompiler(CompilerTests.Registry, Mandatory, cacheOptions: new(1));
            }
        );
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
        await provider.GetRequiredService<IAuthorizedReadRegistrationValidator>().ValidateAsync();
        var compiler = provider.GetRequiredKeyedService<PolicyCompiler<Employee>>("employees");
        var policy = CompilerTests.Capture(
            CompilerTests.Definition(CompilerTests.Rule("allow", Effect.Allow))
        );
        compiler.Bind(policy, CompilerTests.Context(), "scope");
        compiler.Bind(policy with { Version = 2 }, CompilerTests.Context(), "scope");
        Assert.Equal(1, compiler.TemplateCount);
        Assert.Equal(new long[] { 2 }, compiler.TemplateInsertionOrder);
    }

    [Fact]
    public async Task Startup_requires_neither_request_identity_nor_active_policy_or_database()
    {
        var source = new FactoryIntegrationTests.Source();
        var services = Services(source);
        var identities = 0;
        services.AddAuthorizedReads<Employee>(
            "employees",
            resource =>
            {
                Resource(resource, source);
                resource
                    .WithPolicySource(_ => throw new PolicyMissingException())
                    .WithTrustedContext(_ =>
                    {
                        identities++;
                        throw new AuthorizationException();
                    })
                    .WithScope(_ => throw new AuthorizationException());
            }
        );
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
        await provider.GetRequiredService<IAuthorizedReadRegistrationValidator>().ValidateAsync();
        Assert.Equal(0, identities);
        Assert.Equal(0, source.Captures);
        Assert.False(File.Exists(path));
        Assert.Single(provider.GetServices<IHostedService>());
    }

    [Fact]
    public async Task Two_request_scopes_share_identity_free_compiler_but_not_provider_or_repository()
    {
        await SeedAsync();
        var source = new FactoryIntegrationTests.Source();
        var services = Services(source);
        services.AddAuthorizedReads<Employee>("employees", resource => Resource(resource, source));
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
        await provider.GetRequiredService<IAuthorizedReadRegistrationValidator>().ValidateAsync();
        await using var one = provider.CreateAsyncScope();
        await using var two = provider.CreateAsyncScope();
        var firstIdentity = one.ServiceProvider.GetRequiredService<Identity>();
        var secondIdentity = two.ServiceProvider.GetRequiredService<Identity>();
        secondIdentity.Tenant = 2;
        var first = one.ServiceProvider.GetRequiredService<IAuthorizedReadRepository<Employee>>();
        var second = two.ServiceProvider.GetRequiredKeyedService<
            IAuthorizedReadRepository<Employee>
        >("employees");
        Assert.Same(
            first,
            one.ServiceProvider.GetRequiredKeyedService<IAuthorizedReadRepository<Employee>>(
                "employees"
            )
        );
        Assert.NotSame(first, second);
        Assert.Same(
            one.ServiceProvider.GetRequiredKeyedService<PolicyCompiler<Employee>>("employees"),
            two.ServiceProvider.GetRequiredKeyedService<PolicyCompiler<Employee>>("employees")
        );
        Assert.Equal(2, (await first.CountAsync(new())).Total);
        Assert.Equal(1, (await second.CountAsync(new())).Total);
        Assert.Equal(2, (await first.CountAsync(new())).Total);
        Assert.Equal(2, firstIdentity.Acquisitions);
        Assert.Equal(1, secondIdentity.Acquisitions);
        Assert.Throws<InvalidOperationException>(() =>
            provider.GetRequiredService<IAuthorizedReadRepository<Employee>>()
        );
    }

    [Fact]
    public async Task Multiple_resources_require_explicit_keys_and_duplicate_keys_are_rejected()
    {
        var source = new FactoryIntegrationTests.Source();
        var services = Services(source);
        services.AddAuthorizedReads<Employee>("one", resource => Resource(resource, source));
        var duplicate = Assert.Throws<DataAccessConfigurationException>(() =>
            services.AddAuthorizedReads<Employee>("one", resource => Resource(resource, source))
        );
        Assert.Equal(ConfigurationErrorCode.DuplicateRegistration, duplicate.Diagnostics[0].Code);
        services.AddAuthorizedReads<Employee>("two", resource => Resource(resource, source));
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
        await provider.GetRequiredService<IAuthorizedReadRegistrationValidator>().ValidateAsync();
        await using var scope = provider.CreateAsyncScope();
        var error = Assert.Throws<DataAccessConfigurationException>(() =>
            scope.ServiceProvider.GetRequiredService<IAuthorizedReadRepository<Employee>>()
        );
        Assert.Equal(ConfigurationErrorCode.AmbiguousResource, error.Diagnostics[0].Code);
        Assert.NotSame(
            scope.ServiceProvider.GetRequiredKeyedService<IAuthorizedReadRepository<Employee>>(
                "one"
            ),
            scope.ServiceProvider.GetRequiredKeyedService<IAuthorizedReadRepository<Employee>>(
                "two"
            )
        );
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("batch")]
    [InlineData("projection")]
    [InlineData("cursor")]
    [InlineData("cursor-binding")]
    public async Task Invalid_configuration_fails_explicitly_at_startup_without_identity(
        string failure
    )
    {
        var source = new FactoryIntegrationTests.Source();
        var services = Services(source);
        services.AddAuthorizedReads<Employee>(
            "employees",
            resource =>
            {
                if (failure == "missing")
                    return;
                Resource(resource, source);
                var options = new ResourceReadOptions
                {
                    Limits = failure == "batch" ? new(1, 100, 2) : new(2, 100, 1),
                    ProjectionMode =
                        failure == "projection"
                            ? (ReadProjectionMode)99
                            : ReadProjectionMode.Required,
                    Cursor = failure == "cursor" ? new("dataset") : null,
                };
                resource.WithOptions(options);
                if (failure == "cursor-binding")
                    resource.WithCursorSubject(_ => "host-subject");
            }
        );
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
        var exception = await Assert.ThrowsAsync<DataAccessConfigurationException>(() =>
            provider.GetRequiredService<IAuthorizedReadRegistrationValidator>().ValidateAsync()
        );
        Assert.NotEmpty(exception.Diagnostics);
        Assert.Equal(0, source.Captures);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Resolver_backed_singleton_factories_are_rejected_instead_of_capturing_request_services()
    {
        var source = new FactoryIntegrationTests.Source();
        var services = Services(source);
        var error = Assert.Throws<DataAccessConfigurationException>(() =>
            services.AddAuthorizedReads<Employee>(
                "employees",
                resource => resource.WithPolicySource(_ => source, ServiceLifetime.Singleton)
            )
        );
        Assert.Equal(ConfigurationErrorCode.InvalidLifetime, error.Diagnostics[0].Code);
    }

    [Fact]
    public async Task Hosted_validation_stops_startup_for_contradictory_limits()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddAuthorizedReads<Employee>(
            "employees",
            resource =>
                resource
                    .WithSchema(CompilerTests.Schema)
                    .WithCompiler(CompilerTests.Registry, Mandatory)
                    .WithPolicySource(new FactoryIntegrationTests.Source())
                    .WithAdapter(_ =>
                        throw new InvalidOperationException("Must not construct an adapter.")
                    )
                    .WithTrustedContext(_ => throw new AuthorizationException())
                    .WithScope("host")
                    .WithOptions(new() { Limits = new(1, 100, 2) })
        );
        await using var app = builder.Build();
        var error = await Assert.ThrowsAsync<DataAccessConfigurationException>(() =>
            app.StartAsync()
        );
        Assert.Equal(ConfigurationErrorCode.InvalidOptions, error.Diagnostics[0].Code);
    }

    sealed class CustomOptionsValidator : IValidateOptions<ResourceReadOptions>
    {
        public ValidateOptionsResult Validate(string? name, ResourceReadOptions options) =>
            ValidateOptionsResult.Fail("Host options validation failed.");
    }

    [Fact]
    public async Task Options_and_async_startup_extension_hooks_are_used_without_capturing_identity()
    {
        var source = new FactoryIntegrationTests.Source();
        var services = Services(source);
        var calls = 0;
        services.AddAuthorizedReads<Employee>(
            "employees",
            resource =>
            {
                Resource(resource, source);
                resource.ValidateWith(
                    async (_, token) =>
                    {
                        await Task.Yield();
                        token.ThrowIfCancellationRequested();
                        calls++;
                    }
                );
            }
        );
        await using (var provider = services.BuildServiceProvider())
            await provider
                .GetRequiredService<IAuthorizedReadRegistrationValidator>()
                .ValidateAsync();
        Assert.Equal(1, calls);
        Assert.Equal(0, source.Captures);
        services.AddSingleton<IValidateOptions<ResourceReadOptions>, CustomOptionsValidator>();
        await using (var provider = services.BuildServiceProvider())
            await Assert.ThrowsAsync<DataAccessConfigurationException>(() =>
                provider.GetRequiredService<IAuthorizedReadRegistrationValidator>().ValidateAsync()
            );
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Captured_builder_mutation_cannot_change_registration_options_after_registration()
    {
        var source = new FactoryIntegrationTests.Source();
        var services = Services(source);
        AuthorizedReadResourceBuilder<Employee>? captured = null;
        services.AddAuthorizedReads<Employee>(
            "employees",
            resource =>
            {
                Resource(resource, source);
                captured = resource;
            }
        );
        captured!.WithOptions(new() { Limits = new(1, 100, 2) });
        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IAuthorizedReadRegistrationValidator>().ValidateAsync();
        Assert.False(File.Exists(path));
    }

    sealed class IdentityProtector : IContinuationTokenProtector
    {
        public byte[] Protect(byte[] payload) => payload;

        public byte[] Unprotect(byte[] payload) => payload;
    }

    [Fact]
    public async Task Awaited_identity_snapshots_scope_and_cursor_subject_without_reacquisition()
    {
        await SeedAsync();
        var source = new FactoryIntegrationTests.Source();
        var services = Services(source);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        services.AddAuthorizedReads<Employee>(
            "employees",
            resource =>
            {
                Resource(resource, source);
                resource
                    .WithTrustedContext(provider => new RepositoryTests.AsyncContext(
                        async cancellation =>
                        {
                            var identity = provider.GetRequiredService<Identity>();
                            identity.Acquisitions++;
                            var trusted = new TrustedContext(
                                ["manager"],
                                [],
                                new Dictionary<string, object?>
                                {
                                    ["tenant"] = identity.Tenant,
                                    ["department"] = 10,
                                    ["subject"] = identity.Subject,
                                }
                            );
                            started.TrySetResult();
                            await release.Task.WaitAsync(cancellation);
                            return trusted;
                        }
                    ))
                    .WithCursorSubject(trusted => (string)trusted.Values["subject"]!)
                    .WithCursorProtection(_ => new IdentityProtector())
                    .WithOptions(new() { Limits = new(2, 100, 1), Cursor = new("dataset") });
            }
        );
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true }
        );
        await provider.GetRequiredService<IAuthorizedReadRegistrationValidator>().ValidateAsync();
        Assert.Equal(0, source.Captures);
        await using var scope = provider.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<Identity>();
        var repository = scope.ServiceProvider.GetRequiredService<
            IAuthorizedReadRepository<Employee>
        >();
        var request = new CursorListReadRequest(Selection: ["Id"], Limit: 1, IncludeTotal: false);
        var pending = repository.ListCursorAsync(request);
        await started.Task;
        identity.Tenant = 2;
        identity.Subject = "subject-two";
        release.SetResult();
        var first = await pending;
        Assert.Equal(1, first.Records[0].GetProperty("Id").GetInt32());
        Assert.NotNull(first.NextContinuationToken);
        Assert.Equal("tenant-1", source.LastScope);
        Assert.Equal(1, identity.Acquisitions);
        identity.Tenant = 1;
        var resume = request with { ContinuationToken = first.NextContinuationToken };
        await Assert.ThrowsAsync<InvalidContinuationTokenException>(() =>
            repository.ListCursorAsync(resume)
        );
        identity.Subject = "subject-one";
        Assert.Equal(
            2,
            (await repository.ListCursorAsync(resume)).Records[0].GetProperty("Id").GetInt32()
        );
        Assert.Equal(3, identity.Acquisitions);
    }

    sealed class StampedSource(FactoryIntegrationTests.Source inner) : IActivationAwarePolicySource
    {
        public List<string> Scopes = [];

        public Task<CapturedPolicy> CaptureAsync(
            string scope,
            string resource,
            CancellationToken cancellation = default
        )
        {
            Scopes.Add(scope);
            return inner.CaptureAsync(scope, resource, cancellation);
        }

        public async Task<CapturedPolicyActivation> CaptureActivationAsync(
            string scope,
            string resource,
            CancellationToken cancellation = default
        )
        {
            var policy = await CaptureAsync(scope, resource, cancellation);
            return new(policy, new(policy.Version, 1));
        }

        public Task<PolicyActivationStamp> ReadActivationAsync(
            string scope,
            string resource,
            CancellationToken cancellation = default
        )
        {
            cancellation.ThrowIfCancellationRequested();
            Scopes.Add(scope);
            return Task.FromResult(new PolicyActivationStamp(1, 1));
        }
    }

    [Fact]
    public async Task Stamped_export_is_lazy_and_retains_its_operation_scope_after_identity_changes()
    {
        await SeedAsync();
        var source = new FactoryIntegrationTests.Source();
        var stamped = new StampedSource(source);
        var services = Services(source);
        services.AddAuthorizedReads<Employee>(
            "employees",
            resource =>
            {
                Resource(resource, source);
                resource
                    .WithPolicySource(stamped)
                    .WithOptions(
                        new()
                        {
                            Limits = new(2, 100, 1),
                            Export = new(ExportPolicyConsistency.AbortOnPolicyChange),
                        }
                    );
            }
        );
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true }
        );
        await provider.GetRequiredService<IAuthorizedReadRegistrationValidator>().ValidateAsync();
        await using var scope = provider.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<Identity>();
        var repository = scope.ServiceProvider.GetRequiredService<
            IAuthorizedReadRepository<Employee>
        >();
        await using var export = repository
            .ExportAsync(new(Selection: ["Id"]))
            .GetAsyncEnumerator();
        Assert.Equal(0, identity.Acquisitions);
        Assert.True(await export.MoveNextAsync());
        Assert.Equal(1, export.Current.Records[0].GetProperty("Id").GetInt32());
        identity.Tenant = 2;
        Assert.True(await export.MoveNextAsync());
        Assert.Equal(2, export.Current.Records[0].GetProperty("Id").GetInt32());
        Assert.False(await export.MoveNextAsync());
        Assert.Equal(1, identity.Acquisitions);
        Assert.Equal(1, source.Captures);
        Assert.All(stamped.Scopes, captured => Assert.Equal("tenant-1", captured));
    }

    public sealed class Other
    {
        public int Id { get; set; }
    }

    sealed class UnpublishedSource : IActivePolicySource
    {
        public Task<CapturedPolicy> CaptureAsync(
            string scope,
            string resource,
            CancellationToken cancellation = default
        ) => throw new PolicyMissingException();
    }

    sealed class UnusedAdapter : IEntityReadAdapter<Other>
    {
        public ValueTask<IEntityReadSession<Other>> OpenAsync(
            CancellationToken cancellation = default
        ) => throw new InvalidOperationException("Startup must not open an entity session.");
    }

    [Fact]
    public async Task Same_key_for_different_entity_types_keeps_nongeneric_dependencies_separate()
    {
        await SeedAsync();
        var source = new FactoryIntegrationTests.Source();
        var services = Services(source);
        services.AddAuthorizedReads<Employee>("shared", resource => Resource(resource, source));
        services.AddAuthorizedReads<Other>(
            "shared",
            resource =>
                resource
                    .WithSchema(
                        new(
                            "Other",
                            "1",
                            [
                                ResourceSchema<Other>.Field(
                                    "Id",
                                    entity => entity.Id,
                                    [FieldOperation.Select]
                                ),
                            ],
                            "Id"
                        )
                    )
                    .WithCompiler(
                        new(new Dictionary<string, Type> { ["alternate"] = typeof(int) }),
                        CompilerTests.Reference("Id", "alternate")
                    )
                    .WithPolicySource(new UnpublishedSource())
                    .WithAdapter(new UnusedAdapter())
                    .WithTrustedContext(_ => new SynchronousTrustedContextProvider(() =>
                        new TrustedContext(
                            [],
                            [],
                            new Dictionary<string, object?> { ["alternate"] = 1 }
                        )
                    ))
                    .WithScope("other-scope")
                    .WithOptions(new() { Limits = new(1, 100, 1) })
        );
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true }
        );
        await provider.GetRequiredService<IAuthorizedReadRegistrationValidator>().ValidateAsync();
        await using var scope = provider.CreateAsyncScope();
        Assert.NotNull(
            scope.ServiceProvider.GetRequiredService<IAuthorizedReadRepository<Other>>()
        );
        var repository = scope.ServiceProvider.GetRequiredService<
            IAuthorizedReadRepository<Employee>
        >();
        Assert.Equal(
            2,
            (await repository.ListAsync(new(Selection: ["Id"], Limit: 2))).Records.Count
        );
    }

    public sealed class EmptyDatabase(DbContextOptions<EmptyDatabase> options) : DbContext(options)
    {
        public int Disposals;

        public override async ValueTask DisposeAsync()
        {
            Disposals++;
            await base.DisposeAsync();
        }
    }

    sealed class EmptyFactory(DbContextOptions<EmptyDatabase> options)
        : IDbContextFactory<EmptyDatabase>
    {
        public EmptyDatabase? Created;

        public EmptyDatabase CreateDbContext() =>
            throw new InvalidOperationException("Async creation required.");

        public Task<EmptyDatabase> CreateDbContextAsync(
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Created = new(options));
        }
    }

    [Fact]
    public async Task Startup_model_mismatch_is_structured_and_disposes_its_temporary_context()
    {
        var source = new FactoryIntegrationTests.Source();
        var services = Services(source);
        var factory = new EmptyFactory(
            new DbContextOptionsBuilder<EmptyDatabase>().UseSqlite($"Data Source={path}").Options
        );
        services.AddAuthorizedReads<Employee>(
            "employees",
            resource =>
            {
                Resource(resource, source);
                resource.WithAdapter(_ =>
                    SqliteEntityReads.Create<Employee, EmptyDatabase>(CompilerTests.Schema, factory)
                );
            }
        );
        await using var provider = services.BuildServiceProvider();
        var error = await Assert.ThrowsAsync<DataAccessConfigurationException>(() =>
            provider.GetRequiredService<IAuthorizedReadRegistrationValidator>().ValidateAsync()
        );
        Assert.Equal(ConfigurationErrorCode.InvalidModel, error.Diagnostics[0].Code);
        Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.Equal(1, factory.Created!.Disposals);
        Assert.False(File.Exists(path));
        Assert.Equal(0, source.Captures);
    }

    [Fact]
    public async Task Cancelled_startup_does_not_construct_services_or_run_extension_hooks()
    {
        var source = new FactoryIntegrationTests.Source();
        var services = Services(source);
        var calls = 0;
        services.AddAuthorizedReads<Employee>(
            "employees",
            resource =>
            {
                Resource(resource, source);
                resource
                    .WithAdapter(_ =>
                    {
                        calls++;
                        throw new InvalidOperationException();
                    })
                    .ValidateWith(
                        (_, _) =>
                        {
                            calls++;
                            return ValueTask.CompletedTask;
                        }
                    );
            }
        );
        await using var provider = services.BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider
                .GetRequiredService<IAuthorizedReadRegistrationValidator>()
                .ValidateAsync(cancellation.Token)
        );
        Assert.Equal(0, calls);
        Assert.Equal(0, source.Captures);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Core_and_optional_DI_keep_their_package_boundaries()
    {
        var core = typeof(AuthorizedReadRepository<>).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(
            core,
            reference =>
                reference.Name!.StartsWith("Microsoft.Extensions.DependencyInjection")
                || reference.Name.StartsWith("Microsoft.AspNetCore")
        );
        var optional = typeof(ResourceReadOptions).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(
            optional,
            reference =>
                reference.Name!.StartsWith("Microsoft.EntityFrameworkCore")
                || reference.Name.StartsWith("Microsoft.AspNetCore")
        );
    }
}
