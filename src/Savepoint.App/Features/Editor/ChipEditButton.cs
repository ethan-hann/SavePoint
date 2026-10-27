using Avalonia.Controls;
using Avalonia.Input;

namespace Savepoint.App.Features.Editor;

/// <summary>
/// The text of a chip, which opens the chip for editing. A keyboard press or a screen reader's invoke command clicks it, as for any
/// button; a click with the mouse or a finger does not, because opening a chip takes a double-click.
/// </summary>
public sealed class ChipEditButton : Button
{
    private bool _fromPointer;

    // Takes the Button theme.
    protected override Type StyleKeyOverride => typeof(Button);

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _fromPointer = true;
        try
        {
            base.OnPointerReleased(e);
        }
        finally
        {
            _fromPointer = false;
        }
    }

    /// <inheritdoc />
    protected override void OnClick()
    {
        if (!_fromPointer)
        {
            base.OnClick();
        }
    }
}
