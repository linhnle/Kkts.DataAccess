using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kkts.DataAccess;

public sealed record SortField(string Field, bool Descending = false);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ListReadRequest(
    string[]? Selection = null,
    Condition? Filter = null,
    SortField[]? Sort = null,
    int Offset = 0,
    int Limit = 50,
    bool IncludeTotal = true,
    bool IncludeHasMore = false
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CountReadRequest(Condition? Filter = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DetailReadRequest(
    JsonElement Key,
    string[]? Selection = null,
    Condition? Filter = null
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ExportReadRequest(
    string[]? Selection = null,
    Condition? Filter = null,
    SortField[]? Sort = null
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CursorListReadRequest(
    string[]? Selection = null,
    Condition? Filter = null,
    SortField[]? Sort = null,
    int Limit = 50,
    string? ContinuationToken = null,
    bool IncludeTotal = true,
    bool IncludeHasMore = false
);

public sealed record CursorReadPage(
    long PolicyVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Total,
    IReadOnlyList<JsonElement> Records,
    string? NextContinuationToken,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] bool? HasMore = null
);

public sealed record ReadPage(
    long PolicyVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Total,
    IReadOnlyList<JsonElement> Records,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] bool? HasMore = null
);

public sealed record ReadCount(
    long PolicyVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long Total
);

public sealed record ReadDetail(long PolicyVersion, JsonElement? Record);

public sealed record ExportBatch(long PolicyVersion, IReadOnlyList<JsonElement> Records);

public sealed record ReadLimits(
    int MaxPageSize = 200,
    int MaxOffset = 1_000_000,
    int ExportBatchSize = 100
);
