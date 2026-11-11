using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using JetApi.Desktop.Localization;
using JetApi.Desktop.ViewModels;

namespace JetApi.Desktop.Views;

/// <summary>An open workspace: sidebar, tabs, editors and overlays.</summary>
public sealed partial class WorkspaceView : UserControl
{
    private const double DragThreshold = 6;

    private static readonly DataFormat<string> NodeFormat = DataFormat.CreateStringApplicationFormat("jetapi-tree-node");

    // Edge band of the tree that scrolls while dragging, and how long to hover a closed folder before it opens.
    private const double ScrollBand = 28;
    private static readonly TimeSpan ExpandDelay = TimeSpan.FromMilliseconds(700);

    private TreeNodeViewModel? _pressedNode;
    private Point _pressedAt;
    private PointerPressedEventArgs? _pressedArgs;
    private RequestTabViewModel? _middlePressedTab;
    private TreeNodeViewModel? _dragSource;
    private TreeNodeViewModel? _hintNode;
    private TreeNodeViewModel? _expandCandidate;
    private DateTime _expandSince;

    /// <summary>Creates the view.</summary>
    public WorkspaceView()
    {
        InitializeComponent();
        Tree.AddHandler(PointerPressedEvent, OnTreePointerPressed, RoutingStrategies.Tunnel);
        Tree.AddHandler(PointerMovedEvent, OnTreePointerMoved, RoutingStrategies.Tunnel);
        Tree.AddHandler(PointerReleasedEvent, OnTreePointerReleased, RoutingStrategies.Tunnel);
        Tree.AddHandler(KeyDownEvent, OnTreeKeyDown, RoutingStrategies.Tunnel);
        Tree.ContextRequested += OnTreeContextRequested;
        DragDrop.SetAllowDrop(Tree, true);
        Tree.AddHandler(DragDrop.DragOverEvent, OnTreeDragOver);
        Tree.AddHandler(DragDrop.DropEvent, OnTreeDrop);
        Tree.AddHandler(DragDrop.DragLeaveEvent, (_, _) => SetHint(null, DropPosition.None));
        TabStrip.AddHandler(PointerPressedEvent, OnTabPointerPressed, RoutingStrategies.Tunnel);
        TabStrip.AddHandler(PointerReleasedEvent, OnTabPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        TabStrip.ContextRequested += OnTabContextRequested;

        // A node selected from code (e.g. after a move) scrolls into view once its row exists.
        Tree.SelectionChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(
            () => (Tree.SelectedItem is { } item ? Tree.TreeContainerFromItem(item) : null)?.BringIntoView(), Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private WorkspaceViewModel? ViewModel => DataContext as WorkspaceViewModel;

    /// <summary>Focuses the URL box of the current editor (Ctrl+L).</summary>
    public void FocusUrl()
    {
        var editor = Editor.GetVisualDescendants().FirstOrDefault(v => v is HttpRequestView or GrpcRequestView);
        switch (editor)
        {
            case HttpRequestView http:
                http.FocusUrl();
                break;
            case GrpcRequestView grpc:
                grpc.FocusUrl();
                break;
        }
    }

    private static TreeNodeViewModel? NodeFrom(object? source) =>
        (source as ILogical)?.GetSelfAndLogicalAncestors().OfType<StyledElement>().Select(e => e.DataContext).OfType<TreeNodeViewModel>().FirstOrDefault();

    private static bool IsOnExpander(object? source) =>
        source is Visual visual && visual.GetSelfAndVisualAncestors().Any(v => v is ToggleButton);

    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Tree).Properties.IsLeftButtonPressed || IsOnExpander(e.Source))
        {
            return;
        }

        _pressedNode = NodeFrom(e.Source);
        _pressedAt = e.GetPosition(Tree);
        _pressedArgs = e;
    }

