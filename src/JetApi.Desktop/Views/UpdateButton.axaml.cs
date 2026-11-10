using Avalonia.Controls;

namespace JetApi.Desktop.Views;

/// <summary>The top bar button that opens the update dialog when a newer release is available.</summary>
public sealed partial class UpdateButton : UserControl
{
    /// <summary>Creates the button; the data context is an <see cref="ViewModels.UpdatesViewModel"/>.</summary>
    public UpdateButton()
    {
        InitializeComponent();
    }
}
