using System.Text.Json;
using Kkts.Dynamic;
using Xunit;

namespace Kkts.DataAccess.Tests;

[CollectionDefinition("Dto mapping", DisableParallelization = true)]
public class DtoMappingCollection;

[Collection("Dto mapping")]
public class DtoShapeCacheTests
{
    public class OtherRow
    {
        public int Id { get; set; }
    }

    public class BlockingRow
    {
        public required ManualResetEventSlim Entered { get; init; }
        public required ManualResetEventSlim Release { get; init; }
        public int Id
        {
            get
            {
                Entered.Set();
                if (!Release.Wait(TimeSpan.FromSeconds(30)))
                    throw new TimeoutException("Mapping was not released.");
                return 42;
            }
        }
    }

    static DtoShapeKey Key(
        string[]? selection = null,
        string version = "1",
        string alias = "displayName",
        bool alternate = false,
        string resource = "MapperRow"
    ) =>
        DtoShapeKey.Create(
            DtoMapperTests.Schema(version, alias, alternate, resource),
            selection ?? ["id", alias]
        );

    [Fact]
    public void Serialization_metadata_is_reused_but_not_shared_with_default_options()
    {
        var cache = new DtoShapeCache();
        var shape = cache.GetOrBuild(Key(["id"]));
        shape.Map(new DtoMapperTests.Row { Id = 1 });
        var metadata = shape.Options.GetTypeInfo(shape.Type);
        shape.Map(new DtoMapperTests.Row { Id = 2 });
        Assert.Equal("{\"id\":3}", shape.MapValues([3]).GetRawText());
        Assert.True(shape.Options.IsReadOnly);
        Assert.Same(metadata, shape.Options.GetTypeInfo(shape.Type));
        Assert.NotSame(metadata, JsonSerializerOptions.Default.GetTypeInfo(shape.Type));
    }

    [Fact]
    public void Structural_keys_reuse_equivalent_schemas_and_snapshot_selection()
    {
        var cache = new DtoShapeCache();
        string[] selection = ["id", "displayName"];
        var key = Key(selection);
        selection[0] = "unknown";
        var shape = cache.GetOrBuild(key);
        Assert.Equal(key, Key());
        Assert.Equal(key.GetHashCode(), Key().GetHashCode());
        Assert.Same(shape, cache.GetOrBuild(Key()));
        Assert.Equal(1, cache.BuildCount);
        Assert.Equal(
            "{\"displayName\":\"Name\",\"id\":1}",
            shape.Map(new DtoMapperTests.Row { Id = 1, Name = "Name" }).GetRawText()
        );
    }

    [Fact]
    public void Different_bindings_orders_labels_and_entity_types_do_not_collide()
    {
        var cache = new DtoShapeCache();
        var original = cache.GetOrBuild(Key());
        DtoShapeKey[] different =
        [
            Key(["displayName", "id"]),
            Key(["id"]),
            Key(version: "2"),
            Key(alias: "name"),
            Key(alternate: true),
            Key(resource: "DifferentResource"),
            DtoShapeKey.Create(
                new ResourceSchema<OtherRow>(
                    "MapperRow",
                    "1",
                    [ResourceSchema<OtherRow>.Field("id", r => r.Id, [FieldOperation.Select])],
                    "id"
                ),
                ["id"]
            ),
        ];
        foreach (var key in different)
            Assert.NotSame(original.Type, cache.GetOrBuild(key).Type);
        Assert.Equal(8, cache.Count);
        var row = new DtoMapperTests.Row
        {
            Id = 1,
            Name = "Name",
            Other = "Other",
        };
        Assert.Equal(
            "Other",
            cache.GetOrBuild(Key(alternate: true)).Map(row).GetProperty("displayName").GetString()
        );
    }

    [Fact]
    public void Default_capacity_enforces_the_exact_128_entry_limit()
    {
        var cache = new DtoShapeCache();
        DtoShape? oldest = null;
        for (var i = 0; i < 129; i++)
        {
            var shape = cache.GetOrBuild(Key(["id"], version: i.ToString()));
            oldest ??= shape;
            Assert.Equal(Math.Min(i + 1, 128), cache.Count);
        }
        Assert.NotNull(oldest);
        Assert.NotSame(oldest.Type, cache.GetOrBuild(Key(["id"], version: "0")).Type);
        Assert.Equal(128, cache.Count);
        Assert.Equal(130, cache.BuildCount);
    }

    [Fact]
    public void Fifo_is_bounded_across_entity_types_and_hits_do_not_reorder()
    {
        Assert.Equal(128, DtoShapeCache.DefaultCapacity);
        Assert.Throws<ArgumentOutOfRangeException>(() => new DtoShapeCache(0));
        var cache = new DtoShapeCache(2);
        var first = cache.GetOrBuild(Key(["id"]));
        var otherKey = DtoShapeKey.Create(
            new ResourceSchema<OtherRow>(
                "Other",
                "1",
                [ResourceSchema<OtherRow>.Field("id", r => r.Id, [FieldOperation.Select])],
                "id"
            ),
            ["id"]
        );
        var second = cache.GetOrBuild(otherKey);
        Assert.Same(first, cache.GetOrBuild(Key(["id"])));
        cache.GetOrBuild(Key(["displayName"]));
        Assert.Equal(2, cache.Count);
        Assert.Same(second, cache.GetOrBuild(otherKey));
        Assert.NotSame(first.Type, cache.GetOrBuild(Key(["id"])).Type);
        Assert.Equal(2, cache.Count);
        Assert.Equal(4, cache.BuildCount);
        Assert.Equal(7, first.Map(new DtoMapperTests.Row { Id = 7 }).GetProperty("id").GetInt32());
    }

