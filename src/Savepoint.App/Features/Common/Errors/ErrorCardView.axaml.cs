using Avalonia;
using Avalonia.Controls;
using Savepoint.App.Features.Common.Accessibility;

namespace Savepoint.App.Features.Common.Errors;

/// <summary>The error card; its DataContext is an <see cref="ErrorCardViewModel"/>.</summary>
public partial class ErrorCardView : UserControl
{
    /// <summary>Creates the card; bind or set the DataContext.</summary>
    public ErrorCardView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is ErrorCardViewModel card)
        {
            Announcements.Appeared(this, $"{card.Title} {card.Body}".Trim());
        }
    }
}
