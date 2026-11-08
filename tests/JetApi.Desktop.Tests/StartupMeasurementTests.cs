using System.Text.Json;
using JetApi.Desktop.Services;

namespace JetApi.Desktop.Tests;

public sealed class StartupMeasurementTests
{
    [Fact]
    public void FromArgs_ReadsWorkspaceAndReport()
    {
        var measurement = StartupMeasurement.FromArgs(["--measure", "ws", "out/report.json"]).ShouldNotBeNull();

        measurement.Workspace.ShouldBe(Path.GetFullPath("ws"));
        measurement.ReportPath.ShouldBe(Path.GetFullPath("out/report.json"));
    }

    [Theory]
    [InlineData]
    [InlineData("--measure")]
    [InlineData("--measure", "ws")]
    [InlineData("some-file.yaml")]
    public void FromArgs_IsNullForNormalStarts(params string[] args) =>
        StartupMeasurement.FromArgs(args).ShouldBeNull();

    [Fact]
    public async Task Report_IsWrittenAsCamelCaseJson()
    {
        var directory = Path.Combine(Path.GetTempPath(), "jetapi-measure-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var measurement = StartupMeasurement.FromArgs(["--measure", "ws", Path.Combine(directory, "r.json")])!;
            var report = StartupMeasurement.CreateReport(TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(900), 500);

            await measurement.WriteAsync(report);

            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(measurement.ReportPath, TestContext.Current.CancellationToken));
            json.RootElement.GetProperty("workspaceReadyMs").GetDouble().ShouldBe(900);
            json.RootElement.GetProperty("requests").GetInt32().ShouldBe(500);
            json.RootElement.GetProperty("idleWorkingSetMb").GetDouble().ShouldBeGreaterThan(0);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
