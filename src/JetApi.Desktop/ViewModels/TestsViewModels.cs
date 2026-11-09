using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JetApi.Core.Model;
using JetApi.Desktop.Localization;
using JetApi.Testing;

namespace JetApi.Desktop.ViewModels;

/// <summary>The last result of a check or extraction row.</summary>
public enum TestOutcome
{
    /// <summary>Not run since the row was last edited.</summary>
    None,

    /// <summary>Passed (or the value was stored).</summary>
    Passed,

    /// <summary>Failed (or nothing was found).</summary>
    Failed,
}

/// <summary>Labels for checks, shared by the editor rows and the results list.</summary>
public static class TestLabels
{
    /// <summary>Check sources in menu order.</summary>
    public static IReadOnlyList<LocalizedOption<CheckSource>> Sources { get; } =
    [
        new(CheckSource.Status, "Check_Status"), new(CheckSource.Header, "Check_Header"), new(CheckSource.Json, "Check_Json"),
        new(CheckSource.Body, "Check_Body"), new(CheckSource.Time, "Check_Time"), new(CheckSource.Schema, "Check_Schema"),
    ];

    /// <summary>Every operator.</summary>
    public static IReadOnlyList<LocalizedOption<CheckOperator>> Operators { get; } =
    [
        new(CheckOperator.Equals, "Op_Equals"), new(CheckOperator.NotEquals, "Op_NotEquals"), new(CheckOperator.GreaterThan, "Op_GreaterThan"),
        new(CheckOperator.LessThan, "Op_LessThan"), new(CheckOperator.Contains, "Op_Contains"), new(CheckOperator.NotContains, "Op_NotContains"),
        new(CheckOperator.Matches, "Op_Matches"), new(CheckOperator.Exists, "Op_Exists"), new(CheckOperator.NotExists, "Op_NotExists"),
    ];

    /// <summary>Extraction sources.</summary>
    public static IReadOnlyList<LocalizedOption<ExtractionSource>> ExtractionSources { get; } =
        [new(ExtractionSource.Json, "Check_Json"), new(ExtractionSource.Header, "Check_Header"), new(ExtractionSource.Status, "Check_Status"), new(ExtractionSource.Body, "Check_Body")];

    /// <summary>Where extracted values go.</summary>
    public static IReadOnlyList<LocalizedOption<ExtractionTarget>> Targets { get; } =
        [new(ExtractionTarget.Runtime, "To_Runtime"), new(ExtractionTarget.Environment, "To_Environment"), new(ExtractionTarget.Collection, "To_Collection"), new(ExtractionTarget.Global, "To_Global")];

    /// <summary>The operators that make sense for a source; none for a schema check.</summary>
    public static IReadOnlyList<LocalizedOption<CheckOperator>> OperatorsFor(CheckSource source) => source switch
    {
        CheckSource.Status => Pick(CheckOperator.Equals, CheckOperator.NotEquals, CheckOperator.GreaterThan, CheckOperator.LessThan),
        CheckSource.Time => Pick(CheckOperator.LessThan, CheckOperator.GreaterThan),
        CheckSource.Json => Operators,
        CheckSource.Schema => [],
        _ => Pick(CheckOperator.Equals, CheckOperator.NotEquals, CheckOperator.Contains, CheckOperator.NotContains, CheckOperator.Matches, CheckOperator.Exists, CheckOperator.NotExists),
    };

    /// <summary>"Status equals 201", "Content-Type contains json", "$.id exists", "Matches the JSON Schema".</summary>
    public static string Describe(ResponseCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        if (check.Source == CheckSource.Schema)
        {
            return Loc.Get("Tests_SchemaTitle");
        }

        var subject = check.Source switch
        {
            CheckSource.Header or CheckSource.Json => check.Target,
            _ => Sources.First(s => s.Value == check.Source).Label,
        };
        var op = Operators.First(o => o.Value == check.Operator).Label;
        return check.Operator is CheckOperator.Exists or CheckOperator.NotExists
            ? $"{subject} {op}"
            : $"{subject} {op} {check.Expected}{(check.Source == CheckSource.Time ? " ms" : string.Empty)}";
    }

