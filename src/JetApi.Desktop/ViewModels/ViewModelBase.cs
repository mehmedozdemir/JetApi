using CommunityToolkit.Mvvm.ComponentModel;
using JetApi.Desktop.Localization;

namespace JetApi.Desktop.ViewModels;

/// <summary>Base of all view models. Computed, language-dependent texts refresh when the language changes.</summary>
public abstract class ViewModelBase : ObservableObject, ILocalizable
{
    /// <summary>Registers for language changes.</summary>
    protected ViewModelBase()
    {
        Loc.Instance.Subscribe(this);
    }

    /// <inheritdoc />
    public virtual void OnLanguageChanged() => OnPropertyChanged(string.Empty);
}

/// <summary>Something with a display label (lets XAML templates bind options of any type).</summary>
public interface ILabeled
{
    /// <summary>The label in the current language.</summary>
    string Label { get; }
}

/// <summary>A choice in a list whose label is a localized string.</summary>
/// <typeparam name="T">The value type.</typeparam>
public sealed class LocalizedOption<T> : ViewModelBase, ILabeled
{
    /// <summary>Creates the option.</summary>
    public LocalizedOption(T value, string labelKey)
    {
        Value = value;
        LabelKey = labelKey;
    }

    /// <summary>The value.</summary>
    public T Value { get; }

    /// <summary>The string key of the label.</summary>
    public string LabelKey { get; }

    /// <summary>The label in the current language.</summary>
    public string Label => Loc.Get(LabelKey);

    /// <inheritdoc />
    public override string ToString() => Label;
}
