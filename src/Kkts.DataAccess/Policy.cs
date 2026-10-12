using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kkts.DataAccess;

public enum Effect
{
    Allow,
    Deny,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Condition
{
    public Condition[]? All { get; init; }
    public Condition[]? Any { get; init; }
    public Condition? Not { get; init; }
    public string? Field { get; init; }
    public string? Operator { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Literal { get; init; }
    public string? Context { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Subject(string[] Roles, string[] Groups, Condition? ContextCondition = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecordRule(
    string Id,
    string Resource,
    string Action,
    Effect Effect,
    Subject Subject,
    Condition? Condition
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FieldEntry(
    string Id,
    string Resource,
    string Action,
    Effect Effect,
    Subject Subject,
    Dictionary<FieldOperation, string[]> Fields
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PolicyDefinition(
    string Resource,
    string SchemaVersion,
    RecordRule[] Rules,
    FieldEntry[] FieldEntries
);

public sealed record PolicyLimits(
    int MaxDepth = 12,
    int MaxNodes = 256,
    int MaxLiteralBytes = 4096,
    int MaxCollection = 100,
    int MaxEntries = 100
);

public static class PolicyJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            MaxDepth = 64,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.MakeReadOnly(true);
        return options;
    }

    public static string Serialize(PolicyDefinition value) =>
        JsonSerializer.Serialize(value, Options);

    public static PolicyDefinition Deserialize(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 1_048_576)
            throw new JsonException("Policy transport exceeds 1 MiB.");
        try
        {
            return JsonSerializer.Deserialize<PolicyDefinition>(json, Options)
                ?? throw new JsonException("Policy cannot be null.");
        }
        catch (JsonException ex) when (ex.Message.Contains("'fields'", StringComparison.Ordinal))
        {
            throw new JsonException(
                "Record-dependent field grants are unsupported. Use separate context-wide fieldEntries. "
                    + ex.Message,
                ex
            );
        }
    }
}
