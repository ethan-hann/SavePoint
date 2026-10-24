using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Savepoint.App.Features.Common.Accessibility;

namespace Savepoint.App.Features.Common.Errors;

/// <summary>The error card; its DataContext is an <see cref="ErrorCardViewModel"/>.</summary>
public partial class ErrorCardView : UserControl
{
    private ErrorCardViewModel? _card;

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

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_card is not null)
        {
            _card.CopyRequested -= OnCopyRequested;
        }

        _card = DataContext as ErrorCardViewModel;
        if (_card is not null)
        {
            _card.CopyRequested += OnCopyRequested;
        }
    }

    private async void OnCopyRequested(object? sender, string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
            _card?.ConfirmCopied();
        }
    }
}
