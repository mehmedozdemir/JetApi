using Avalonia.Controls;

namespace JetApi.Desktop.Views;

/// <summary>The HTTP request editor and response panel.</summary>
public sealed partial class HttpRequestView : UserControl
{
    /// <summary>Creates the view.</summary>
    public HttpRequestView()
    {
        InitializeComponent();
    }

    /// <summary>Moves focus to the URL box (Ctrl+L).</summary>
    public void FocusUrl()
    {
        UrlBox.Focus();
        UrlBox.SelectAll();
    }
}
