using Avalonia.Controls.Primitives;

namespace Savepoint.App.Features.Games;

/// <summary>
/// A chip that shows one of a screen's views as selected or not but never flips itself when pressed: the selected chip
/// is the view that is open, and a chip for another view runs its command and leaves for that view. It keeps the toggle
/// button's look and its on/off state for assistive technology; set <c>IsChecked</c> for the view that is open.
/// </summary>
public sealed class TabChip : ToggleButton
{
    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(ToggleButton);

    /// <inheritdoc />
    protected override void Toggle()
    {
    }
}
