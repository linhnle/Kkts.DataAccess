using AdapterConformance;
using Kkts.DataAccess.Testing;

var report = await new AdapterConformanceSuite<ConformanceEntity>().RunAsync(() =>
    new RecordingFixture()
);
Console.WriteLine(
    System.Text.Json.JsonSerializer.Serialize(
        report,
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
    )
);
return report.Results.Any(result => result.Status == ConformanceStatus.Failed) ? 1 : 0;
