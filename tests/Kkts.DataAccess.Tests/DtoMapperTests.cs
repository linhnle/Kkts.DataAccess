using System.Text.Json;
using Kkts.Dynamic;
using Xunit;

namespace Kkts.DataAccess.Tests;

[Collection("Dto mapping")]
public class DtoMapperTests
{
    public class Row
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Other { get; set; } = "";
    }

    public class ScalarRow<TValue>
    {
        public int Id { get; set; }
        public TValue Value { get; set; } = default!;
    }

    public enum Status
    {
        Active,
    }

    internal static ResourceSchema<Row> Schema(
        string version = "1",
        string alias = "displayName",
        bool alternate = false,
        string resource = "MapperRow"
    ) =>
        new(
            resource,
            version,
            [
                ResourceSchema<Row>.Field("id", r => r.Id, [FieldOperation.Select]),
                alternate
                    ? ResourceSchema<Row>.Field(alias, r => r.Other, [FieldOperation.Select])
                    : ResourceSchema<Row>.Field(alias, r => r.Name, [FieldOperation.Select]),
            ],
            "id"
        );

    internal static IReadOnlyList<JsonElement> MapUncached<T>(
        ResourceSchema<T> schema,
        IReadOnlyList<T> entities,
        IReadOnlyList<string> selection
    )
        where T : class
    {
        if (selection.Count == 0)
            return Array.AsReadOnly(
                entities
                    .Select(_ =>
                        JsonSerializer.SerializeToElement(new Dictionary<string, object>())
                    )
                    .ToArray()
            );
        using var assembly = new DynamicAssembly();
        var shape = assembly.DeclareClass(
            "AuthorizedDto",
            typeof(T),
            selection.Select(name =>
            {
                var field = schema.Resolve(name);
                return new PropertyBinding
                {
                    EntityProperty = field.Property.Name,
                    DtoProperty = field.Name,
                    Mode = BindingMode.OneWayToDto,
                };
            })
        );
        assembly.Build();
        return Array.AsReadOnly(
            entities
                .Select(entity =>
                {
                    var dto = shape.CreateDto(entity);
                    return JsonSerializer.SerializeToElement(dto, dto.GetType());
                })
                .ToArray()
        );
    }

    [Fact]
    public void Mapper_matches_uncached_ordered_aliases_and_empty_selection()
    {
        var schema = Schema();
        Row[] rows =
        [
            new()
            {
                Id = 1,
                Name = "<Alice>",
                Other = "Secret",
            },
            new() { Id = 2 },
        ];
        foreach (
            string[] selection in new[]
            {
                new[] { "displayName", "id" },
                ["id", "displayName"],
                ["id"],
                [],
            }
        )
        {
            Assert.Equal(
                MapUncached(schema, rows, selection).Select(r => r.GetRawText()),
                DtoMapper.Map(schema, rows, selection).Select(r => r.GetRawText())
            );
            var projected = rows.Select(row => new ProjectedReadRow(
                    selection.Select(name => schema.Resolve(name).Property.GetValue(row))
                ))
                .ToArray();
            Assert.Equal(
                DtoMapper.Map(schema, rows, selection).Select(r => r.GetRawText()),
                DtoMapper.MapValues(schema, projected, selection).Select(r => r.GetRawText())
            );
        }
        Assert.Equal(
            new[] { "displayName", "id" },
            DtoMapper
                .Map(schema, rows, ["displayName", "id"])[0]
                .EnumerateObject()
                .Select(p => p.Name)
        );
    }

    [Fact]
    public void Mapper_matches_uncached_supported_scalar_and_null_values()
    {
        CompareScalar("escaped \"<>&");
        CompareScalar<string?>(null);
        CompareScalar(true);
        CompareScalar(Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"));
        CompareScalar(new DateTime(2026, 10, 12, 3, 0, 0, DateTimeKind.Utc));
        CompareScalar(new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.FromHours(7)));
        CompareScalar(Status.Active);
        CompareScalar((byte)1);
        CompareScalar((sbyte)-1);
        CompareScalar((short)-2);
        CompareScalar((ushort)2);
        CompareScalar(-3);
        CompareScalar(3u);
        CompareScalar(-4L);
        CompareScalar(4UL);
        CompareScalar(1.25m);
        CompareScalar(1.25d);
        CompareScalar(1.25f);
        CompareScalar<int?>(null);
        CompareScalar<int?>(42);
        CompareScalar<Status?>(null);
    }

    static void CompareScalar<TValue>(TValue value)
    {
        var schema = new ResourceSchema<ScalarRow<TValue>>(
            "Scalar",
            "1",
            [
                ResourceSchema<ScalarRow<TValue>>.Field("id", r => r.Id, [FieldOperation.Select]),
                ResourceSchema<ScalarRow<TValue>>.Field(
                    "value",
                    r => r.Value,
                    [FieldOperation.Select]
                ),
            ],
            "id"
        );
        ScalarRow<TValue>[] rows = [new() { Id = 1, Value = value }];
        Assert.Equal(
            MapUncached(schema, rows, ["value"])[0].GetRawText(),
            DtoMapper.Map(schema, rows, ["value"])[0].GetRawText()
        );
        Assert.Equal(
            DtoMapper.Map(schema, rows, ["value"])[0].GetRawText(),
            DtoMapper.MapValues(schema, [new ProjectedReadRow([value])], ["value"])[0].GetRawText()
        );
    }

    [Fact]
    public void Mapper_matches_uncached_invalid_binding_and_null_failures()
    {
        var schema = Schema();
        foreach (
            string[] selection in new[] { new[] { "unknown" }, ["displayName", "displayName"] }
        )
        {
            var expected = Record.Exception(() =>
                MapUncached(schema, Array.Empty<Row>(), selection)
            );
            var actual = Record.Exception(() =>
                DtoMapper.Map(schema, Array.Empty<Row>(), selection)
            );
            Assert.NotNull(expected);
            Assert.NotNull(actual);
            Assert.Equal(expected.GetType(), actual.GetType());
            Assert.Equal(expected.Message, actual.Message);
        }
        Row[] rows = [null!];
        var baseline = Assert.Throws<ArgumentNullException>(() =>
            MapUncached(schema, rows, ["id"])
        );
        var current = Assert.Throws<ArgumentNullException>(() =>
            DtoMapper.Map(schema, rows, ["id"])
        );
        Assert.Equal(baseline.ParamName, current.ParamName);
        Assert.Equal("{}", DtoMapper.Map(schema, rows, [])[0].GetRawText());
    }
}
