using JetApi.Desktop.Localization;
using JetApi.Desktop.Services;

namespace JetApi.Desktop.ViewModels;

/// <summary>One version in a release notes list.</summary>
/// <param name="Heading">"0.3.0" or "Unreleased".</param>
/// <param name="Date">Release date as shown, empty when unknown.</param>
/// <param name="Blocks">The notes.</param>
public sealed record ReleaseNotesSection(string Heading, string Date, IReadOnlyList<MarkdownBlock> Blocks)
{
    /// <summary>The section has a date.</summary>
    public bool HasDate => Date.Length > 0;

    /// <summary>Builds a section from changelog notes.</summary>
    public static ReleaseNotesSection From(ReleaseNotes notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        return new ReleaseNotesSection(
            notes.Version?.ToString() ?? Loc.Get("Notes_Unreleased"),
            notes.Date?.ToString("d MMMM yyyy", Loc.Instance.Culture) ?? string.Empty,
            MarkdownText.Parse(notes.Markdown, notes.LinkBase));
    }

    /// <summary>Builds a section from a published release (its notes come from GitHub).</summary>
    public static ReleaseNotesSection From(ReleaseInfo release)
    {
        ArgumentNullException.ThrowIfNull(release);
        var notes = string.IsNullOrWhiteSpace(release.Notes) ? Loc.Get("Update_NoNotes") : release.Notes;
        return new ReleaseNotesSection(
            release.Version.ToString(),
            release.PublishedAt?.ToLocalTime().ToString("d MMMM yyyy", Loc.Instance.Culture) ?? string.Empty,
            MarkdownText.Parse(notes, new Uri(AppInfo.RepositoryUrl + "/blob/v" + release.Version + "/")));
    }
}

/// <summary>The release notes window: what changed in each version, from the changelog shipped with the app.</summary>
public sealed class ReleaseNotesViewModel : ViewModelBase
{
    /// <summary>Creates the window's model.</summary>
    /// <param name="notes">Changelog sections, newest first.</param>
    /// <param name="current">The running version.</param>
    /// <param name="updatedFrom">After an update: the previous version; only newer notes are shown.</param>
    public ReleaseNotesViewModel(IReadOnlyList<ReleaseNotes> notes, ReleaseVersion current, ReleaseVersion? updatedFrom = null)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(current);
        // A release closes its "Unreleased" section, so that section (dropped when empty) only shows in development builds.
        var shown = updatedFrom is null
            ? notes
            : notes.Where(n => n.Version is { } version && version > updatedFrom && version <= current);
        Sections = [.. shown.Select(ReleaseNotesSection.From)];
        Title = updatedFrom is null ? Loc.Get("Notes_Title") : Loc.Format("Notes_UpdatedTitle", current);
        Subtitle = updatedFrom is null ? Loc.Format("Notes_Subtitle", current) : Loc.Format("Notes_UpdatedSubtitle", updatedFrom);
    }

    /// <summary>Window heading.</summary>
    public string Title { get; }

    /// <summary>Line under the heading.</summary>
    public string Subtitle { get; }

    /// <summary>Versions, newest first.</summary>
    public IReadOnlyList<ReleaseNotesSection> Sections { get; }

    /// <summary>Nothing to show.</summary>
    public bool IsEmpty => Sections.Count == 0;

    /// <summary>The notes are written in Turkish; English users are told so.</summary>
    public bool ShowsLanguageNote => !Loc.Instance.IsTurkish;
}
