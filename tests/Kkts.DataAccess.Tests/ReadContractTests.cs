using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace Kkts.DataAccess.Tests;

public class ReadContractTests
{
    static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Defaults_and_supported_members_round_trip()
    {
        var list = JsonSerializer.Deserialize<ListReadRequest>("{}", Web)!;
        Assert.Null(list.Selection);
        Assert.Null(list.Filter);
        Assert.Null(list.Sort);
        Assert.Equal(0, list.Offset);
        Assert.Equal(50, list.Limit);
        Assert.True(list.IncludeTotal);
        Assert.False(list.IncludeHasMore);
        Assert.Null(JsonSerializer.Deserialize<CountReadRequest>("{}", Web)!.Filter);
        var export = JsonSerializer.Deserialize<ExportReadRequest>("{}", Web)!;
        Assert.Null(export.Selection);
        Assert.Null(export.Filter);
        Assert.Null(export.Sort);
        var detail = JsonSerializer.Deserialize<DetailReadRequest>("{}", Web)!;
        Assert.Equal(JsonValueKind.Undefined, detail.Key.ValueKind);
        Assert.Null(detail.Selection);
        Assert.Null(detail.Filter);
        var cursor = JsonSerializer.Deserialize<CursorListReadRequest>("{}", Web)!;
        Assert.Equal(50, cursor.Limit);
        Assert.Null(cursor.ContinuationToken);
        Assert.True(cursor.IncludeTotal);
        Assert.False(cursor.IncludeHasMore);
        AssertRoundTrip<CursorListReadRequest>(
            """{"selection":["Name"],"sort":[{"field":"Name"}],"limit":1,"continuationToken":"opaque"}"""
        );

        AssertRoundTrip<ListReadRequest>(
            """{"selection":["Name","Id"],"filter":{"field":"Status","operator":"eq","literal":"Active"},"sort":[{"field":"Name","descending":true}],"offset":2,"limit":3}"""
        );
        AssertRoundTrip<CountReadRequest>(
            """{"filter":{"field":"Status","operator":"eq","literal":"Active"}}"""
        );
        AssertRoundTrip<DetailReadRequest>(
            """{"key":1,"selection":[],"filter":{"field":"Status","operator":"eq","literal":"Active"}}"""
        );
        AssertRoundTrip<ExportReadRequest>(
            """{"selection":["Name"],"filter":{"field":"Status","operator":"eq","literal":"Active"},"sort":[{"field":"Name"}]}"""
        );
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Metadata_flags_round_trip(bool total, bool hasMore)
    {
        var json = JsonSerializer.Serialize(new { includeTotal = total, includeHasMore = hasMore });
        var list = JsonSerializer.Deserialize<ListReadRequest>(json, Web)!;
        var cursor = JsonSerializer.Deserialize<CursorListReadRequest>(json, Web)!;
        Assert.Equal(total, list.IncludeTotal);
        Assert.Equal(hasMore, list.IncludeHasMore);
        Assert.Equal(total, cursor.IncludeTotal);
        Assert.Equal(hasMore, cursor.IncludeHasMore);
        AssertRoundTrip<ListReadRequest>(json);
        AssertRoundTrip<CursorListReadRequest>(json);
    }

    [Theory]
    [InlineData("includeTotal")]
    [InlineData("includeHasMore")]
    public void Metadata_options_are_strict_list_only_booleans(string member)
    {
        foreach (var literal in new[] { "null", "0", "\"true\"", "[]", "{}" })
        {
            var invalid = $"{{\"{member}\":{literal}}}";
            Assert.Throws<JsonException>(() =>
                JsonSerializer.Deserialize<ListReadRequest>(invalid, Web)
            );
            Assert.Throws<JsonException>(() =>
                JsonSerializer.Deserialize<CursorListReadRequest>(invalid, Web)
            );
        }
        var json = $"{{\"{member}\":true}}";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CountReadRequest>(json, Web));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<DetailReadRequest>(json, Web)
        );
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ExportReadRequest>(json, Web)
        );
    }

    [Fact]
    public void Null_metadata_is_explicit_even_when_serializer_ignores_null_or_default()
    {
        foreach (var web in new[] { false, true })
        foreach (
            var ignore in new[]
            {
                JsonIgnoreCondition.Never,
                JsonIgnoreCondition.WhenWritingNull,
                JsonIgnoreCondition.WhenWritingDefault,
            }
        )
        {
            var options = new JsonSerializerOptions(
                web ? JsonSerializerDefaults.Web : JsonSerializerDefaults.General
            )
            {
                DefaultIgnoreCondition = ignore,
            };
            var total = web ? "total" : "Total";
            var hasMore = web ? "hasMore" : "HasMore";
            foreach (
                var page in new object[]
                {
                    new ReadPage(1, null, []),
                    new CursorReadPage(1, null, [], null),
                }
            )
            {
                var json = JsonSerializer.SerializeToElement(page, options);
                Assert.Equal(JsonValueKind.Null, json.GetProperty(total).ValueKind);
                Assert.Equal(JsonValueKind.Null, json.GetProperty(hasMore).ValueKind);
            }
            foreach (
                var page in new object[]
                {
                    new ReadPage(1, 0, [], false),
                    new CursorReadPage(1, 0, [], null, false),
                }
            )
            {
                var json = JsonSerializer.SerializeToElement(page, options);
                Assert.Equal(0, json.GetProperty(total).GetInt64());
                Assert.False(json.GetProperty(hasMore).GetBoolean());
            }
            var count = JsonSerializer.SerializeToElement(new ReadCount(1, 0), options);
            Assert.Equal(0, count.GetProperty(total).GetInt64());
            Assert.Equal(1, count.GetProperty(web ? "policyVersion" : "PolicyVersion").GetInt64());
        }
    }

    static void AssertRoundTrip<T>(string json)
    {
        var value = JsonSerializer.Deserialize<T>(json, Web)!;
        var serialized = JsonSerializer.Serialize(value, Web);
        Assert.Equal(
            serialized,
            JsonSerializer.Serialize(JsonSerializer.Deserialize<T>(serialized, Web), Web)
        );
    }

    [Theory]
    [InlineData("action", "\"Read\"")]
    [InlineData("action", "\"Write\"")]
    [InlineData("action", "\"Aggregate\"")]
    [InlineData("identity", "{}")]
    [InlineData("context", "{}")]
    [InlineData("roles", "[]")]
    [InlineData("unknown", "true")]
    public void Every_request_rejects_overrides_and_unknown_members(string name, string value)
    {
        var json = $"{{\"{name}\":{value}}}";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ListReadRequest>(json, Web));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CountReadRequest>(json, Web));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<DetailReadRequest>(json, Web)
        );
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ExportReadRequest>(json, Web)
        );
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<CursorListReadRequest>(json, Web)
        );
    }

    [Theory]
    [InlineData("selection", "[]")]
    [InlineData("sort", "[]")]
    [InlineData("offset", "0")]
    [InlineData("limit", "1")]
    [InlineData("key", "1")]
    [InlineData("exportBatchSize", "1")]
    public void Count_rejects_nonfilter_members(string name, string value) =>
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<CountReadRequest>($"{{\"{name}\":{value}}}", Web)
        );

    [Theory]
    [InlineData("sort", "[]")]
    [InlineData("offset", "0")]
    [InlineData("limit", "1")]
    [InlineData("exportBatchSize", "1")]
    public void Detail_rejects_non_detail_members(string name, string value) =>
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<DetailReadRequest>($"{{\"key\":1,\"{name}\":{value}}}", Web)
        );

    [Theory]
    [InlineData("offset", "0")]
    [InlineData("limit", "1")]
    [InlineData("key", "1")]
    [InlineData("exportBatchSize", "1")]
    public void Export_rejects_pagination_and_detail_members(string name, string value) =>
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ExportReadRequest>($"{{\"{name}\":{value}}}", Web)
        );

    [Fact]
    public void Strictness_is_type_local_and_applies_with_default_options()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ListReadRequest>("""{"Action":"Read"}""")
        );
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<CountReadRequest>("""{"Limit":1}""")
        );
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<DetailReadRequest>("""{"Key":1,"Sort":[]}""")
        );
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ExportReadRequest>("""{"Offset":0}""")
        );
        Assert.NotNull(JsonSerializer.Deserialize<SortField>("""{"Field":"Name","unknown":1}"""));
    }

    [Fact]
    public void Cursor_rejects_offset_and_export_rejects_resume()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<CursorListReadRequest>("""{"offset":0}""", Web)
        );
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ExportReadRequest>("""{"continuationToken":"opaque"}""", Web)
        );
    }

    [Fact]
    public void Repository_and_facade_expose_only_matching_operation_inputs()
    {
        var expected = new Dictionary<string, Type>
        {
            ["ListAsync"] = typeof(ListReadRequest),
            ["ListCursorAsync"] = typeof(CursorListReadRequest),
            ["CountAsync"] = typeof(CountReadRequest),
            ["DetailAsync"] = typeof(DetailReadRequest),
            ["ExportAsync"] = typeof(ExportReadRequest),
        };
        foreach (
            var type in new[]
            {
                typeof(IAuthorizedReadRepository<CompilerTests.Employee>),
                typeof(AuthorizedReadRepository<CompilerTests.Employee>),
                typeof(Sqlite.AuthorizedReads<CompilerTests.Employee>),
            }
        )
        foreach (var (name, requestType) in expected)
        {
            var method = Assert.Single(type.GetMethods(), m => m.Name == name);
            Assert.Equal(
                new[] { requestType, typeof(CancellationToken) },
                method.GetParameters().Select(p => p.ParameterType)
            );
            if (name == "CountAsync")
                Assert.Equal(typeof(Task<ReadCount>), method.ReturnType);
        }
        Assert.Null(typeof(ListReadRequest).Assembly.GetType("Kkts.DataAccess.ReadRequest"));
    }
}
