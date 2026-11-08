using Avalonia;

namespace JetApi.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions { RenderingMode = WindowsRenderingModes(Environment.GetEnvironmentVariable("JETAPI_RENDERING")) })
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// Windows renders in software by default: the GPU path (ANGLE/Direct3D) costs about 110 MB of idle memory for no
    /// start-up gain (NFR-2, docs/performance.md). <c>JETAPI_RENDERING=gpu</c> switches back, e.g. for very large screens.
    /// </summary>
    internal static IReadOnlyList<Win32RenderingMode> WindowsRenderingModes(string? preference) =>
        string.Equals(preference, "gpu", StringComparison.OrdinalIgnoreCase)
            ? [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software]
            : [Win32RenderingMode.Software];
}
