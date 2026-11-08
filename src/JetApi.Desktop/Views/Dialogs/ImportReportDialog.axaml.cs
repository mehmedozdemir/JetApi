using Avalonia.Controls;
using Avalonia.Interactivity;
using JetApi.Desktop.Localization;
using JetApi.Desktop.Services;

namespace JetApi.Desktop.Views.Dialogs;

/// <summary>The import report: what each file brought in and what could not be carried over.</summary>
public sealed partial class ImportReportDialog : Window
{
    /// <summary>Required by the XAML loader.</summary>
    public ImportReportDialog()
    {
        InitializeComponent();
    }

    /// <summary>Creates the dialog.</summary>
    public ImportReportDialog(ImportReport report)
        : this()
    {
        ArgumentNullException.ThrowIfNull(report);
        Title = Loc.Get("Import_ReportTitle");
        TitleText.Text = Title;
        var notes = report.Files.Sum(f => f.Issues.Count);
        BodyText.Text = notes == 0 ? Loc.Get("Import_ReportClean") : Loc.Format("Import_ReportNotes", notes);
        FileList.ItemsSource = report.Files;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close(true);
}
