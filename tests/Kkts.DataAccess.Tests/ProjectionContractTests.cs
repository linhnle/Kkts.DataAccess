using System.Text.Json;
using Xunit;

namespace Kkts.DataAccess.Tests;

[Collection("Dto mapping")]
public class ProjectionContractTests
{
    public class Row
    {
        public static int Constructors;
        public static int Getters;

        public Row() => Constructors++;

        public int Id { get; set; }
        public int? Optional { get; set; }
        public string? Text { get; set; }
        public string Name
        {
            get
            {
                Getters++;
                return "unused";
            }
        }
    }

    static EntityReadPlan<Row> Read() =>
        new(
            row => row.Id > 0,
            [new(typeof(Row).GetProperty("Id")!, false)],
            [
                new("optional", typeof(Row).GetProperty("Optional")!),
                new("text", typeof(Row).GetProperty("Text")!),
            ]
        );

    [Fact]
    public void Plans_and_rows_snapshot_ordered_bindings_and_values()
    {
        var bindings = new[] { new EntityOutputBinding("text", typeof(Row).GetProperty("Text")!) };
        var read = new EntityReadPlan<Row>(
            row => row.Id > 0,
            [new(typeof(Row).GetProperty("Id")!, false)],
            bindings
        );
        bindings[0] = new("id", typeof(Row).GetProperty("Id")!);
        object?[] outputs = ["value"];
        object?[] sidecars = [1];
        var row = new ProjectedReadRow(outputs, sidecars);
        outputs[0] = "changed";
        sidecars[0] = 99;
        Assert.Equal("text", Assert.Single(read.OutputBindings).Alias);
        Assert.Equal("value", Assert.Single(row.OutputValues));
        Assert.Equal(1, Assert.Single(row.ContinuationValues));
        row.Validate(read, new EntitySeekPlan<Row>(read, null));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<EntityOutputBinding>)read.OutputBindings)[0] = bindings[0]
        );
        Assert.Throws<NotSupportedException>(() =>
            ((IList<object?>)row.ContinuationValues)[0] = 99
        );
        Assert.DoesNotContain(
            "Continuation",
            JsonSerializer.Serialize(row),
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("99", JsonSerializer.Serialize(row), StringComparison.Ordinal);
        byte[] mutable = [1, 2];
        var defensive = new ProjectedReadRow([], [mutable]);
        mutable[0] = 99;
        Assert.Equal(new byte[] { 1, 2 }, Assert.IsType<byte[]>(defensive.ContinuationValues[0]));
        Assert.Throws<InvalidOperationException>(() => defensive.Validate(read));
    }

    [Fact]
    public void Strict_output_and_sidecar_validation_preserves_nullable_boxing()
    {
        var read = Read();
        var seek = new EntitySeekPlan<Row>(read, null);
        new ProjectedReadRow([42, null]).Validate(read);
        new ProjectedReadRow([null, "text"], [1]).Validate(read, seek);
        foreach (
            var row in new[]
            {
                new ProjectedReadRow([]),
                new ProjectedReadRow([42]),
                new ProjectedReadRow([42L, "text"]),
                new ProjectedReadRow([42, 1]),
                new ProjectedReadRow([42, "text", 1]),
                new ProjectedReadRow([42, "text"], [1]),
            }
        )
            Assert.Throws<InvalidOperationException>(() => row.Validate(read));
        foreach (object?[] boundary in new object?[][] { [], [null], [1L], [1, 2] })
            Assert.Throws<InvalidOperationException>(() =>
                new ProjectedReadRow([42, "text"], boundary).Validate(read, seek)
            );
    }

    [Fact]
    public void Scalar_mapping_never_constructs_entities_or_invokes_getters()
    {
        var schema = new ResourceSchema<Row>(
            "Counter",
            "1",
            [
                ResourceSchema<Row>.Field("id", row => row.Id, [FieldOperation.Select]),
                ResourceSchema<Row>.Field("name", row => row.Name, [FieldOperation.Select]),
            ],
            "id"
        );
        Row.Constructors = Row.Getters = 0;
        var builds = DtoMapper.Cache.BuildCount;
        Assert.Equal(
            "{}",
            DtoMapper.MapValues(schema, [new ProjectedReadRow([])], [])[0].GetRawText()
        );
        Assert.Equal(builds, DtoMapper.Cache.BuildCount);
        var output = DtoMapper.MapValues(
            schema,
            [new ProjectedReadRow(["stored", 1])],
            ["name", "id"]
        );
        Assert.Equal("stored", output[0].GetProperty("name").GetString());
        Assert.Equal(0, Row.Constructors);
        Assert.Equal(0, Row.Getters);
        Assert.Throws<InvalidOperationException>(() =>
            DtoMapper.MapValues(
                schema,
                [new ProjectedReadRow(["valid", 1]), new ProjectedReadRow(["invalid", 1L])],
                ["name", "id"]
            )
        );
    }

    [Fact]
    public void Projected_seek_does_not_require_entity_seek_and_modes_stay_host_only()
    {
        Assert.DoesNotContain(
            typeof(IEntitySeekReadSession<Row>),
            typeof(IEntityProjectedSeekReadSession<Row>).GetInterfaces()
        );
        Assert.Contains(
            typeof(IEntityProjectedReadSession<Row>),
            typeof(IEntityProjectedSeekReadSession<Row>).GetInterfaces()
        );
        foreach (
            var type in new[]
            {
                typeof(ListReadRequest),
                typeof(CursorListReadRequest),
                typeof(CountReadRequest),
                typeof(DetailReadRequest),
                typeof(ExportReadRequest),
            }
        )
        foreach (var member in new[] { "projectionMode", "projection", "projectionExpression" })
            Assert.Throws<JsonException>(() =>
                JsonSerializer.Deserialize(
                    $"{{\"{member}\":\"Required\"}}",
                    type,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)
                )
            );
    }
}
