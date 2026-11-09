using Avalonia;
using Avalonia.Headless;
using Avalonia.Logging;
namespace JetApi.Desktop.Tests;

/// <summary>Runs the real application (styles, templates, Skia rendering) without a display.</summary>
public static class HeadlessApp
{
    private static readonly Lazy<HeadlessUnitTestSession> SharedSession = new(() => HeadlessUnitTestSession.StartNew(typeof(HeadlessApp)));

    /// <summary>Binding and layout problems logged while views are rendered.</summary>
    public static CollectingLogSink Log { get; } = new();

    /// <summary>One Avalonia application per test run; every UI test dispatches onto its UI thread.</summary>
    public static HeadlessUnitTestSession Session => SharedSession.Value;

    public static AppBuilder BuildAvaloniaApp()
    {
        Logger.Sink = Log;
        return AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont();
    }
}

/// <summary>Collects binding errors so tests can fail on them.</summary>
public sealed class CollectingLogSink : ILogSink
{
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_messages)
            {
                return [.. _messages];
            }
        }
    }

    public void Clear()
    {
        lock (_messages)
        {
            _messages.Clear();
        }
    }

    public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning && area == LogArea.Binding;

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) => Log(level, area, source, messageTemplate, []);

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        if (!IsEnabled(level, area))
        {
            return;
        }

        var message = messageTemplate;
        foreach (var value in propertyValues)
        {
            var start = message.IndexOf('{', StringComparison.Ordinal);
            var end = start < 0 ? -1 : message.IndexOf('}', start);
            if (end < 0)
            {
                break;
            }

            message = string.Concat(message.AsSpan(0, start), value?.ToString(), message.AsSpan(end + 1));
        }

        lock (_messages)
        {
            _messages.Add($"{source?.GetType().Name}: {message}");
        }
    }
}

/// <summary>The modifier of the app's shortcuts: Cmd on macOS, Ctrl elsewhere (as in MainWindow).</summary>
internal static class Shortcut
{
    public static Avalonia.Input.RawInputModifiers Command =>
        OperatingSystem.IsMacOS() ? Avalonia.Input.RawInputModifiers.Meta : Avalonia.Input.RawInputModifiers.Control;
}