    private static List<LocalizedOption<CheckOperator>> Pick(params CheckOperator[] operators) => [.. operators.Select(o => Operators.First(x => x.Value == o))];
}

/// <summary>One check in the Tests tab.</summary>
public sealed partial class CheckRowViewModel : ViewModelBase
{
    private readonly Action _changed;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Operators), nameof(HasTarget), nameof(HasOperator), nameof(HasExpected), nameof(HasInlineExpected), nameof(IsSchema), nameof(ShowNoTarget), nameof(TargetPlaceholder), nameof(ExpectedPlaceholder))]
    private LocalizedOption<CheckSource> _selectedSource;

    [ObservableProperty]
    private string _target;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExpected), nameof(HasInlineExpected))]
    private LocalizedOption<CheckOperator>? _selectedOperator;

    [ObservableProperty]
    private string _expected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPassed), nameof(IsFailed))]
    private TestOutcome _outcome;

    [ObservableProperty]
    private string? _resultText;

    /// <summary>Creates a row for <paramref name="check"/>.</summary>
    public CheckRowViewModel(ResponseCheck check, Action changed, Action<CheckRowViewModel> remove)
    {
        ArgumentNullException.ThrowIfNull(check);
        _changed = changed;
        _isEnabled = check.IsEnabled;
        _selectedSource = TestLabels.Sources.First(s => s.Value == check.Source);
        _target = check.Target;
        _expected = check.Expected;
        _selectedOperator = Operators.FirstOrDefault(o => o.Value == check.Operator) ?? FirstOperator();
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    /// <summary>Sources for the menu.</summary>
    public IReadOnlyList<LocalizedOption<CheckSource>> Sources => TestLabels.Sources;

    /// <summary>Operators that fit the source.</summary>
    public IReadOnlyList<LocalizedOption<CheckOperator>> Operators => TestLabels.OperatorsFor(SelectedSource.Value);

    /// <summary>Header name or JSONPath is asked for.</summary>
    public bool HasTarget => SelectedSource.Value is CheckSource.Header or CheckSource.Json;

    /// <summary>An operator is chosen (not for schemas).</summary>
    public bool HasOperator => SelectedSource.Value != CheckSource.Schema;

    /// <summary>An expected value is asked for.</summary>
    public bool HasExpected => SelectedSource.Value == CheckSource.Schema || SelectedOperator?.Value is not (CheckOperator.Exists or CheckOperator.NotExists);

    /// <summary>The expected value is a JSON Schema (several lines).</summary>
    public bool IsSchema => SelectedSource.Value == CheckSource.Schema;

    /// <summary>"—" in the target column: nothing to name, and no schema box over it.</summary>
    public bool ShowNoTarget => !HasTarget && !IsSchema;

    /// <summary>The expected value fits in the one-line box (everything but schemas).</summary>
    public bool HasInlineExpected => HasExpected && !IsSchema;

    /// <summary>Hint in the target box.</summary>
    public string TargetPlaceholder => SelectedSource.Value == CheckSource.Header ? "Content-Type" : "$.data.id";

    /// <summary>Hint in the expected box.</summary>
    public string ExpectedPlaceholder => SelectedSource.Value switch
    {
        CheckSource.Status => "200",
        CheckSource.Time => "500",
        CheckSource.Schema => "{ \"type\": \"object\" }",
        _ => string.Empty,
    };

    /// <summary>Last run passed.</summary>
    public bool IsPassed => Outcome == TestOutcome.Passed;

    /// <summary>Last run failed.</summary>
    public bool IsFailed => Outcome == TestOutcome.Failed;

    /// <summary>Removes the row.</summary>
    public IRelayCommand RemoveCommand { get; }

    private LocalizedOption<CheckOperator>? FirstOperator() => Operators.Count > 0 ? Operators[0] : null;

    /// <summary>The check as edited.</summary>
    public ResponseCheck ToCheck() => new(
        SelectedSource.Value,
        SelectedOperator?.Value ?? CheckOperator.Equals,
        HasTarget ? Target.Trim() : string.Empty,
        HasExpected ? Expected : string.Empty,
        IsEnabled);

    /// <summary>Shows the result of the last run.</summary>
    internal void Show(CheckResult? result)
    {
        Outcome = result is null ? TestOutcome.None : result.Passed ? TestOutcome.Passed : TestOutcome.Failed;
        ResultText = result is null ? null : result.Message ?? result.Actual;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(SelectedSource) && (SelectedOperator is null || !Operators.Contains(SelectedOperator)))
        {
            SelectedOperator = FirstOperator();
        }

        if (e.PropertyName is nameof(SelectedSource) or nameof(IsEnabled) or nameof(Target) or nameof(SelectedOperator) or nameof(Expected))
        {
            Outcome = TestOutcome.None;
            ResultText = null;
            _changed();
        }
    }
}

