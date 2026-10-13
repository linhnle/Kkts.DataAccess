using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Kkts.DataAccess.DependencyInjection;

public enum ConfigurationErrorCode
{
    MissingRegistration,
    DuplicateRegistration,
    AmbiguousResource,
    InvalidOptions,
    InvalidLifetime,
    InvalidModel,
}

public sealed record ConfigurationDiagnostic(
    ConfigurationErrorCode Code,
    string Path,
    string Message
);

public sealed class DataAccessConfigurationException : Exception
{
    public IReadOnlyList<ConfigurationDiagnostic> Diagnostics { get; }

    public DataAccessConfigurationException(
        ConfigurationDiagnostic diagnostic,
        Exception? innerException = null
    )
        : base(diagnostic.Message, innerException) =>
        Diagnostics = Array.AsReadOnly(new[] { diagnostic });
}

public sealed record ResourceCursorOptions(
    string DatasetId,
    TimeSpan? Lifetime = null,
    int MaxEncodedTokenSize = 16 * 1024,
    TimeProvider? TimeProvider = null
);

public sealed record ResourceReadOptions
{
    public ReadLimits Limits { get; init; } = new();
    public ReadProjectionMode ProjectionMode { get; init; } = ReadProjectionMode.Required;
    public ExportReadConfiguration Export { get; init; } = new();
    public ResourceCursorOptions? Cursor { get; init; }
}

public sealed class ResourceReadOptionsValidator : IValidateOptions<ResourceReadOptions>
{
    public ValidateOptionsResult Validate(string? name, ResourceReadOptions options)
    {
        var failures = new List<string>();
        if (
            options.Limits == null
            || options.Limits.MaxPageSize < 1
            || options.Limits.MaxOffset < 0
            || options.Limits.ExportBatchSize < 1
            || options.Limits.ExportBatchSize > options.Limits.MaxPageSize
        )
            failures.Add(
                "Read limits must be positive and ExportBatchSize must not exceed MaxPageSize."
            );
        if (!Enum.IsDefined(options.ProjectionMode))
            failures.Add("The projection mode is invalid.");
        if (options.Export == null)
            failures.Add("Export options are required.");
        if (options.Cursor is { } cursor)
        {
            if (
                string.IsNullOrWhiteSpace(cursor.DatasetId)
                || cursor.Lifetime is { } lifetime && lifetime <= TimeSpan.Zero
                || cursor.MaxEncodedTokenSize < 1
            )
                failures.Add(
                    "Cursor protection, dataset identity, positive lifetime and token limit are required."
                );
        }
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

public interface IAuthorizedReadRegistrationValidator
{
    Task ValidateAsync(CancellationToken cancellation = default);
}

internal interface IResourceRegistration
{
    Task ValidateAsync(IServiceProvider services, CancellationToken cancellation);
}

internal sealed class AuthorizedReadRegistrationValidator(
    IServiceScopeFactory scopes,
    IEnumerable<IResourceRegistration> registrations
) : IAuthorizedReadRegistrationValidator
{
    public async Task ValidateAsync(CancellationToken cancellation = default)
    {
        foreach (var registration in registrations)
        {
            cancellation.ThrowIfCancellationRequested();
            await using var scope = scopes.CreateAsyncScope();
            await registration.ValidateAsync(scope.ServiceProvider, cancellation);
        }
    }
}

internal sealed class AuthorizedReadStartupValidation(
    IAuthorizedReadRegistrationValidator validator
) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        validator.ValidateAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
