using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JetApi.Core.Model;
using JetApi.Desktop.Localization;
using JetApi.Desktop.Services;
using JetApi.Runner;

namespace JetApi.Desktop.ViewModels;

/// <summary>A request that can take part in a run.</summary>
public sealed partial class RunRequestOptionViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _include = true;

    /// <summary>Creates the option.</summary>
    public RunRequestOptionViewModel(RunRequest request, string folder)
    {
        Request = request;
        Folder = folder;
    }

    /// <summary>The request.</summary>
    public RunRequest Request { get; }

    /// <summary>Method or "gRPC".</summary>
    public string Badge => Request.Request is HttpRequestDefinition http ? http.Method : "gRPC";

    /// <summary>Its name.</summary>
    public string Name => Request.Request.Name;

    /// <summary>"Admin /" for a request in a folder, else empty.</summary>
    public string Folder { get; }
}

/// <summary>One request's line in the run results.</summary>
public sealed class RunResultItemViewModel : ViewModelBase
{
    /// <summary>Creates the line.</summary>
    public RunResultItemViewModel(RequestRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Result = result;
    }

    /// <summary>The result.</summary>
    public RequestRunResult Result { get; }

    /// <summary>Passed (a response and every check held).</summary>
    public bool Passed => Result.Passed;

    /// <summary>Method or "gRPC".</summary>
    public string Badge => Result.Request.Request is HttpRequestDefinition http ? http.Method : "gRPC";

    /// <summary>Request name.</summary>
    public string Name => Result.Request.Request.Name;

    /// <summary>"200 OK"; empty without a response.</summary>
    public string StatusText => Result.Send.StatusText;

    /// <summary>The status is an error status (4xx/5xx, or a gRPC error).</summary>
    public bool IsErrorStatus => Result.Send.Response.StatusCode is { } code && (Result.Request.Request is GrpcRequestDefinition ? code != 0 : code >= 400);

    /// <summary>"84 ms".</summary>
    public string DurationText => Formatting.Duration(Result.Duration);

    /// <summary>"2/3" when the request has checks.</summary>
    public string ChecksText => Result.Checks.Count == 0 ? string.Empty : $"{Result.Checks.Count(c => c.Passed)}/{Result.Checks.Count}";

    /// <summary>Why it failed: the send error, or each failed check with its message.</summary>
    public IReadOnlyList<string> Problems =>
        Result.Send.Error is { } error
            ? [error]
            : [.. Result.Checks.Where(c => !c.Passed).Select(c => $"{TestLabels.Describe(c.Check)} · {c.Message}")];

    /// <summary>Has problems to list.</summary>
    public bool HasProblems => !Passed;
}

/// <summary>The results of one iteration.</summary>
public sealed class RunIterationViewModel : ViewModelBase
{
    private readonly IReadOnlyDictionary<string, string>? _row;

    /// <summary>Creates the group.</summary>
    public RunIterationViewModel(int index, IReadOnlyDictionary<string, string>? row)
    {
        Index = index;
        _row = row;
    }

    /// <summary>1-based iteration.</summary>
    public int Index { get; }

    /// <summary>"Iteration 3 · row 3: name=Ali, email=ali@example.com".</summary>
    public string Header => _row is null
        ? Loc.Format("Run_Iteration", Index)
        : Loc.Format("Run_IterationRow", Index, string.Join(", ", _row.Select(c => $"{c.Key}={c.Value}")));

    /// <summary>Requests that ran in this iteration.</summary>
    public ObservableCollection<RunResultItemViewModel> Items { get; } = [];
}

/// <summary>
/// The collection runner screen: which requests, environment, iterations or a data file, delay, stop on failure; then live
/// results grouped by iteration. Opened from a collection or folder.
/// </summary>
public sealed partial class CollectionRunViewModel : ViewModelBase, IDisposable
{
    private readonly WorkspaceSession _session;
    private readonly RequestRunner _runner;
    private readonly IDialogService _dialogs;
    private readonly Action _close;
    private CancellationTokenSource? _running;
    private IReadOnlyList<IReadOnlyDictionary<string, string>>? _dataRows;
    private RunSummary? _summary;
    private int _planned;
    private int _done;
    private int _iteration;
    private DateTimeOffset? _startedAt;
    private string? _environmentName;

    [ObservableProperty]
    private decimal _iterations = 1;