/// <summary>One extraction in the Tests tab.</summary>
public sealed partial class ExtractionRowViewModel : ViewModelBase
{
    private readonly Action _changed;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTarget), nameof(TargetPlaceholder))]
    private LocalizedOption<ExtractionSource> _selectedSource;

    [ObservableProperty]
    private string _target;

    [ObservableProperty]
    private string _variable;

    [ObservableProperty]
    private LocalizedOption<ExtractionTarget> _selectedTo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPassed), nameof(IsFailed))]
    private TestOutcome _outcome;

    [ObservableProperty]
    private string? _resultText;

    /// <summary>Creates a row for <paramref name="extraction"/>.</summary>
    public ExtractionRowViewModel(Extraction extraction, Action changed, Action<ExtractionRowViewModel> remove)
    {
        ArgumentNullException.ThrowIfNull(extraction);
        _changed = changed;
        _isEnabled = extraction.IsEnabled;
        _selectedSource = TestLabels.ExtractionSources.First(s => s.Value == extraction.Source);
        _target = extraction.Target;
        _variable = extraction.Variable;
        _selectedTo = TestLabels.Targets.First(t => t.Value == extraction.To);
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    /// <summary>Sources for the menu.</summary>
    public IReadOnlyList<LocalizedOption<ExtractionSource>> Sources => TestLabels.ExtractionSources;

    /// <summary>Destinations for the menu.</summary>
    public IReadOnlyList<LocalizedOption<ExtractionTarget>> Targets => TestLabels.Targets;

    /// <summary>A JSONPath or header name is asked for.</summary>
    public bool HasTarget => SelectedSource.Value is ExtractionSource.Json or ExtractionSource.Header;

    /// <summary>Hint in the target box.</summary>
    public string TargetPlaceholder => SelectedSource.Value == ExtractionSource.Header ? "Location" : "$.access_token";

    /// <summary>Last run stored a value.</summary>
    public bool IsPassed => Outcome == TestOutcome.Passed;

    /// <summary>Last run found nothing.</summary>
    public bool IsFailed => Outcome == TestOutcome.Failed;

    /// <summary>Removes the row.</summary>
    public IRelayCommand RemoveCommand { get; }

    /// <summary>The extraction as edited.</summary>
    public Extraction ToExtraction() => new(SelectedSource.Value, HasTarget ? Target.Trim() : string.Empty, Variable.Trim(), SelectedTo.Value, IsEnabled);

    /// <summary>Shows the result of the last run.</summary>
    internal void Show(ExtractionResult? result)
    {
        Outcome = result is null ? TestOutcome.None : result.IsSuccess ? TestOutcome.Passed : TestOutcome.Failed;
        ResultText = result is null ? null : result.Error ?? result.Value;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(IsEnabled) or nameof(SelectedSource) or nameof(Target) or nameof(Variable) or nameof(SelectedTo))
        {
            Outcome = TestOutcome.None;
            ResultText = null;
            _changed();
        }
    }
}

/// <summary>The Tests tab of a request: its checks and extractions, with the result of the last send.</summary>
public sealed partial class TestsEditorViewModel : ViewModelBase
{
    private readonly Action _changed;
    private readonly ScriptSet _scripts;

