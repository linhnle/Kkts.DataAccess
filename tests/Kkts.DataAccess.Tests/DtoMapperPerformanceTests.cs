using System.Diagnostics;
using System.Runtime.InteropServices;
using Xunit;
using Xunit.Abstractions;

namespace Kkts.DataAccess.Tests;

public sealed class DtoPerformanceFactAttribute : FactAttribute
{
    public DtoPerformanceFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("KKTS_DTO_PERFORMANCE") != "1")
            Skip = "Set KKTS_DTO_PERFORMANCE=1 to run the bounded mapper measurement.";
    }
}

[Collection("Dto mapping")]
public class DtoMapperPerformanceTests(ITestOutputHelper output)
{
    const int Repetitions = 5;
    const int Iterations = 100;
    const int Warmups = 20;

    readonly record struct Measurement(double Milliseconds, long Bytes);

    [DtoPerformanceFact]
    public void Compare_uncached_and_resident_mapping()
    {
        output.WriteLine(
            $"{RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription}; "
                + $"{RuntimeInformation.ProcessArchitecture}; repetitions={Repetitions}, "
                + $"iterations={Iterations}, warmups={Warmups}"
        );
        foreach (var size in new[] { 1, 100 })
        {
            var schema = DtoMapperTests.Schema(resource: "Performance-" + size);
            var rows = Enumerable
                .Range(0, size)
                .Select(i => new DtoMapperTests.Row { Id = i, Name = "Name" + i })
                .ToArray();
            string[] selection = ["id", "displayName"];
            Action baseline = () => DtoMapperTests.MapUncached(schema, rows, selection);
            Action cached = () => DtoMapper.Map(schema, rows, selection);
            Assert.Equal(
                DtoMapperTests.MapUncached(schema, rows, selection).Select(r => r.GetRawText()),
                DtoMapper.Map(schema, rows, selection).Select(r => r.GetRawText())
            );
            for (var i = 0; i < Warmups; i++)
            {
                baseline();
                cached();
            }
            var builds = DtoMapper.Cache.BuildCount;
            var uncached = new Measurement[Repetitions];
            var resident = new Measurement[Repetitions];
            for (var i = 0; i < Repetitions; i++)
            {
                // Alternate order so the same implementation is not always measured first.
                if (i % 2 == 0)
                {
                    uncached[i] = Measure(baseline, Iterations);
                    resident[i] = Measure(cached, Iterations);
                }
                else
                {
                    resident[i] = Measure(cached, Iterations);
                    uncached[i] = Measure(baseline, Iterations);
                }
            }
            Assert.Equal(builds, DtoMapper.Cache.BuildCount);
            Report($"rows={size} uncached", uncached);
            Report($"rows={size} resident", resident);
            output.WriteLine($"rows={size} resident-hit builds=0");

            var cold = new Measurement[Repetitions];
            for (var i = 0; i < Repetitions; i++)
            {
                var coldSchema = DtoMapperTests.Schema(resource: $"Cold-{size}-{i}");
                cold[i] = Measure(() => DtoMapper.Map(coldSchema, rows, selection), 1);
            }
            Assert.Equal(builds + Repetitions, DtoMapper.Cache.BuildCount);
            Report($"rows={size} cold miss", cold);
        }
        MeasureConcurrentMisses();
    }

    void MeasureConcurrentMisses()
    {
        var cache = new DtoShapeCache();
        using var barrier = new Barrier(9);
        var tasks = Enumerable
            .Range(0, 8)
            .Select(i =>
            {
                var key = DtoShapeKey.Create(
                    DtoMapperTests.Schema(resource: "Concurrent-" + i),
                    ["id", "displayName"]
                );
                return Task.Factory.StartNew(
                    () =>
                    {
                        Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
                        return Measure(
                            () => cache.GetOrBuild(key).Map(new DtoMapperTests.Row { Id = i }),
                            1
                        );
                    },
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default
                );
            })
            .ToArray();
        Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
        Task.WaitAll(tasks);
        Assert.Equal(8, cache.BuildCount);
        Assert.Equal(8, cache.Count);
        var measurements = tasks.Select(task => task.Result).ToArray();
        Report("8 concurrent distinct cold misses (includes lock wait)", measurements);
        output.WriteLine(
            $"concurrent cold misses max={measurements.Max(m => m.Milliseconds):F6} ms; builds=8"
        );
    }

    static Measurement Measure(Action action, int iterations)
    {
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++)
            action();
        var elapsed = Stopwatch.GetElapsedTime(start);
        return new(
            elapsed.TotalMilliseconds / iterations,
            (GC.GetAllocatedBytesForCurrentThread() - allocated) / iterations
        );
    }

    void Report(string label, Measurement[] values) =>
        output.WriteLine(
            $"{label}: median={values.Select(v => v.Milliseconds).Order().ElementAt(values.Length / 2):F6} ms/op; "
                + $"{values.Select(v => v.Bytes).Order().ElementAt(values.Length / 2)} bytes/op"
        );
}
