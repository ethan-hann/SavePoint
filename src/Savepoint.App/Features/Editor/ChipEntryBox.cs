using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Savepoint.App.Features.Editor;

/// <summary>
/// The text field a chip list shows while a value is being added or a chip is edited: it takes focus when it appears with
/// its text selected, and leaving it runs <see cref="CommitCommand"/> so a value typed and left behind is kept, as Enter would.
/// </summary>
public sealed class ChipEntryBox : TextBox
{
    /// <summary>Runs when the field loses focus.</summary>
    public static readonly StyledProperty<ICommand?> CommitCommandProperty =
        AvaloniaProperty.Register<ChipEntryBox, ICommand?>(nameof(CommitCommand));

    /// <summary>Creates the field.</summary>
    public ChipEntryBox()
    {
        LostFocus += (_, _) =>
        {
            if (CommitCommand?.CanExecute(null) == true)
            {
                CommitCommand.Execute(null);
            }
        };
    }

    /// <summary>Runs when the field loses focus.</summary>
    public ICommand? CommitCommand
    {
        get => GetValue(CommitCommandProperty);
        set => SetValue(CommitCommandProperty, value);
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(TextBox);

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && change.GetNewValue<bool>())
        {
            // An edited chip's text starts selected, so typing replaces it.
            Dispatcher.UIThread.Post(() =>
            {
                Focus();
                SelectAll();
            });
        }
    }
}
