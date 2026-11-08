using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JetApi.Desktop.Services;

/// <summary>What one measured start produced (NFR-1 start-up time, NFR-2 idle memory).</summary>
/// <param name="WindowShownMs">Process start until the main window was shown.</param>
/// <param name="WorkspaceReadyMs">Process start until the workspace was loaded and its first frame rendered (NFR-1).</param>
/// <param name="Requests">Requests in the opened workspace.</param>
/// <param name="IdleWorkingSetMb">Working set after <see cref="StartupMeasurement.IdleDelay"/> of idling (NFR-2).</param>
/// <param name="IdlePrivateMb">Private bytes after idling.</param>
/// <param name="ManagedHeapMb">Managed heap after idling, without forcing a collection.</param>
/// <param name="Runtime">Runtime and OS, for the record.</param>
public sealed record StartupReport(
    double WindowShownMs,
    double WorkspaceReadyMs,
    int Requests,
    double IdleWorkingSetMb,
    double IdlePrivateMb,
    double ManagedHeapMb,
    string Runtime);

/// <summary>
/// Measurement mode: <c>JetApi --measure &lt;workspace&gt; &lt;report.json&gt;</c> opens the workspace without
/// touching the user's settings, records start-up time and idle memory, writes a JSON report and exits.
/// <c>build/measure.sh</c> drives it.
/// </summary>
public sealed class StartupMeasurement
{
    /// <summary>How long the app idles before memory is read.</summary>
    public static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, NumberHandling = JsonNumberHandling.Strict };

    private StartupMeasurement(string workspace, string reportPath)
    {
        Workspace = workspace;
        ReportPath = reportPath;
    }

    /// <summary>The workspace to open.</summary>
    public string Workspace { get; }

    /// <summary>Where the JSON report goes.</summary>
    public string ReportPath { get; }

    /// <summary>Returns the measurement settings when <paramref name="args"/> ask for one, otherwise <c>null</c>.</summary>
    public static StartupMeasurement? FromArgs(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var index = args.ToList().IndexOf("--measure");
        return index >= 0 && index + 2 < args.Count
            ? new StartupMeasurement(Path.GetFullPath(args[index + 1]), Path.GetFullPath(args[index + 2]))
            : null;
    }

    /// <summary>Time since the process started.</summary>
    public static TimeSpan SinceProcessStart()
    {
        using var process = Process.GetCurrentProcess();
        return DateTime.Now - process.StartTime;
    }

    /// <summary>Reads memory after idling and builds the report.</summary>
    public static StartupReport CreateReport(TimeSpan windowShown, TimeSpan workspaceReady, int requests)
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return new StartupReport(
            Math.Round(windowShown.TotalMilliseconds),
            Math.Round(workspaceReady.TotalMilliseconds),
            requests,
            Megabytes(process.WorkingSet64),
            Megabytes(process.PrivateMemorySize64),
            Megabytes(GC.GetTotalMemory(forceFullCollection: false)),
            $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}; {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
    }

    /// <summary>Writes the report as JSON.</summary>
    public async Task WriteAsync(StartupReport report)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)!);
        await File.WriteAllTextAsync(ReportPath, JsonSerializer.Serialize(report, JsonOptions)).ConfigureAwait(false);
    }

    private static double Megabytes(long bytes) => Math.Round(bytes / 1024d / 1024d, 1);
}
