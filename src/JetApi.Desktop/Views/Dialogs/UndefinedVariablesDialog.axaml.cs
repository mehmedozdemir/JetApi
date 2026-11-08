using Avalonia.Controls;
using Avalonia.Interactivity;
using JetApi.Core.Variables;
using JetApi.Desktop.Localization;
using JetApi.Desktop.Services;

namespace JetApi.Desktop.Views.Dialogs;

/// <summary>Shown before sending a request that has undefined variables.</summary>
public sealed partial class UndefinedVariablesDialog : Window
{
    /// <summary>Required by the XAML loader.</summary>
    public UndefinedVariablesDialog()
    {
        InitializeComponent();
    }

    /// <summary>Creates the dialog.</summary>
    public UndefinedVariablesDialog(IReadOnlyList<VariableIssue> issues, string? environmentName)
        : this()
    {
        ArgumentNullException.ThrowIfNull(issues);
        var title = issues.Count == 1 ? Loc.Get("Vars_UndefinedTitleOne") : Loc.Format("Vars_UndefinedTitle", issues.Count);
        Title = title;
        TitleText.Text = title;
        BodyText.Text = Loc.Format("Vars_UndefinedBody", environmentName ?? Loc.Get("Top_NoEnvironment"));
        IssueList.ItemsSource = issues
            .Select(issue => "{{" + issue.Name + "}}" + (issue.Kind == VariableIssueKind.Cycle ? "  · " + Loc.Get("Vars_Cycle") : string.Empty))
            .ToList();
    }

    private void OnSend(object? sender, RoutedEventArgs e) => Close(UndefinedVariablesChoice.SendAnyway);

    private void OnOpenEnvironments(object? sender, RoutedEventArgs e) => Close(UndefinedVariablesChoice.OpenEnvironments);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(UndefinedVariablesChoice.Cancel);
}
