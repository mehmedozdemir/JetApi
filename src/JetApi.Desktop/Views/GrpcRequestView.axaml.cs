using Avalonia.Controls;

namespace JetApi.Desktop.Views;

/// <summary>The gRPC request editor and response panel.</summary>
public sealed partial class GrpcRequestView : UserControl
{
    /// <summary>Creates the view.</summary>
    public GrpcRequestView()
    {
        InitializeComponent();
    }

    /// <summary>Moves focus to the server URL (Ctrl+L).</summary>
    public void FocusUrl()
    {
        UrlBox.FocusEditor();
        UrlBox.SelectAll();
    }
}
