using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Savepoint.App.Features.Editor;

/// <summary>The game editor pane; its DataContext is a <see cref="GameEditorViewModel"/>.</summary>
public partial class GameEditorView : UserControl
{
    /// <summary>Creates the view.</summary>
    public GameEditorView()
    {
        InitializeComponent();
        InstallField.LostFocus += OnInstallFieldLostFocus;
        NameField.LostFocus += OnInstallFieldLostFocus;

        // The pane replaces the screen the user was on, so the focus has to be given to it rather than left behind.
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => NameField.Focus());
    }

    // The install folder and the game's name are only looked at once the user has finished with the field, not on every key.
    private void OnInstallFieldLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GameEditorViewModel editor)
        {
            _ = editor.RefreshSuggestionsAsync();
        }
    }
}
