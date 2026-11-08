using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JetApi.Http;

namespace JetApi.Desktop.ViewModels;

/// <summary>The request as code: cURL, PowerShell, C#, JavaScript or Python, ready to copy.</summary>
public sealed partial class CodeSnippetViewModel : ViewModelBase
{
    // The last language chosen, so the dialog opens where the user left it.
    private static SnippetLanguage s_lastLanguage = OperatingSystem.IsWindows() ? SnippetLanguage.CurlWindows : SnippetLanguage.Curl;

    private readonly PreparedHttpRequest _request;
    private readonly Func<string, Task> _copy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Code), nameof(EditorLanguage))]
    private LocalizedOption<SnippetLanguage> _selectedLanguage;

    [ObservableProperty]
    private bool _copied;

    /// <summary>Creates the snippet view for a prepared (variables resolved) request.</summary>
    /// <param name="request">The request as it would be sent.</param>
    /// <param name="copy">Copies text to the clipboard.</param>
    public CodeSnippetViewModel(PreparedHttpRequest request, Func<string, Task> copy)
    {
        _request = request;
        _copy = copy;
        Languages =
        [
            new(SnippetLanguage.Curl, "Snippet_Curl"),
            new(SnippetLanguage.CurlWindows, "Snippet_CurlWindows"),
            new(SnippetLanguage.PowerShell, "Snippet_PowerShell"),
            new(SnippetLanguage.CSharp, "Snippet_CSharp"),
            new(SnippetLanguage.JavaScript, "Snippet_JavaScript"),
            new(SnippetLanguage.Python, "Snippet_Python"),
        ];
        _selectedLanguage = Languages.First(l => l.Value == s_lastLanguage);
    }

    /// <summary>Languages offered.</summary>
    public IReadOnlyList<LocalizedOption<SnippetLanguage>> Languages { get; }

    /// <summary>The code.</summary>
    public string Code => CodeSnippetGenerator.Generate(_request, SelectedLanguage.Value);

    /// <summary>The highlighting grammar of <see cref="Code"/>.</summary>
    public string EditorLanguage => SelectedLanguage.Value switch
    {
        SnippetLanguage.Curl => "shellscript",
        SnippetLanguage.CurlWindows => "bat",
        SnippetLanguage.PowerShell => "powershell",
        SnippetLanguage.CSharp => "csharp",
        SnippetLanguage.JavaScript => "javascript",
        SnippetLanguage.Python => "python",
        _ => "text",
    };

    partial void OnSelectedLanguageChanged(LocalizedOption<SnippetLanguage> value)
    {
        s_lastLanguage = value.Value;
        Copied = false;
    }

    [RelayCommand]
    private async Task CopyAsync()
    {
        await _copy(Code).ConfigureAwait(true);
        Copied = true;
    }
}
