using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kkts.DataAccess;

[Flags]
public enum ReadOperations
{
    None = 0,
    Offset = 1,
    Detail = 2,
    Count = 4,
    Cursor = 8,
    Export = 16,
    Snapshot = 32,
    All = Offset | Detail | Count | Cursor | Export,
}

public enum CapabilityStatus
{
    Supported,
    Unsupported,
    Unverified,
}

public enum ProviderFieldUse
{
    Output,
    Predicate,
    OffsetOrder,
    SeekOrder,
}

public sealed record CapabilityResult
{
    public CapabilityStatus Status { get; }
    public string Reason { get; }

    public CapabilityResult(CapabilityStatus status, string reason)
    {
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Status = status;
        Reason = reason;
    }

    public static CapabilityResult Supported { get; } =
        new(CapabilityStatus.Supported, "The individual model binding is supported.");
    public static CapabilityResult Unverified { get; } =
        new(CapabilityStatus.Unverified, "Translation of this binding is not verified.");
}

public sealed record ProviderMappingScope(
    string Property,
    string ClrType,
    string? ProviderType,
    string? StoreType,
    string? Converter,
    int? Precision,
    int? Scale,
    string? Collation,
    string? ColumnName = null
);

public sealed class ProviderFieldCapabilities
{
    public ProviderMappingScope Mapping { get; }
    public CapabilityResult Output { get; }
    public CapabilityResult OffsetOrder { get; }
    public CapabilityResult SeekOrder { get; }
    public FrozenDictionary<string, CapabilityResult> Predicates { get; }

    public ProviderFieldCapabilities(
        ProviderMappingScope mapping,
        CapabilityResult output,
        CapabilityResult offsetOrder,
        CapabilityResult seekOrder,
        IEnumerable<KeyValuePair<string, CapabilityResult>> predicates
    )
    {
        Mapping = mapping;
        Output = output;
        OffsetOrder = offsetOrder;
        SeekOrder = seekOrder;
        Predicates = predicates.ToFrozenDictionary(StringComparer.Ordinal);
    }
}

public interface IReadProviderCapabilities
{
    string Identity { get; }
    string Resource { get; }
    string SchemaVersion { get; }
    string Provider { get; }
    ReadOperations Operations { get; }
    CapabilityResult SnapshotShape { get; }
    CapabilityResult CheckField(string field, ProviderFieldUse use, string? operation = null);
}

public interface IReadProviderCapabilitySource
{
    ReadOperations Operations { get; }
    IReadProviderCapabilities? Capabilities { get; }
}

public sealed class ReadProviderCapabilities : IReadProviderCapabilities
{
    public string Identity { get; }
    public string Resource { get; }
    public string SchemaVersion { get; }
    public string Provider { get; }
    public string ImplementationVersion { get; }
    public string ModelIdentity { get; }
    public ReadOperations Operations { get; }
    public CapabilityResult SnapshotShape { get; }
    public CapabilityResult ComposedQuery => CapabilityResult.Unverified;
    public FrozenDictionary<string, ProviderFieldCapabilities> Fields { get; }
    public IReadOnlyList<string> KeyFields { get; }

