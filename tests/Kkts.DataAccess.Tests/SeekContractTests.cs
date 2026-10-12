using Xunit;

namespace Kkts.DataAccess.Tests;

public class SeekContractTests
{
    public class Row
    {
        public int Id { get; set; }
        public int? Optional { get; set; }
        public string? Text { get; set; }
        public string Required { get; set; } = "";
        public object Unsupported { get; set; } = new();
        public static int Static => 1;
        public int this[int index] => index;
        public int WriteOnly
        {
            set { }
        }
    }

    static EntitySort Sort(string name) => new(typeof(Row).GetProperty(name)!, false);

    static EntityReadPlan<Row> Read(params EntitySort[] sorts) => new(row => row.Id > 0, sorts);

    [Fact]
    public void Read_and_boundary_snapshots_are_independent_of_caller_collections()
    {
        var sorts = new[] { Sort("Id"), Sort("Optional"), Sort("Text") };
        var read = Read(sorts);
        object?[] boundary = [1, 2, "value"];
        var seek = new EntitySeekPlan<Row>(read, boundary);
        sorts[0] = Sort("Required");
        boundary[0] = 99;
        Assert.Equal("Id", seek.Read.Sort[0].Property.Name);
        Assert.Equal(new object?[] { 1, 2, "value" }, seek.Boundary);
        Assert.Throws<NotSupportedException>(() => ((IList<object?>)seek.Boundary!)[0] = 99);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<EntitySort>)seek.Read.Sort)[0] = Sort("Text")
        );
        Assert.True(seek.Read.Predicate.Compile()(new() { Id = 1 }));
        Assert.False(seek.Read.Predicate.Compile()(new() { Id = 0 }));
    }

    [Fact]
    public void Fresh_seek_has_no_boundary_and_nullable_boxing_is_valid()
    {
        var read = Read(Sort("Id"), Sort("Optional"), Sort("Text"));
        Assert.Null(new EntitySeekPlan<Row>(read, null).Boundary);
        Assert.Equal(
            new object?[] { 1, null, null },
            new EntitySeekPlan<Row>(read, [1, null, null]).Boundary
        );
        int? optional = 2;
        Assert.Equal(2, new EntitySeekPlan<Row>(read, [1, optional, ""]).Boundary![1]);
    }

    [Fact]
    public void Wrong_cardinality_types_and_required_nulls_fail_explicitly()
    {
        var read = Read(Sort("Id"), Sort("Optional"), Sort("Text"));
        foreach (
            object?[] values in new object?[][]
            {
                [],
                [1, 2],
                [1, 2, "", 3],
                [1L, 2, ""],
                [1, 2L, ""],
                [1, 2, 3],
                [null, 2, ""],
            }
        )
            Assert.Throws<ArgumentException>(() => new EntitySeekPlan<Row>(read, values));
        Assert.Throws<ArgumentException>(() =>
            new EntitySeekPlan<Row>(Read(Sort("Required")), [null])
        );
    }

    [Fact]
    public void Invalid_bindings_and_empty_order_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new EntitySeekPlan<Row>(Read(), null));
        foreach (var name in new[] { "Unsupported", "Static", "Item", "WriteOnly" })
            Assert.Throws<ArgumentException>(() => new EntitySeekPlan<Row>(Read(Sort(name)), null));
        Assert.Throws<ArgumentException>(() =>
            new EntitySeekPlan<Row>(
                Read(new EntitySort(typeof(CompilerTests.Employee).GetProperty("Id")!, false)),
                null
            )
        );
    }

    [Fact]
    public void Seek_is_optional_and_preserves_existing_session_methods()
    {
        Assert.Contains(
            typeof(IEntityReadSession<Row>),
            typeof(IEntitySeekReadSession<Row>).GetInterfaces()
        );
        Assert.Equal(
            new[] { "CountAsync", "PageAsync", "SingleAsync" },
            typeof(IEntityReadSession<Row>).GetMethods().Select(m => m.Name).Order()
        );
    }
}