    /// <summary>Creates the editor for the checks and extractions of <paramref name="request"/>.</summary>
    public TestsEditorViewModel(RequestNode request, Action changed)
    {
        ArgumentNullException.ThrowIfNull(request);
        _changed = changed;
        _scripts = request.Scripts;
        foreach (var check in request.Checks)
        {
            Checks.Add(NewRow(check));
        }

        foreach (var extraction in request.Extractions)
        {
            Extractions.Add(NewRow(extraction));
        }

        Checks.CollectionChanged += (_, _) => OnRowsChanged();
        Extractions.CollectionChanged += (_, _) => OnRowsChanged();
    }

    /// <summary>The checks.</summary>
    public ObservableCollection<CheckRowViewModel> Checks { get; } = [];

    /// <summary>The extractions.</summary>
    public ObservableCollection<ExtractionRowViewModel> Extractions { get; } = [];

    /// <summary>No checks and no extractions.</summary>
    public bool IsEmpty => Checks.Count == 0 && Extractions.Count == 0;

    /// <summary>The request has Postman scripts kept from an import (ADR-0010): a note says they do not run.</summary>
    public bool HasScripts => !_scripts.IsEmpty;

    /// <summary>The kept scripts are shown under the note.</summary>
    [ObservableProperty]
    public partial bool ShowScripts { get; set; }

    /// <summary>Shows or hides the kept scripts.</summary>
    [RelayCommand]
    private void ToggleScripts() => ShowScripts = !ShowScripts;

    /// <summary>The kept scripts, for reading: the pre-request script, then the test script.</summary>
    public string ScriptsText => string.Join(
        "\n\n",
        new[] { (Loc.Get("Tests_PreRequestScript"), _scripts.PreRequest), (Loc.Get("Tests_TestScript"), _scripts.Test) }
            .Where(s => !string.IsNullOrWhiteSpace(s.Item2))
            .Select(s => $"// {s.Item1}\n{s.Item2.TrimEnd()}"));

    /// <summary>Has checks (shows their table).</summary>
    public bool HasChecks => Checks.Count > 0;

    /// <summary>Has extractions (shows their table).</summary>
    public bool HasExtractions => Extractions.Count > 0;

    /// <summary>Badge on the tab: "3/4" after a run, else the number of enabled checks, else empty.</summary>
    public string Badge
    {
        get
        {
            var ran = Checks.Where(c => c.Outcome != TestOutcome.None).ToList();
            if (ran.Count > 0)
            {
                return $"{ran.Count(c => c.IsPassed)}/{ran.Count}";
            }

            var enabled = Checks.Count(c => c.IsEnabled);
            return enabled == 0 ? string.Empty : enabled.ToString(Loc.Instance.Culture);
        }
    }

    /// <summary>A check failed in the last run (the badge turns red).</summary>
    public bool HasFailures => Checks.Any(c => c.IsFailed);

    /// <summary>Adds a check (status equals 200 by default).</summary>
    [RelayCommand]
    private void AddCheck() => Checks.Add(NewRow(new ResponseCheck(CheckSource.Status, CheckOperator.Equals, Expected: "200")));

    /// <summary>Adds an extraction from a JSON body.</summary>
    [RelayCommand]
    private void AddExtraction() => Extractions.Add(NewRow(new Extraction(ExtractionSource.Json, "$.", string.Empty)));

    /// <summary>Adds a check, e.g. from a value in the response.</summary>
    public void Add(ResponseCheck check) => Checks.Add(NewRow(check));

    /// <summary>Adds an extraction, e.g. from a value in the response.</summary>
    public void Add(Extraction extraction) => Extractions.Add(NewRow(extraction));

    /// <summary>The checks as edited.</summary>
    public IReadOnlyList<ResponseCheck> ToChecks() => [.. Checks.Select(c => c.ToCheck())];

    /// <summary>The extractions as edited.</summary>
    public IReadOnlyList<Extraction> ToExtractions() => [.. Extractions.Select(e => e.ToExtraction())];

    /// <summary>Shows a run's results next to the enabled rows they belong to (results come in row order).</summary>
    public void ShowResults(IReadOnlyList<CheckResult> checks, IReadOnlyList<ExtractionResult> extractions)
    {
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(extractions);
        Pair(Checks, checks, (row, result) => row.Show(result), row => row.IsEnabled);
        Pair(Extractions, extractions, (row, result) => row.Show(result), row => row.IsEnabled);
        OnPropertyChanged(nameof(Badge));
        OnPropertyChanged(nameof(HasFailures));
    }

