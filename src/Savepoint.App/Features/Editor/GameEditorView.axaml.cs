using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Savepoint.App.Features.Editor;

/// <summary>The game editor pane; its DataContext is a <see cref="GameEditorViewModel"/>.</summary>
public partial class GameEditorView : UserControl
{
    private GameEditorViewModel? _editor;

    /// <summary>Creates the view.</summary>
    public GameEditorView()
    {
        InitializeComponent();
        InstallField.LostFocus += OnInstallFieldLostFocus;
        NameField.LostFocus += OnInstallFieldLostFocus;

        // The match list is part of the Name field: the arrow keys go into it and out of it, and it closes when the focus is somewhere else.
        NameField.AddHandler(KeyDownEvent, OnNameKeyDown, RoutingStrategies.Tunnel);
        NameMatchList.AddHandler(KeyDownEvent, OnMatchKeyDown, RoutingStrategies.Tunnel);
        NameBlock.AddHandler(LostFocusEvent, OnNameBlockLostFocus);

        // The pane replaces the screen the user was on, so the focus has to be given to it rather than left behind.
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => NameField.Focus());
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_editor is not null)
        {
            _editor.FocusNameRequested -= OnFocusNameRequested;
            _editor.ProcessNameFieldFocusRequested -= OnProcessFieldFocusRequested;
        }

        _editor = DataContext as GameEditorViewModel;
        if (_editor is not null)
        {
            _editor.FocusNameRequested += OnFocusNameRequested;
            _editor.ProcessNameFieldFocusRequested += OnProcessFieldFocusRequested;
        }
    }

    // After a match is chosen the list is gone, so the focus goes back to the field the player was typing in.
    private void OnFocusNameRequested(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() => NameField.Focus());

    // The field is in a section that has only just opened, so it takes the focus once it has been laid out.
    private void OnProcessFieldFocusRequested(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => ProcessField.Focus(), DispatcherPriority.Background);

    private IReadOnlyList<Button> Matches() => [.. NameMatchList.GetVisualDescendants().OfType<Button>()];

    private void OnNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && _editor is { HasNameMatches: true } && Matches() is [var first, ..])
        {
            first.Focus();
            e.Handled = true;
        }
    }

    private void OnMatchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Down or Key.Up) || TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Button focused)
        {
            return;
        }

        var matches = Matches();
        var index = matches.ToList().IndexOf(focused);
        if (index < 0)
        {
            return;
        }

        if (e.Key == Key.Down && index + 1 < matches.Count)
        {
            matches[index + 1].Focus();
        }
        else if (e.Key == Key.Up)
        {
            (index == 0 ? NameField : (Control)matches[index - 1]).Focus();
        }

        e.Handled = true;
    }

    // The focus has to settle before it can be said to have left: moving from the field into the list is not leaving.
    private void OnNameBlockLostFocus(object? sender, RoutedEventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (_editor is not { HasNameMatches: true })
            {
                return;
            }

            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
            if (focused is null || !(ReferenceEquals(focused, NameBlock) || focused.GetVisualAncestors().Contains(NameBlock)))
            {
                _editor.DismissNameMatchesCommand.Execute(null);
            }
        });

    // The install folder and the game's name are only looked at once the user has finished with the field, not on every key.
    private void OnInstallFieldLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GameEditorViewModel editor)
        {
            _ = editor.RefreshSuggestionsAsync();
        }
    }
}
