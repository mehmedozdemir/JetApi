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
            .With(new Win32PlatformOptions { RenderingMode = WindowsRenderingModes(Environment.GetEnvironmentVariable("JETAPI_RENDERING") ?? SavedRendering()) })
            .WithInterFont()
            .LogToTrace();

    // The rendering choice from the settings window; read before the app starts because it cannot change later.
    private static string SavedRendering()
    {
        var saved = new Services.AppSettingsStore(Services.AppSettingsStore.DefaultFilePath).Load().Rendering;
        ViewModels.SettingsViewModel.StartupRendering = saved;
        return saved == Services.RenderingPreference.Gpu ? "gpu" : "software";
    }

    /// <summary>
    /// Windows renders in software by default: the GPU path (ANGLE/Direct3D) costs about 110 MB of idle memory for no
    /// start-up gain (NFR-2, docs/performance.md). <c>JETAPI_RENDERING=gpu</c> or the setting switches back, e.g. for very large screens.
    /// </summary>
    internal static IReadOnlyList<Win32RenderingMode> WindowsRenderingModes(string? preference) =>
        string.Equals(preference, "gpu", StringComparison.OrdinalIgnoreCase)
            ? [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software]
            : [Win32RenderingMode.Software];
}