    public ReadProviderCapabilities(
        string resource,
        string schemaVersion,
        string provider,
        string implementationVersion,
        string modelIdentity,
        ReadOperations operations,
        IEnumerable<KeyValuePair<string, ProviderFieldCapabilities>> fields,
        IEnumerable<string> keyFields,
        CapabilityResult snapshotShape
    )
    {
        if (
            operations == ReadOperations.None
            || (operations & ~(ReadOperations.All | ReadOperations.Snapshot)) != 0
            || operations.HasFlag(ReadOperations.Snapshot)
                && !operations.HasFlag(ReadOperations.Export)
        )
            throw new ArgumentException(
                "Snapshot requires Export and at least one declared read operation.",
                nameof(operations)
            );
        Resource = resource;
        SchemaVersion = schemaVersion;
        Provider = provider;
        ImplementationVersion = implementationVersion;
        ModelIdentity = modelIdentity;
        Operations = operations;
        Fields = fields.ToFrozenDictionary(StringComparer.Ordinal);
        KeyFields = Array.AsReadOnly(keyFields.ToArray());
        if (
            KeyFields.Count == 0
            || KeyFields.Distinct(StringComparer.Ordinal).Count() != KeyFields.Count
            || KeyFields.Any(key => string.IsNullOrWhiteSpace(key) || !Fields.ContainsKey(key))
        )
            throw new ArgumentException(
                "Key fields must be distinct registered bindings.",
                nameof(keyFields)
            );
        SnapshotShape = snapshotShape;
        Identity = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(
                        new
                        {
                            resource,
                            schemaVersion,
                            provider,
                            implementationVersion,
                            modelIdentity,
                            operations,
                            keys = KeyFields,
                            snapshotShape,
                            fields = Fields
                                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                                .Select(pair => new { name = pair.Key, value = pair.Value }),
                        }
                    )
                )
            )
        );
    }

    public CapabilityResult CheckField(string field, ProviderFieldUse use, string? operation = null)
    {
        if (!Fields.TryGetValue(field, out var binding))
            return new(
                CapabilityStatus.Unsupported,
                "The field is not registered in this profile."
            );
        return use switch
        {
            ProviderFieldUse.Output => binding.Output,
            ProviderFieldUse.OffsetOrder => binding.OffsetOrder,
            ProviderFieldUse.SeekOrder => binding.SeekOrder,
            ProviderFieldUse.Predicate => operation != null
            && binding.Predicates.TryGetValue(operation, out var result)
                ? result
                : new(
                    CapabilityStatus.Unsupported,
                    "The operator is not registered in this profile."
                ),
            _ => throw new ArgumentOutOfRangeException(nameof(use)),
        };
    }

    public IReadOnlyList<Diagnostic> ValidateRegistration<T>(ResourceSchema<T> schema)
        where T : class
    {
        var diagnostics = new List<Diagnostic>();
        if (Resource != schema.Resource || SchemaVersion != schema.Version)
            throw new ArgumentException(
                "The profile does not match the resource schema.",
                nameof(schema)
            );
        var output =
            (
                Operations
                & (
                    ReadOperations.Offset
                    | ReadOperations.Detail
                    | ReadOperations.Cursor
                    | ReadOperations.Export
                )
            ) != 0;
        var seek = (Operations & (ReadOperations.Cursor | ReadOperations.Export)) != 0;
        foreach (var field in schema.Fields.Values)
        {
            void Check(ProviderFieldUse use, string operation)
            {
                var result = CheckField(field.Name, use);
                if (result.Status == CapabilityStatus.Unsupported)
                    diagnostics.Add(
                        new(
                            DataAccessErrorCode.ProviderUnsupported,
                            JsonPointer.Append("", "operations", operation, "fields", field.Name),
                            result.Reason
                        )
                    );
            }
            if (output && field.Eligibility.Contains(FieldOperation.Select))
                Check(ProviderFieldUse.Output, "output");
            if (
                Operations.HasFlag(ReadOperations.Offset)
                && (
                    field.Eligibility.Contains(FieldOperation.Sort)
                    || KeyFields.Contains(field.Name)
                )
            )
                Check(ProviderFieldUse.OffsetOrder, "offset");
            if (
                seek
                && (
                    field.Eligibility.Contains(FieldOperation.Sort)
                    || KeyFields.Contains(field.Name)
                )
            )
                Check(ProviderFieldUse.SeekOrder, "seek");
        }
        if (
            Operations.HasFlag(ReadOperations.Snapshot)
            && SnapshotShape.Status == CapabilityStatus.Unsupported
        )
            diagnostics.Add(
                new(
                    DataAccessErrorCode.ProviderUnsupported,
                    "/operations/snapshot",
                    SnapshotShape.Reason
                )
            );
        return Array.AsReadOnly(diagnostics.ToArray());
    }
}

public interface IEntityReadPlanValidator<T>
    where T : class
{
    ValueTask ValidatePlanAsync(
        EntityReadPlan<T> plan,
        ReadOperations operation,
        EntitySeekPlan<T>? seek = null,
        IReadOnlyList<IReadProviderCapabilities>? profiles = null,
        CancellationToken cancellation = default
    );
}
