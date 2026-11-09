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

    private TreeNodeViewModel? _pressedNode;
    private Point _pressedAt;
    private PointerPressedEventArgs? _pressedArgs;

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
        if (_pressedNode is not { IsCollection: false } node || _pressedArgs is null)
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
        await DragDrop.DoDragDropAsync(args, transfer, DragDropEffects.Move).ConfigureAwait(true);
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
            if (node.Path.Count > 1)
            {
                items.Add(Item("Menu_MoveToRoot", vm.MoveToRootCommand, node));
            }
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
        var source = FindNode(e.DataTransfer.TryGetValue(NodeFormat));
        var target = NodeFrom(e.Source);
        e.DragEffects = source is not null && (target is null || (target.CollectionKey == source.CollectionKey && !target.Identity.StartsWith(source.Identity, StringComparison.Ordinal)))
            ? DragDropEffects.Move
            : DragDropEffects.None;
    }

    private async void OnTreeDrop(object? sender, DragEventArgs e)
    {
        if (ViewModel is not { } vm || FindNode(e.DataTransfer.TryGetValue(NodeFormat)) is not { } source)
        {
            return;
        }

        var target = NodeFrom(e.Source);
        if (target is not null && target.Identity.StartsWith(source.Identity, StringComparison.Ordinal))
        {
            return;
        }

        await vm.MoveAsync(source, target ?? vm.Collections.FirstOrDefault(c => c.CollectionKey == source.CollectionKey)).ConfigureAwait(true);
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

    private void OnTabPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Middle && (sender as Control)?.DataContext is RequestTabViewModel tab)
        {
            ViewModel?.CloseTabCommand.Execute(tab);
            e.Handled = true;
        }
    }

    private void OnHistoryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (HistoryList.SelectedItem is HistoryItemViewModel item)
        {
            ViewModel?.OpenHistoryEntryCommand.Execute(item);
        }
    }
}
