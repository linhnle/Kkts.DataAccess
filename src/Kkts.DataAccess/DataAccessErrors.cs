using System.Text.Json.Serialization;

namespace Kkts.DataAccess;

[JsonConverter(typeof(JsonStringEnumConverter<DataAccessErrorCategory>))]
public enum DataAccessErrorCategory
{
    InvalidRequest,
    AccessDenied,
    PolicyMissing,
    SchemaIncompatible,
    RevisionConflict,
    ProviderUnsupported,
    InvalidContinuation,
    ExportPolicyChanged,
    ExportDurationExceeded,
}

[JsonConverter(typeof(JsonStringEnumConverter<DataAccessErrorCode>))]
public enum DataAccessErrorCode
{
    [JsonStringEnumMemberName("invalid_request")]
    InvalidRequest,

    [JsonStringEnumMemberName("access_denied")]
    AccessDenied,

    [JsonStringEnumMemberName("policy_missing")]
    PolicyMissing,

    [JsonStringEnumMemberName("schema_incompatible")]
    SchemaIncompatible,

    [JsonStringEnumMemberName("revision_conflict")]
    RevisionConflict,

    [JsonStringEnumMemberName("provider_unsupported")]
    ProviderUnsupported,

    [JsonStringEnumMemberName("invalid_continuation")]
    InvalidContinuation,

    [JsonStringEnumMemberName("export_policy_changed")]
    ExportPolicyChanged,

    [JsonStringEnumMemberName("export_duration_exceeded")]
    ExportDurationExceeded,

    [JsonStringEnumMemberName("invalid_condition")]
    InvalidCondition,

    [JsonStringEnumMemberName("invalid_literal")]
    InvalidLiteral,

    [JsonStringEnumMemberName("unknown_field")]
    UnknownField,

    [JsonStringEnumMemberName("invalid_operator")]
    InvalidOperator,

    [JsonStringEnumMemberName("invalid_context_reference")]
    InvalidContextReference,

    [JsonStringEnumMemberName("invalid_subject")]
    InvalidSubject,

    [JsonStringEnumMemberName("invalid_entry")]
    InvalidEntry,

    [JsonStringEnumMemberName("complexity_exceeded")]
    ComplexityExceeded,

    [JsonStringEnumMemberName("conditional_fields_unsupported")]
    ConditionalFieldsUnsupported,

    [JsonStringEnumMemberName("unknown-property")]
    UnknownProperty,

    [JsonStringEnumMemberName("operator-not-applicable")]
    OperatorNotApplicable,

    [JsonStringEnumMemberName("incompatible-operand")]
    IncompatibleOperand,
}

public sealed record Diagnostic(DataAccessErrorCode Code, string Path, string Message);

public class DataAccessException : Exception
{
    public const int MaxDiagnostics = 256;
    public DataAccessErrorCode Code { get; }
    public DataAccessErrorCategory Category { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public DataAccessException(
        DataAccessErrorCode code,
        string message,
        IEnumerable<Diagnostic>? diagnostics = null,
        Exception? innerException = null
    )
        : base(message, innerException)
    {
        if (!Enum.IsDefined(code))
            throw new ArgumentOutOfRangeException(nameof(code));
        Code = code;
        Category = code switch
        {
            DataAccessErrorCode.AccessDenied => DataAccessErrorCategory.AccessDenied,
            DataAccessErrorCode.PolicyMissing => DataAccessErrorCategory.PolicyMissing,
            DataAccessErrorCode.SchemaIncompatible => DataAccessErrorCategory.SchemaIncompatible,
            DataAccessErrorCode.RevisionConflict => DataAccessErrorCategory.RevisionConflict,
            DataAccessErrorCode.ProviderUnsupported => DataAccessErrorCategory.ProviderUnsupported,
            DataAccessErrorCode.InvalidContinuation => DataAccessErrorCategory.InvalidContinuation,
            DataAccessErrorCode.ExportPolicyChanged => DataAccessErrorCategory.ExportPolicyChanged,
            DataAccessErrorCode.ExportDurationExceeded =>
                DataAccessErrorCategory.ExportDurationExceeded,
            _ => DataAccessErrorCategory.InvalidRequest,
        };
        Diagnostics = Array.AsReadOnly((diagnostics ?? []).Take(MaxDiagnostics).ToArray());
    }
}

public class InvalidRequestException(
    string message = "The request is invalid.",
    IEnumerable<Diagnostic>? diagnostics = null,
    Exception? innerException = null
) : DataAccessException(DataAccessErrorCode.InvalidRequest, message, diagnostics, innerException);

public sealed class AuthorizationException(
    string message = "Access is denied.",
    Exception? innerException = null
) : DataAccessException(DataAccessErrorCode.AccessDenied, message, innerException: innerException);

public class PolicyMissingException(string message = "No active policy is available.")
    : DataAccessException(DataAccessErrorCode.PolicyMissing, message);

public class SchemaIncompatibleException(
    string message = "The schema is incompatible.",
    Exception? innerException = null
)
    : DataAccessException(
        DataAccessErrorCode.SchemaIncompatible,
        message,
        innerException: innerException
    );

public class RevisionConflictException(
    string message = "The revision conflicts with current state."
) : DataAccessException(DataAccessErrorCode.RevisionConflict, message);

public class ProviderUnsupportedException(
    string message = "The requested operation is unsupported.",
    IEnumerable<Diagnostic>? diagnostics = null
) : DataAccessException(DataAccessErrorCode.ProviderUnsupported, message, diagnostics);

public static class JsonPointer
{
    public static string Append(string root, params string[] segments)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root.Length != 0 && root[0] != '/')
            throw new ArgumentException(
                "A JSON Pointer must be empty or start with '/'.",
                nameof(root)
            );
        return root
            + string.Concat(
                segments.Select(segment =>
                    "/"
                    + segment
                        .Replace("~", "~0", StringComparison.Ordinal)
                        .Replace("/", "~1", StringComparison.Ordinal)
                )
            );
    }
}

internal sealed class DiagnosticBuffer : List<Diagnostic>
{
    public new void Add(Diagnostic diagnostic)
    {
        if (Count < DataAccessException.MaxDiagnostics)
            base.Add(diagnostic);
    }

    public new void AddRange(IEnumerable<Diagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics.Take(DataAccessException.MaxDiagnostics - Count))
            base.Add(diagnostic);
    }
}