    private async void OnTreePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressedNode is not { } node || _pressedArgs is null)
        {
            return;
        }

        var delta = e.GetPosition(Tree) - _pressedAt;
        if (Math.Abs(delta.X) + Math.Abs(delta.Y) < DragThreshold)
        {
            return;
        }

        var args = _pressedArgs;
        _pressedNode = null;
        _pressedArgs = null;
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(NodeFormat, node.Identity));
        _dragSource = node;
        try
        {
            await DragDrop.DoDragDropAsync(args, transfer, DragDropEffects.Move).ConfigureAwait(true);
        }
        finally
        {
            _dragSource = null;
            SetHint(null, DropPosition.None);
        }
    }

    private void OnTreePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pressedNode is { } node && e.InitialPressMouseButton == MouseButton.Left)
        {
            if (node.IsRequest || (node.IsCollection && !IsOnExpander(e.Source)))
            {
                ViewModel?.OpenNode(node);
            }
            else if (!IsOnExpander(e.Source))
            {
                node.IsExpanded = !node.IsExpanded;
            }
        }

        _pressedNode = null;
        _pressedArgs = null;
    }

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (Tree.SelectedItem is not TreeNodeViewModel node || ViewModel is not { } vm)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter when node.IsRequest:
                vm.OpenNode(node);
                e.Handled = true;
                break;
            case Key.F2:
                vm.RenameCommand.Execute(node);
                e.Handled = true;
                break;
            case Key.D when e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control) && !node.IsCollection:
                vm.DuplicateCommand.Execute(node);
                e.Handled = true;
                break;
            case Key.Delete:
                vm.DeleteCommand.Execute(node);
                e.Handled = true;
                break;
            case Key.Up when e.KeyModifiers == KeyModifiers.Alt:
                vm.MoveUpCommand.Execute(node);
                e.Handled = true;
                break;
            case Key.Down when e.KeyModifiers == KeyModifiers.Alt:
                vm.MoveDownCommand.Execute(node);
                e.Handled = true;
                break;
        }
    }

    private void OnWorkspaceButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            BuildWorkspaceMenu(vm).ShowAt(WorkspaceButton);
        }
    }

    /// <summary>The workspace menu: saved workspaces (the open one checked, missing ones disabled), then "Open another folder…".</summary>
    internal static MenuFlyout BuildWorkspaceMenu(WorkspaceViewModel vm)
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        foreach (var choice in vm.WorkspaceChoices())
        {
            var name = new TextBlock { Text = choice.Name, FontWeight = choice.IsCurrent ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal, MaxWidth = 360, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
            var detail = new TextBlock
            {
                Text = choice.Exists ? choice.PathDisplay : Loc.Format("Top_WorkspaceMissing", choice.PathDisplay),
                MaxWidth = 360,
                TextTrimming = Avalonia.Media.TextTrimming.PathSegmentEllipsis,
            };
            detail.Classes.AddRange(["faint", "small"]);
            var item = new MenuItem
            {
                Header = new StackPanel { Spacing = 1, Children = { name, detail } },
                Icon = choice.IsCurrent ? Controls.Icon.ForMenu("Icon.Check") : null,
                IsEnabled = choice.Exists,
                Tag = choice,
            };
            var path = choice.Path;
            item.Click += (_, _) => _ = vm.SwitchWorkspaceAsync(path);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = Loc.Get("Top_OpenWorkspace"), Icon = Controls.Icon.ForMenu("Icon.Folder"), Command = vm.OpenOtherWorkspaceCommand });
        return menu;
    }

    private void OnTreeContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (NodeFrom(e.Source) is not { } node || ViewModel is not { } vm)
        {
            return;
        }

        var items = new List<Control>();
        if (node.IsRequest)
        {
            var open = new MenuItem { Header = Loc.Get("Menu_Open"), Icon = Controls.Icon.ForMenu("Icon.Open") };
            open.Click += (_, _) => vm.OpenNode(node);
            items.Add(open);
            items.Add(new Separator());
        }

        if (node.IsContainer)
        {
            items.Add(Item("Menu_NewHttp", vm.NewHttpRequestCommand, node));
            items.Add(Item("Menu_NewGrpc", vm.NewGrpcRequestCommand, node));
            items.Add(Item("Menu_NewFolder", vm.NewFolderCommand, node));
            items.Add(new Separator());
            items.Add(Item(node.IsCollection ? "Menu_CollectionSettings" : "Menu_FolderSettings", vm.OpenNodeSettingsCommand, node));
            items.Add(Item("Menu_Run", vm.RunNodeCommand, node));
        }

        items.Add(Item("Menu_Rename", vm.RenameCommand, node, "F2"));
        if (!node.IsCollection)
        {
            items.Add(Item("Menu_Duplicate", vm.DuplicateCommand, node, OperatingSystem.IsMacOS() ? "Meta+D" : "Ctrl+D"));
        }

        var moves = new List<MenuItem>();
        if (TreeDrop.Step(vm.Session.Workspace, node, down: false) is not null)
        {
            moves.Add(Item("Menu_MoveUp", vm.MoveUpCommand, node, "Alt+Up"));
        }

        if (TreeDrop.Step(vm.Session.Workspace, node, down: true) is not null)
        {
            moves.Add(Item("Menu_MoveDown", vm.MoveDownCommand, node, "Alt+Down"));
        }

        if (node.Path.Count > 1)
        {
            moves.Add(Item("Menu_MoveToRoot", vm.MoveToRootCommand, node));
        }

        if (moves.Count > 0)
        {
            items.Add(new Separator());
            items.AddRange(moves);
        }

        items.Add(new Separator());
        var delete = Item("Menu_Delete", vm.DeleteCommand, node, "Delete");
        delete.Classes.Add("danger");
        items.Add(delete);

        var menu = new ContextMenu { ItemsSource = items };
        menu.Open(e.Source as Control ?? Tree);
        e.Handled = true;
    }

    private static Controls.Icon? MenuIcon(string key) => key switch
    {
        "Menu_NewHttp" => Controls.Icon.ForMenu("Icon.Plus"),
        "Menu_NewGrpc" => Controls.Icon.ForMenu("Icon.Server"),
        "Menu_NewFolder" => Controls.Icon.ForMenu("Icon.Folder"),
        "Menu_CollectionSettings" or "Menu_FolderSettings" => Controls.Icon.ForMenu("Icon.Sliders"),
        "Menu_Rename" => Controls.Icon.ForMenu("Icon.Edit"),
        "Menu_Run" => Controls.Icon.ForMenu("Icon.Play"),
        "Menu_Duplicate" => Controls.Icon.ForMenu("Icon.Copy"),
        "Menu_MoveToRoot" => Controls.Icon.ForMenu("Icon.MoveUp"),
        "Menu_MoveUp" => Controls.Icon.ForMenu("Icon.ArrowUp"),
        "Menu_MoveDown" => Controls.Icon.ForMenu("Icon.ArrowDown"),
        "Menu_Delete" => Controls.Icon.ForMenu("Icon.Trash"),
        _ => null,
    };

    private static MenuItem Item(string key, System.Windows.Input.ICommand command, TreeNodeViewModel node, string? gesture = null)
    {
        var item = new MenuItem { Header = Loc.Get(key), Command = command, CommandParameter = node, Icon = MenuIcon(key) };
        if (gesture is not null)
        {
            item.InputGesture = KeyGesture.Parse(gesture);
        }

        return item;
    }

    private void OnTreeDragOver(object? sender, DragEventArgs e)
    {
        AutoScroll(e);
        var (target, position) = DropTargetAt(e);
        SetHint(target, position);
        e.DragEffects = position == DropPosition.None ? DragDropEffects.None : DragDropEffects.Move;

        // Hovering a closed folder for a moment opens it, so the node can go deeper.
        if (position == DropPosition.Inside && target is { IsExpanded: false, Children.Count: > 0 })
        {
            if (_expandCandidate != target)
            {
                _expandCandidate = target;
                _expandSince = DateTime.UtcNow;
            }
            else if (DateTime.UtcNow - _expandSince >= ExpandDelay)
            {
                target.IsExpanded = true;
                _expandCandidate = null;
            }
        }
        else
        {
            _expandCandidate = null;
        }

        e.Handled = true;
    }

    private async void OnTreeDrop(object? sender, DragEventArgs e)
    {
        var (target, position) = DropTargetAt(e);
        var source = DraggedNode(e);
        SetHint(null, DropPosition.None);
        if (ViewModel is not { } vm || source is null || target is null || position == DropPosition.None)
        {
            return;
        }

        e.Handled = true;
        await vm.DropAsync(source, target, position).ConfigureAwait(true);
    }

    private TreeNodeViewModel? DraggedNode(DragEventArgs e) => FindNode(e.DataTransfer.TryGetValue(NodeFormat)) ?? _dragSource;

    /// <summary>The row under the pointer and where a dragged node would land there (see <see cref="TreeDrop"/>).</summary>
    internal (TreeNodeViewModel? Target, DropPosition Position) DropTargetAt(DragEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (DraggedNode(e) is not { } source || ViewModel is not { } vm)
        {
            return (null, DropPosition.None);
        }

        var item = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<TreeViewItem>().FirstOrDefault();
        if (item?.DataContext is not TreeNodeViewModel target)
        {
            // Empty space below the rows: the end of the last collection.
            return vm.Collections.LastOrDefault(c => c.IsVisible) is { } last
                ? (last, Checked(source, last, source.IsCollection ? DropPosition.After : DropPosition.Inside))
                : (null, DropPosition.None);
        }

        var header = item.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_LayoutRoot");
        var height = header?.Bounds.Height is > 0 and var h ? h : item.Bounds.Height;
        var fraction = Math.Clamp(e.GetPosition(item).Y / Math.Max(height, 1), 0, 1);
        return (target, TreeDrop.PositionAt(source, target, fraction));
    }

    private static DropPosition Checked(TreeNodeViewModel source, TreeNodeViewModel target, DropPosition position) =>
        TreeDrop.CanDrop(source, target, position) ? position : DropPosition.None;

    private void SetHint(TreeNodeViewModel? target, DropPosition position)
    {
        if (_hintNode is not null && (_hintNode != target || position == DropPosition.None))
        {
            _hintNode.DropHint = DropPosition.None;
        }

        _hintNode = position == DropPosition.None ? null : target;
        if (_hintNode is not null)
        {
            _hintNode.DropHint = position;
        }
    }

    // Scrolls the tree while the pointer is near its top or bottom edge.
    private void AutoScroll(DragEventArgs e)
    {
        if (Tree.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is not { } scroller)
        {
            return;
        }

        var y = e.GetPosition(scroller).Y;
        var bottom = scroller.Bounds.Height - ScrollBand;
        var step = y < ScrollBand ? -(ScrollBand - y) / 2 : y > bottom ? (y - bottom) / 2 : 0;
        if (step != 0)
        {
            var max = Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height);
            scroller.Offset = scroller.Offset.WithY(Math.Clamp(scroller.Offset.Y + step, 0, max));
        }
    }

    private TreeNodeViewModel? FindNode(string? identity)
    {
        if (identity is null || ViewModel is null)
        {
            return null;
        }

        static IEnumerable<TreeNodeViewModel> Flatten(TreeNodeViewModel node) => node.Children.SelectMany(Flatten).Prepend(node);
        return ViewModel.Collections.SelectMany(Flatten).FirstOrDefault(n => n.Identity == identity);
    }

    private void OnTabContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (TabFrom(e.Source) is not { } tab || ViewModel is not { } vm)
        {
            return;
        }

        BuildTabMenu(vm, tab).Open(e.Source as Control ?? TabStrip);
        e.Handled = true;
    }

    /// <summary>
    /// The tab menu: close, close others, close to the right, close saved, close all (the bulk ones leave pinned tabs open),
    /// then pin or unpin. Items that would close nothing are disabled.
    /// </summary>
    internal static ContextMenu BuildTabMenu(WorkspaceViewModel vm, RequestTabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(tab);
        var index = vm.Tabs.IndexOf(tab);
        var close = TabItem("Tab_Close", vm.CloseTabCommand, tab, "Icon.Close", OperatingSystem.IsMacOS() ? "Meta+W" : "Ctrl+W");
        var others = TabItem("Tab_CloseOthers", vm.CloseOtherTabsCommand, tab, "Icon.CloseOthers");
        others.IsEnabled = vm.Tabs.Any(t => t != tab && !t.IsPinned);
        var right = TabItem("Tab_CloseRight", vm.CloseTabsToRightCommand, tab, "Icon.CloseRight");
        right.IsEnabled = vm.Tabs.Skip(index + 1).Any(t => !t.IsPinned);
        var saved = TabItem("Tab_CloseSaved", vm.CloseSavedTabsCommand, null, "Icon.Check");
        saved.IsEnabled = vm.Tabs.Any(t => !t.IsDirty && !t.IsPinned);
        var all = TabItem("Tab_CloseAll", vm.CloseAllTabsCommand, null, "Icon.CloseAll");
        all.IsEnabled = vm.Tabs.Any(t => !t.IsPinned);
        var pin = TabItem(tab.IsPinned ? "Tab_Unpin" : "Tab_Pin", tab.TogglePinCommand, null, tab.IsPinned ? "Icon.Unpin" : "Icon.Pin");
        return new ContextMenu { ItemsSource = new List<Control> { close, others, right, saved, all, new Separator(), pin } };
    }

    private static MenuItem TabItem(string key, System.Windows.Input.ICommand command, object? parameter, string? icon = null, string? gesture = null)
    {
        var item = new MenuItem { Header = Loc.Get(key), Command = command, CommandParameter = parameter, Icon = icon is null ? null : Controls.Icon.ForMenu(icon) };
        if (gesture is not null)
        {
            item.InputGesture = KeyGesture.Parse(gesture);
        }

        return item;
    }

    // A middle click anywhere on a tab closes it (also on its padding, and although the list item handles the press);
    // pinned tabs stay, as they do for "close all".
    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _middlePressedTab = e.GetCurrentPoint(TabStrip).Properties.IsMiddleButtonPressed ? TabFrom(e.Source) : null;
        if (_middlePressedTab is not null)
        {
            e.Handled = true;
        }
    }

    private void OnTabPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Middle && _middlePressedTab is { IsPinned: false } tab && TabFrom(e.Source) == tab)
        {
            ViewModel?.CloseTabCommand.Execute(tab);
            e.Handled = true;
        }

        _middlePressedTab = null;
    }

    private static RequestTabViewModel? TabFrom(object? source) =>
        (source as Visual)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault()?.DataContext as RequestTabViewModel;

    private void OnHistoryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (HistoryList.SelectedItem is HistoryItemViewModel item)
        {
            ViewModel?.OpenHistoryEntryCommand.Execute(item);
        }
    }
}
