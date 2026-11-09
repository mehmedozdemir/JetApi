using System.Xml.Linq;
using JetApi.Desktop.Controls;

namespace JetApi.Desktop.Tests;

/// <summary>Every icon a view or view model names exists in Tokens.axaml.</summary>
public sealed class IconTests
{
    [Fact]
    public Task PaletteCommandIcons_Exist() => HeadlessApp.Session.Dispatch(CheckPaletteAsync, TestContext.Current.CancellationToken);

    [Fact]
    public void EveryIconKeyUsedInCode_IsDefined()
    {
        var root = FindRoot();
        var tokens = XDocument.Load(Path.Combine(root, "src", "JetApi.Desktop", "Themes", "Tokens.axaml"));
        var defined = tokens.Descendants().Select(e => (string?)e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))).OfType<string>().ToHashSet();
        var used = Directory.EnumerateFiles(Path.Combine(root, "src", "JetApi.Desktop"), "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".axaml", StringComparison.Ordinal)) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f), "\"(Icon\\.[A-Za-z]+)\"|StaticResource (Icon\\.[A-Za-z]+)").Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value))
            .ToHashSet();

        used.ShouldNotBeEmpty();
        used.Except(defined).ShouldBeEmpty();
    }

    private static async Task<bool> CheckPaletteAsync()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        using var vm = workspace.CreateViewModel(new FakeDialogs());
        vm.OpenPalette();
        vm.Palette!.Query = ">";
        var commands = vm.Palette.Results.Where(r => !r.IsRequest).ToList();
        commands.ShouldNotBeEmpty();
        commands.ShouldAllBe(c => Icon.FromKey(c.IconKey) != null);
        return true;
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "JetApi.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