    private static void Pair<TRow, TResult>(IEnumerable<TRow> rows, IReadOnlyList<TResult> results, Action<TRow, TResult?> show, Func<TRow, bool> isEnabled)
        where TResult : class
    {
        var next = 0;
        foreach (var row in rows)
        {
            show(row, isEnabled(row) && next < results.Count ? results[next++] : null);
        }
    }

    private CheckRowViewModel NewRow(ResponseCheck check) => new(check, OnRowChanged, row => Checks.Remove(row));

    private ExtractionRowViewModel NewRow(Extraction extraction) => new(extraction, OnRowChanged, row => Extractions.Remove(row));

    private void OnRowChanged()
    {
        OnPropertyChanged(nameof(Badge));
        OnPropertyChanged(nameof(HasFailures));
        _changed();
    }

    private void OnRowsChanged()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasChecks));
        OnPropertyChanged(nameof(HasExtractions));
        OnRowChanged();
    }
}

/// <summary>One line of the Test results tab.</summary>
/// <param name="Passed">Passed, or the value was stored.</param>
/// <param name="Title">"Status equals 201" or "token ← $.access_token".</param>
/// <param name="Detail">The actual value, or why it failed.</param>
public sealed record TestResultLine(bool Passed, string Title, string Detail);

/// <summary>The results of the checks and extractions of the last send.</summary>
public sealed class TestRunViewModel : ViewModelBase
{
    private readonly IReadOnlyList<CheckResult> _checks;
    private readonly IReadOnlyList<ExtractionResult> _extractions;

    /// <summary>Creates the results.</summary>
    public TestRunViewModel(IReadOnlyList<CheckResult> checks, IReadOnlyList<ExtractionResult> extractions)
    {
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(extractions);
        _checks = checks;
        _extractions = extractions;
        PassedCount = checks.Count(c => c.Passed);
        FailedCount = checks.Count - PassedCount;
    }

    /// <summary>Checks that passed.</summary>
    public int PassedCount { get; }

    /// <summary>Checks that failed.</summary>
    public int FailedCount { get; }

    /// <summary>"3 passed · 1 failed".</summary>
    public string Summary => Loc.Format("Tests_Summary", PassedCount, FailedCount);

    /// <summary>Share of passed checks, 0 to 1, for the bar.</summary>
    public double PassedShare => PassedCount + FailedCount == 0 ? 1 : (double)PassedCount / (PassedCount + FailedCount);

    /// <summary>Any check ran.</summary>
    public bool HasChecks => _checks.Count > 0;

    /// <summary>Any extraction ran.</summary>
    public bool HasExtractions => _extractions.Count > 0;

    /// <summary>All checks passed.</summary>
    public bool AllPassed => FailedCount == 0;

    /// <summary>Badge on the results tab: "3/4".</summary>
    public string Badge => HasChecks ? $"{PassedCount}/{PassedCount + FailedCount}" : string.Empty;

    /// <summary>Check results, failures first. Titles follow the UI language; messages are as the run gave them.</summary>
    public IReadOnlyList<TestResultLine> Checks =>
        [.. _checks.OrderBy(c => c.Passed).Select(c => new TestResultLine(c.Passed, TestLabels.Describe(c.Check), c.Message ?? c.Actual ?? string.Empty))];

    /// <summary>Extraction results in order.</summary>
    public IReadOnlyList<TestResultLine> Extractions =>
    [
        .. _extractions.Select(e => new TestResultLine(
            e.IsSuccess,
            $"{e.Extraction.Variable} ← {(e.Extraction.Target.Length > 0 ? e.Extraction.Target : TestLabels.ExtractionSources.First(s => s.Value == e.Extraction.Source).Label)}",
            e.IsSuccess
                ? $"{e.Value} · {TestLabels.Targets.First(t => t.Value == e.Extraction.To).Label}"
                : $"{e.Error} {Loc.Get("Tests_NotChanged")}")),
    ];
}
