using System.Text.Json;

namespace Kkts.DataAccess;

public sealed record SortField(string Field, bool Descending = false);

public sealed record ReadRequest(
    string[]? Selection = null,
    Condition? Filter = null,
    SortField[]? Sort = null,
    int Offset = 0,
    int Limit = 50,
    string Action = "Read"
);

public sealed record ReadPage(long PolicyVersion, long Total, IReadOnlyList<JsonElement> Records);

public sealed record ReadDetail(long PolicyVersion, JsonElement? Record);

public sealed record ExportBatch(long PolicyVersion, IReadOnlyList<JsonElement> Records);

public sealed record ReadLimits(
    int MaxPageSize = 200,
    int MaxOffset = 1_000_000,
    int ExportBatchSize = 100
);
