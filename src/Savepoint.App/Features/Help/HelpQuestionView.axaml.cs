using Avalonia.Controls;
using Avalonia.Input;
using Savepoint.App.Features.Common.Accessibility;

namespace Savepoint.App.Features.Help;

/// <summary>The "What's this?" button of one explanation; its DataContext is a <see cref="ContextualHelpViewModel"/>.</summary>
public partial class HelpQuestionView : UserControl
{
    private ContextualHelpViewModel? _help;

    /// <summary>Creates the view.</summary>
    public HelpQuestionView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_help is not null)
        {
            _help.PropertyChanged -= OnHelpChanged;
            _help.FocusQuestionRequested -= OnFocusRequested;
        }

        _help = DataContext as ContextualHelpViewModel;
        if (_help is not null)
        {
            _help.PropertyChanged += OnHelpChanged;
            _help.FocusQuestionRequested += OnFocusRequested;
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        ContextualHelpKeys.CloseOnEscape(e, DataContext);
    }

    // An answer that appears is only spoken when something that is always on screen says it: the window's announcer, which this view reaches.
    private void OnHelpChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ContextualHelpViewModel.IsOpen) && _help is { IsOpen: true } open)
        {
            Announcements.Appeared(this, open.Answer);
        }
    }

    private void OnFocusRequested(object? sender, EventArgs e) => QuestionButton.Focus(NavigationMethod.Tab);
}
