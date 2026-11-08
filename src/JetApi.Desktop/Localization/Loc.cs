using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace JetApi.Desktop.Localization;

/// <summary>
/// UI strings in the current language (Turkish or English). Bindings use the indexer, so switching
/// the language updates every visible text immediately, without a restart.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    /// <summary>Turkish.</summary>
    public static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>English.</summary>
    public static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static readonly ResourceManager Resources = new("JetApi.Desktop.Localization.Strings", typeof(Loc).Assembly);

    private readonly List<WeakReference<ILocalizable>> _subscribers = [];

    private Loc()
    {
        Culture = DefaultCulture();
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The shared instance used by bindings and view models.</summary>
    public static Loc Instance { get; } = new();

    /// <summary>The current UI culture.</summary>
    public CultureInfo Culture { get; private set; }

    /// <summary>True when the UI is Turkish.</summary>
    public bool IsTurkish => Culture.TwoLetterISOLanguageName == "tr";

    /// <summary>The string for <paramref name="key"/>; <c>[key]</c> when it is missing, so gaps are visible.</summary>
    public string this[string key] => Resources.GetString(key, Culture) ?? $"[{key}]";

    /// <summary>Shortcut for <c>Instance[key]</c>.</summary>
    public static string Get(string key) => Instance[key];

    /// <summary>Formats the string for <paramref name="key"/> with <paramref name="args"/> in the UI culture.</summary>
    public static string Format(string key, params object?[] args) => string.Format(Instance.Culture, Instance[key], args);

    /// <summary>Switches the language: <c>tr</c>, <c>en</c> or <c>null</c> to follow the operating system.</summary>
    public void SetLanguage(string? language)
    {
        Culture = language switch
        {
            "tr" => Turkish,
            "en" => English,
            _ => DefaultCulture(),
        };
        CultureInfo.CurrentUICulture = Culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Culture)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsTurkish)));

        lock (_subscribers)
        {
            _subscribers.RemoveAll(reference => !reference.TryGetTarget(out _));
            foreach (var reference in _subscribers.ToList())
            {
                if (reference.TryGetTarget(out var subscriber))
                {
                    subscriber.OnLanguageChanged();
                }
            }
        }
    }

    /// <summary>
    /// Registers an object whose computed texts depend on the language. Held weakly, so view models
    /// that are no longer used are collected normally.
    /// </summary>
    public void Subscribe(ILocalizable subscriber)
    {
        lock (_subscribers)
        {
            _subscribers.Add(new WeakReference<ILocalizable>(subscriber));
        }
    }

    private static CultureInfo DefaultCulture() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr" ? Turkish : English;
}

/// <summary>An object that refreshes its texts when the UI language changes.</summary>
public interface ILocalizable
{
    /// <summary>Called after the language changed.</summary>
    void OnLanguageChanged();
}