    [ObservableProperty]
    private decimal _delayMs;

    [ObservableProperty]
    private bool _stopOnFailure = true;

    [ObservableProperty]
    private bool _persistExtractions;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDataFile), nameof(DataFileName))]
    private string? _dataFilePath;

    [ObservableProperty]
    private string? _dataSummary;

    [ObservableProperty]
    private string? _dataError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(StopCommand))]
    private bool _isRunning;

    /// <summary>Creates the screen for the collection (empty path) or a folder.</summary>
    public CollectionRunViewModel(WorkspaceSession session, RequestRunner runner, IDialogService dialogs, string collectionKey, IReadOnlyList<string> folderPath, Action close)
    {
        _session = session;
        _runner = runner;
        _dialogs = dialogs;
        _close = close;
        var collection = session.Workspace.FindCollection(collectionKey) ?? throw new ArgumentException("Unknown collection.", nameof(collectionKey));
        Title = folderPath.Count == 0 ? collection.Name : CollectionTree.Find(collection, folderPath)?.Name ?? collection.Name;
        foreach (var (path, request) in CollectionTree.EnumerateRequests(collection).Where(r => r.Path.Take(folderPath.Count).SequenceEqual(folderPath)))
        {
            var folders = Enumerable.Range(1, path.Count - 1).Select(length => CollectionTree.Find(collection, [.. path.Take(length)])?.Name).OfType<string>().ToList();
            Requests.Add(new RunRequestOptionViewModel(new RunRequest(collectionKey, path, request), folders.Count == 0 ? string.Empty : string.Join(" / ", folders) + " /"));
        }
    }

    /// <summary>Collection or folder name.</summary>
    public string Title { get; }

    /// <summary>"Environment: Development" or "No environment".</summary>
    public string EnvironmentText => _session.ActiveEnvironment is { } environment ? Loc.Format("Var_ScopeEnvironment", environment.Name) : Loc.Get("Run_NoEnvironment");

    /// <summary>The requests in run order, each with an include box.</summary>
    public ObservableCollection<RunRequestOptionViewModel> Requests { get; } = [];

    /// <summary>The folder has no requests.</summary>
    public bool HasNoRequests => Requests.Count == 0;

    /// <summary>A data file is chosen (iterations follow its rows).</summary>
    public bool HasDataFile => DataFilePath is not null;

    /// <summary>The data file's name.</summary>
    public string DataFileName => DataFilePath is null ? string.Empty : Path.GetFileName(DataFilePath);

    /// <summary>Not running.</summary>
    public bool IsIdle => !IsRunning;

    /// <summary>Results by iteration.</summary>
    public ObservableCollection<RunIterationViewModel> Results { get; } = [];

    /// <summary>A run has started at least once.</summary>
    public bool HasResults => Results.Count > 0;

    /// <summary>The last finished run.</summary>
    public RunSummary? Summary => _summary;

    /// <summary>"Iteration 2 / 3 · 5 requests · 4 checks passed · 1 failed · 1.2 s" (follows the UI language).</summary>
    public string ProgressText
    {
        get
        {
            var results = Results.SelectMany(i => i.Items).Select(i => i.Result).ToList();
            var passed = results.Sum(r => r.Checks.Count(c => c.Passed));
            var failed = results.Sum(r => r.Checks.Count(c => !c.Passed)) + results.Count(r => r.Send.Error is not null);
            var elapsed = Formatting.Duration(TimeSpan.FromTicks(results.Sum(r => r.Duration.Ticks)));
            var iterations = _dataRows?.Count ?? (int)Math.Clamp(Iterations, 1, 10000);
            return Loc.Format("Run_Progress", Math.Max(_iteration, 1), iterations, results.Count, passed, failed, elapsed);
        }
    }

    /// <summary>Share of the planned requests that ran, 0 to 1.</summary>
    public double Progress => _planned == 0 ? 0 : (double)_done / _planned;

    /// <summary>Why the run ended early, or <c>null</c>.</summary>
    public string? EndNote => _summary is null ? null
        : _summary.Cancelled ? Loc.Get("Run_Cancelled")
        : _summary.StoppedEarly ? Loc.Format("Run_Stopped", _planned - _done)
        : null;

    /// <summary>Picks a CSV or JSON data file.</summary>
    [RelayCommand]
    private async Task ChooseDataFileAsync()
    {
        if (await _dialogs.PickFileAsync(Loc.Get("Run_PickData")).ConfigureAwait(true) is not { } path)
        {
            return;
        }

        var data = await DataFile.LoadAsync(path).ConfigureAwait(true);
        DataFilePath = path;
        DataError = data.Error;
        _dataRows = data.IsSuccess ? data.Rows : null;
        DataSummary = data.IsSuccess ? Loc.Format("Run_DataSummary", data.Rows.Count, string.Join(", ", data.Columns)) : null;
    }

    /// <summary>Runs without a data file again.</summary>
    [RelayCommand]
    private void ClearDataFile()
    {
        DataFilePath = null;
        DataSummary = null;
        DataError = null;
        _dataRows = null;
    }

    /// <summary>Starts the run.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task RunAsync()
    {
        var requests = Requests.Where(r => r.Include).Select(r => r.Request).ToList();
        if (requests.Count == 0 || (HasDataFile && _dataRows is null))
        {
            return;
        }

        Results.Clear();
        OnPropertyChanged(nameof(HasResults));
        _summary = null;
        OnPropertyChanged(nameof(EndNote));
        OnPropertyChanged(nameof(CanSaveReport));
        var options = new RunOptions((int)Math.Clamp(Iterations, 1, 10000), TimeSpan.FromMilliseconds((double)Math.Clamp(DelayMs, 0, 600000)), StopOnFailure, _dataRows, PersistExtractions);
        _planned = (_dataRows?.Count ?? options.Iterations) * requests.Count;
        _done = 0;
        UpdateProgress(0);
        _running?.Dispose();
        _running = new CancellationTokenSource();
        _startedAt = DateTimeOffset.Now;
        _environmentName = _session.ActiveEnvironment?.Name;
        IsRunning = true;
        try
        {
            _summary = await CollectionRunner.RunAsync(
                requests,
                options,
                new RunSender(_session, _runner),
                (index, row) =>
                {
                    Results.Add(new RunIterationViewModel(index, row));
                    OnPropertyChanged(nameof(HasResults));
                    UpdateProgress(index);
                },
                result =>
                {
                    Results[^1].Items.Add(new RunResultItemViewModel(result));
                    _done++;
                    UpdateProgress(result.Iteration);
                },
                _running.Token).ConfigureAwait(true);
        }
        finally
        {
            IsRunning = false;
        }

        UpdateProgress(Results.Count);
        OnPropertyChanged(nameof(EndNote));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(CanSaveReport));
    }

    /// <summary>A finished run can be saved as a report.</summary>
    public bool CanSaveReport => _summary is not null && !IsRunning;

    /// <summary>Saves the last run as a single-file HTML report.</summary>
    [RelayCommand]
    private Task SaveHtmlReportAsync() => SaveReportAsync(".html", RunReports.Html);

    /// <summary>Saves the last run as JUnit XML (what CI servers read).</summary>
    [RelayCommand]
    private Task SaveJUnitReportAsync() => SaveReportAsync(".xml", RunReports.JUnit);

    private async Task SaveReportAsync(string extension, Func<RunSummary, ReportInfo, string> render)
    {
        if (_summary is not { } summary || _startedAt is not { } startedAt)
        {
            return;
        }

        var name = string.Concat(Title.Split(Path.GetInvalidFileNameChars())) + "-" + startedAt.ToString("yyyyMMdd-HHmm", System.Globalization.CultureInfo.InvariantCulture) + extension;
        if (await _dialogs.PickSaveFileAsync(Loc.Get("Run_SaveReport"), name).ConfigureAwait(true) is not { } path)
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(path, render(summary, new ReportInfo(Title, _environmentName, startedAt)), new System.Text.UTF8Encoding(false)).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync(Loc.Format("Run_ReportFailed", exception.Message)).ConfigureAwait(true);
        }
    }

    /// <summary>Stops a running run.</summary>
    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Stop() => _running?.Cancel();

    /// <summary>Closes the screen (stopping a run).</summary>
    [RelayCommand]
    private void Close()
    {
        _running?.Cancel();
        _close();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _running?.Cancel();
        _running?.Dispose();
        _running = null;
        GC.SuppressFinalize(this);
    }

    private void UpdateProgress(int iteration)
    {
        _iteration = iteration;
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(Progress));
    }
}
