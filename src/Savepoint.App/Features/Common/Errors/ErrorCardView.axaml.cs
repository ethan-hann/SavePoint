using Avalonia.Controls;

namespace Savepoint.App.Features.Common.Errors;

/// <summary>The error card; its DataContext is an <see cref="ErrorCardViewModel"/>.</summary>
public partial class ErrorCardView : UserControl
{
    /// <summary>Creates the card; bind or set the DataContext.</summary>
    public ErrorCardView()
    {
        InitializeComponent();
    }
}
