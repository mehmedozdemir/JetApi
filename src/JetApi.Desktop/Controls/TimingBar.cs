using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace JetApi.Desktop.Controls;

/// <summary>A horizontal bar showing one phase of a request on the total time axis.</summary>
public sealed class TimingBar : Control
{
    /// <summary>Start, 0..1.</summary>
    public static readonly StyledProperty<double> OffsetProperty = AvaloniaProperty.Register<TimingBar, double>(nameof(Offset));

    /// <summary>Length, 0..1.</summary>
    public static readonly StyledProperty<double> LengthProperty = AvaloniaProperty.Register<TimingBar, double>(nameof(Length));

    /// <summary>Bar color.</summary>
    public static readonly StyledProperty<IBrush?> BarProperty = AvaloniaProperty.Register<TimingBar, IBrush?>(nameof(Bar));

    /// <summary>Track color.</summary>
    public static readonly StyledProperty<IBrush?> TrackProperty = AvaloniaProperty.Register<TimingBar, IBrush?>(nameof(Track));

    /// <summary>Phase name; selects the bar color from the theme (Timing.Dns, Timing.Total, ...).</summary>
    public static readonly StyledProperty<string?> PhaseProperty = AvaloniaProperty.Register<TimingBar, string?>(nameof(Phase));

    private const double MinimumVisibleWidth = 2;

    private IDisposable? _barBinding;

    /// <summary>Creates the bar.</summary>
    public TimingBar()
    {
        Height = 10;
        Bind(TrackProperty, this.GetResourceObservable("Timing.Track"));
    }

    /// <summary>Phase name.</summary>
    public string? Phase
    {
        get => GetValue(PhaseProperty);
        set => SetValue(PhaseProperty, value);
    }

    static TimingBar()
    {
        AffectsRender<TimingBar>(OffsetProperty, LengthProperty, BarProperty, TrackProperty);
    }

    /// <summary>Start, 0..1.</summary>
    public double Offset
    {
        get => GetValue(OffsetProperty);
        set => SetValue(OffsetProperty, value);
    }

    /// <summary>Length, 0..1.</summary>
    public double Length
    {
        get => GetValue(LengthProperty);
        set => SetValue(LengthProperty, value);
    }

    /// <summary>Bar color.</summary>
    public IBrush? Bar
    {
        get => GetValue(BarProperty);
        set => SetValue(BarProperty, value);
    }

    /// <summary>Track color.</summary>
    public IBrush? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PhaseProperty)
        {
            _barBinding?.Dispose();
            _barBinding = Bind(BarProperty, this.GetResourceObservable("Timing." + (Phase ?? "Total")));
        }
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var bounds = new Rect(Bounds.Size);
        context.DrawRectangle(Track, null, bounds, 3, 3);
        var x = Math.Clamp(Offset, 0, 1) * bounds.Width;
        var width = Math.Max(MinimumVisibleWidth, Math.Clamp(Length, 0, 1) * bounds.Width);
        context.DrawRectangle(Bar, null, new Rect(Math.Min(x, bounds.Width - width), 0, width, bounds.Height), 3, 3);
    }
}
