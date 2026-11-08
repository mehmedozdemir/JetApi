using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JetApi.Core.Model;
using JetApi.Desktop.Localization;
using JetApi.Desktop.Services;

namespace JetApi.Desktop.ViewModels;

/// <summary>Sections of the collection (or folder) screen.</summary>
public enum NodeSettingsSection
{
    /// <summary>Collection variables.</summary>
    Variables,

    /// <summary>Auth inherited by the requests below.</summary>
    Auth,

    /// <summary>Proto import paths and files (collections).</summary>
    Proto,

    /// <summary>Name.</summary>
    General,
}

/// <summary>
/// The collection screen (variables, auth, proto files, name) or a folder's settings (auth, name).
/// Opened by clicking a collection in the tree or from a context menu.
/// </summary>
public sealed partial class NodeSettingsViewModel : ViewModelBase
{
    private readonly WorkspaceSession _session;
    private readonly IDialogService _dialogs;
    private readonly Action<bool> _close;
    private readonly string _savedState;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _importPathsText = string.Empty;

    [ObservableProperty]
    private string _protoFilesText = string.Empty;

    [ObservableProperty]
    private NodeSettingsSection _section;

    /// <summary>Creates the editor for the collection (empty path) or folder at <paramref name="path"/>.</summary>
    public NodeSettingsViewModel(WorkspaceSession session, IDialogService dialogs, string collectionKey, IReadOnlyList<string> path, Action<bool> close)
    {
        _session = session;
        _dialogs = dialogs;
        _close = close;
        CollectionKey = collectionKey;
        Path = path;

        var collection = session.Workspace.FindCollection(collectionKey) ?? throw new ArgumentException("Unknown collection.", nameof(collectionKey));
        if (path.Count == 0)
        {
            _name = collection.Name;
            Auth = new AuthEditorViewModel(collection.Auth, allowInherit: false);
            _importPathsText = string.Join('\n', collection.Proto.ImportPaths);
            _protoFilesText = string.Join('\n', collection.Proto.Files);
            Variables = new EnvironmentsViewModel(session, dialogs, () => { }, collectionKey);
            Variables.Rows.CollectionChanged += (_, _) => OnPropertyChanged(nameof(VariablesBadge));
            Variables.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(EnvironmentsViewModel.IsDirty))
                {
                    OnPropertyChanged(nameof(HasChanges));
                }
            };
            RequestCount = CollectionTree.EnumerateRequests(collection).Count();
            _section = NodeSettingsSection.Variables;
        }
        else
        {
            var folder = (Folder)(CollectionTree.Find(collection, path) ?? throw new ArgumentException("Unknown folder.", nameof(path)));
            _name = folder.Name;
            Auth = new AuthEditorViewModel(folder.Auth, allowInherit: true, () => RequestRunner.ResolveAuth(session, collectionKey, path, AuthConfig.Inherit));
            _section = NodeSettingsSection.Auth;
        }

        _savedState = State();
        Auth.PropertyChanged += (_, _) => OnPropertyChanged(nameof(HasChanges));
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(Name) or nameof(ImportPathsText) or nameof(ProtoFilesText))
        {
            base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(HasChanges)));
        }
    }

    /// <summary>Collection key.</summary>
    public string CollectionKey { get; }

    /// <summary>Folder path; empty for the collection.</summary>
    public IReadOnlyList<string> Path { get; }

    /// <summary>Editing a collection.</summary>
    public bool IsCollection => Path.Count == 0;

    /// <summary>Kind of screen: "Collection" or "Folder settings".</summary>
    public string Title => Loc.Get(IsCollection ? "Col_Title" : "Col_FolderTitle");

    /// <summary>"collections/user-service/collection.yaml · 4 requests" for collections.</summary>
    public string Subtitle => IsCollection ? Loc.Format("Col_Subtitle", CollectionKey, RequestCount) : string.Empty;

    /// <summary>Requests in the collection.</summary>
    public int RequestCount { get; }

    /// <summary>Auth editor.</summary>
    public AuthEditorViewModel Auth { get; }

    /// <summary>The collection's variables; <c>null</c> for folders.</summary>
    public EnvironmentsViewModel? Variables { get; }

    /// <summary>"3" next to the Variables section.</summary>
    public string VariablesBadge => Variables?.Rows.Count(r => !r.IsPlaceholder) is > 0 and var count ? count.ToString(Loc.Instance.Culture) : string.Empty;

    /// <summary>Anything edited since the screen opened.</summary>
    public bool HasChanges => State() != _savedState || (Variables?.IsDirty ?? false);

    /// <summary>Saves and closes.</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            Section = NodeSettingsSection.General;
            return;
        }

        try
        {
            // Variables first: the collection is read again afterwards, so both edits end up in the file.
            if (Variables is { IsDirty: true } variables)
            {
                await variables.SaveAsync().ConfigureAwait(true);
            }

            var collection = _session.Workspace.FindCollection(CollectionKey)!;
            if (IsCollection)
            {
                await _session.SaveCollectionAsync(collection with
                {
                    Name = Name.Trim(),
                    Auth = Auth.ToAuth(),
                    Proto = new ProtoSettings(Lines(ImportPathsText), Lines(ProtoFilesText)),
                }).ConfigureAwait(true);
            }
            else
            {
                var folder = (Folder)CollectionTree.Find(collection, Path)!;
                await _session.SaveNodeAsync(CollectionKey, Path, folder with { Name = Name.Trim(), Auth = Auth.ToAuth() }).ConfigureAwait(true);
            }

            _close(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync(Loc.Format("Error_Save", exception.Message)).ConfigureAwait(true);
        }
    }

    /// <summary>Closes without saving.</summary>
    [RelayCommand]
    private void Cancel() => _close(false);

    private string State() => string.Join('\u0001', Name.Trim(), Auth.ToAuth().ToString(), string.Join('\n', Lines(ImportPathsText)), string.Join('\n', Lines(ProtoFilesText)));

    private static List<string> Lines(string text) =>
        [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(l => l.Replace('\\', '/'))];
}
