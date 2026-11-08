using System.Runtime.CompilerServices;
using JetApi.Desktop.Localization;

namespace JetApi.Desktop.Tests;

// View models share the UI language and Avalonia's UI thread, so tests run one at a time (see xunit.runner.json).
internal static class TestSetup
{
    [ModuleInitializer]
    internal static void Initialize() => Loc.Instance.SetLanguage("en");
}
