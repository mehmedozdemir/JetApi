using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace JetApi.Desktop.Controls;

/// <summary>Raised when the user drags the divider of a <see cref="SplitPane"/>.</summary>
public sealed class SplitShareChangedEventArgs(RoutedEvent routedEvent, bool isStacked, double firstShare) : RoutedEventArgs(routedEvent)
{
    /// <summary>The panes are stacked (second below first).</summary>
    public bool IsStacked { get; } = isStacked;

    /// <summary>The share of the first pane, between 0 and 1.</summary>
    public double FirstShare { get; } = firstShare;
}

/// <summary>
/// Two panes with a draggable divider: side by side, or stacked with the second below the first (the request and
/// response panes). The layout and the shares follow the <c>App.ResponseBelow</c>, <c>App.RequestShareRight</c> and
/// <c>App.RequestShareBottom</c> resources, so every open tab switches at once; dragging raises
/// <see cref="ShareChangedEvent"/> so the window can remember the new share.
/// </summary>
public sealed class SplitPane : Grid
{
    /// <summary>The first pane (left or top).</summary>
    public static readonly StyledProperty<Control?> FirstProperty = AvaloniaProperty.Register<SplitPane, Control?>(nameof(First));

    /// <summary>The second pane (right or bottom).</summary>
    public static readonly StyledProperty<Control?> SecondProperty = AvaloniaProperty.Register<SplitPane, Control?>(nameof(Second));

    /// <summary>Stack the panes (second below first) instead of side by side.</summary>
    public static readonly StyledProperty<bool> IsStackedProperty = AvaloniaProperty.Register<SplitPane, bool>(nameof(IsStacked));

    /// <summary>The share of the first pane when side by side.</summary>
    public static readonly StyledProperty<double> SideShareProperty = AvaloniaProperty.Register<SplitPane, double>(nameof(SideShare), 0.5);

    /// <summary>The share of the first pane when stacked.</summary>
    public static readonly StyledProperty<double> StackedShareProperty = AvaloniaProperty.Register<SplitPane, double>(nameof(StackedShare), 0.5);

    /// <summary>Raised (bubbling) after the user dragged the divider.</summary>
    public static readonly RoutedEvent<SplitShareChangedEventArgs> ShareChangedEvent =
        RoutedEvent.Register<SplitPane, SplitShareChangedEventArgs>("ShareChanged", RoutingStrategies.Bubble);

    /// <summary>Smallest width of a pane side by side.</summary>
    public const double MinPaneWidth = 280;

    /// <summary>Smallest height of a stacked pane.</summary>
    public const double MinPaneHeight = 140;

    private readonly Border _divider = new();
    private readonly GridSplitter _splitter = new();

    /// <summary>Creates the pane.</summary>
    public SplitPane()
    {
        _divider.Classes.Add("divider");
        _splitter.DragCompleted += (_, _) => OnDragCompleted();
        Children.Add(_divider);
        Children.Add(_splitter);
        this[!IsStackedProperty] = this.GetResourceObservable("App.ResponseBelow").ToBinding();
        this[!SideShareProperty] = this.GetResourceObservable("App.RequestShareRight").ToBinding();
        this[!StackedShareProperty] = this.GetResourceObservable("App.RequestShareBottom").ToBinding();
        Arrange();
    }

    /// <summary>The first pane.</summary>
    public Control? First
    {
        get => GetValue(FirstProperty);
        set => SetValue(FirstProperty, value);
    }

    /// <summary>The second pane.</summary>
    public Control? Second
    {
        get => GetValue(SecondProperty);
        set => SetValue(SecondProperty, value);
    }

    /// <summary>Stacked layout.</summary>
    public bool IsStacked
    {
        get => GetValue(IsStackedProperty);
        set => SetValue(IsStackedProperty, value);
    }

    /// <summary>Share of the first pane side by side.</summary>
    public double SideShare
    {
        get => GetValue(SideShareProperty);
        set => SetValue(SideShareProperty, value);
    }

    /// <summary>Share of the first pane when stacked.</summary>
    public double StackedShare
    {
        get => GetValue(StackedShareProperty);
        set => SetValue(StackedShareProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FirstProperty || change.Property == SecondProperty)
        {
            if (change.OldValue is Control old)
            {
                Children.Remove(old);
            }

            if (change.NewValue is Control added)
            {
                Children.Add(added);
            }

            Arrange();
        }
        else if (change.Property == IsStackedProperty || change.Property == SideShareProperty || change.Property == StackedShareProperty)
        {
            Arrange();
        }
    }

    private void Arrange()
    {
        var share = Math.Clamp(IsStacked ? StackedShare : SideShare, 0.1, 0.9);
        var first = new GridLength(share, GridUnitType.Star);
        var second = new GridLength(1 - share, GridUnitType.Star);
        RowDefinitions.Clear();
        ColumnDefinitions.Clear();
        if (IsStacked)
        {
            RowDefinitions.Add(new RowDefinition(first) { MinHeight = MinPaneHeight });
            RowDefinitions.Add(new RowDefinition(1, GridUnitType.Pixel));
            RowDefinitions.Add(new RowDefinition(second) { MinHeight = MinPaneHeight });
        }
        else
        {
            ColumnDefinitions.Add(new ColumnDefinition(first) { MinWidth = MinPaneWidth });
            ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Pixel));
            ColumnDefinitions.Add(new ColumnDefinition(second) { MinWidth = MinPaneWidth });
        }

        Place(First, 0);
        Place(_divider, 1);
        Place(_splitter, 1);
        Place(Second, 2);
        _splitter.ResizeDirection = IsStacked ? GridResizeDirection.Rows : GridResizeDirection.Columns;
        _splitter.Cursor = new Cursor(IsStacked ? StandardCursorType.SizeNorthSouth : StandardCursorType.SizeWestEast);
        _divider.Width = IsStacked ? double.NaN : 1;
        _divider.Height = IsStacked ? 1 : double.NaN;
        _divider.HorizontalAlignment = HorizontalAlignment.Stretch;
        _divider.VerticalAlignment = VerticalAlignment.Stretch;

        void Place(Control? control, int index)
        {
            if (control is null)
            {
                return;
            }

            SetRow(control, IsStacked ? index : 0);
            SetColumn(control, IsStacked ? 0 : index);
        }
    }

    private void OnDragCompleted()
    {
        double first, second;
        if (IsStacked)
        {
            first = RowDefinitions[0].ActualHeight;
            second = RowDefinitions[2].ActualHeight;
        }
        else
        {
            first = ColumnDefinitions[0].ActualWidth;
            second = ColumnDefinitions[2].ActualWidth;
        }

        if (first + second <= 0)
        {
            return;
        }

        var share = Math.Round(first / (first + second), 3);
        RaiseEvent(new SplitShareChangedEventArgs(ShareChangedEvent, IsStacked, share));
    }
}