    [Fact]
    public void Failed_builds_dispose_builders_without_evicting_or_poisoning_entries()
    {
        var assemblies = new List<DynamicAssembly>();
        var cache = new DtoShapeCache(
            1,
            () =>
            {
                var assembly = new DynamicAssembly();
                assemblies.Add(assembly);
                return assembly;
            }
        );
        var healthy = cache.GetOrBuild(Key(["id"]));
        for (var attempt = 0; attempt < 2; attempt++)
        {
            Assert.Throws<ArgumentException>(() =>
                cache.GetOrBuild(Key(["displayName", "displayName"]))
            );
            Assert.Equal(1, cache.Count);
            Assert.Same(healthy, cache.GetOrBuild(Key(["id"])));
        }
        Assert.Equal(3, cache.BuildCount);
        Assert.Equal(3, assemblies.Count);
        foreach (var assembly in assemblies)
            Assert.Throws<ObjectDisposedException>(() => _ = assembly.Classes);
        Assert.Equal(
            "Recovered",
            cache
                .GetOrBuild(Key(["displayName"]))
                .Map(new DtoMapperTests.Row { Name = "Recovered" })
                .GetProperty("displayName")
                .GetString()
        );
    }

    [Fact]
    public void Transient_failure_retries_with_a_fresh_builder()
    {
        DynamicAssembly? failed = null;
        var attempts = 0;
        var cache = new DtoShapeCache(createAssembly: () =>
        {
            var assembly = new DynamicAssembly();
            if (++attempts == 1)
            {
                failed = assembly;
                assembly.DeclareClass<OtherRow>(
                    "AlreadyBuilt",
                    b => b.Map(r => r.Id, "id", BindingMode.OneWayToDto)
                );
                assembly.Build();
            }
            return assembly;
        });
        Assert.Throws<InvalidOperationException>(() => cache.GetOrBuild(Key(["id"])));
        Assert.Equal(0, cache.Count);
        Assert.NotNull(failed);
        Assert.Throws<ObjectDisposedException>(() => _ = failed.Classes);
        cache.GetOrBuild(Key(["id"]));
        Assert.Equal(2, cache.BuildCount);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public async Task Concurrent_first_use_builds_once_and_maps_independent_values()
    {
        var cache = new DtoShapeCache();
        using var barrier = new Barrier(9);
        var tasks = Enumerable
            .Range(0, 8)
            .Select(i =>
                Task.Factory.StartNew(
                    () =>
                    {
                        Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
                        var shape = cache.GetOrBuild(Key());
                        return (
                            shape,
                            record: i % 2 == 0
                                ? shape.Map(new DtoMapperTests.Row { Id = i, Name = "Name" + i })
                                : shape.MapValues([i, "Name" + i])
                        );
                    },
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default
                )
            )
            .ToArray();
        Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
        var results = await Task.WhenAll(tasks);
        Assert.Equal(1, cache.BuildCount);
        Assert.Equal(1, cache.Count);
        for (var i = 0; i < results.Length; i++)
        {
            Assert.Same(results[0].shape, results[i].shape);
            Assert.Equal(i, results[i].record.GetProperty("id").GetInt32());
            Assert.Equal("Name" + i, results[i].record.GetProperty("displayName").GetString());
        }
    }

    [Fact]
    public async Task Eviction_does_not_block_or_invalidate_active_mapping()
    {
        var cache = new DtoShapeCache(1);
        var schema = new ResourceSchema<BlockingRow>(
            "Blocking",
            "1",
            [ResourceSchema<BlockingRow>.Field("id", r => r.Id, [FieldOperation.Select])],
            "id"
        );
        var shape = cache.GetOrBuild(DtoShapeKey.Create(schema, ["id"]));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var task = Task.Run(() =>
            shape.Map(new BlockingRow { Entered = entered, Release = release })
        );
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(30)));
            cache.GetOrBuild(Key(["id"]));
            Assert.Equal(1, cache.Count);
            Assert.Equal(2, cache.BuildCount);
        }
        finally
        {
            release.Set();
        }
        Assert.Equal(42, (await task).GetProperty("id").GetInt32());
    }

    [Fact]
    public void Empty_selection_does_not_build_and_unknown_fields_fail_after_warming()
    {
        var schema = DtoMapperTests.Schema(resource: Guid.NewGuid().ToString());
        DtoMapper.Map(schema, new[] { new DtoMapperTests.Row() }, ["id"]);
        var builds = DtoMapper.Cache.BuildCount;
        Assert.Equal(
            "{}",
            DtoMapper.Map(schema, new[] { new DtoMapperTests.Row() }, [])[0].GetRawText()
        );
        Assert.Equal(builds, DtoMapper.Cache.BuildCount);
        Assert.Equal(
            "{}",
            DtoMapper.MapValues(schema, [new ProjectedReadRow([])], [])[0].GetRawText()
        );
        DtoMapper.MapValues(schema, [new ProjectedReadRow([2])], ["id"]);
        Assert.Equal(builds, DtoMapper.Cache.BuildCount);
        Assert.Throws<InvalidRequestException>(() =>
            DtoMapper.Map(schema, Array.Empty<DtoMapperTests.Row>(), ["id", "unknown"])
        );
    }
}
