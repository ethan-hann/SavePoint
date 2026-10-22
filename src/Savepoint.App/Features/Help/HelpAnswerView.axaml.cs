using Avalonia.Controls;
using Avalonia.Input;

namespace Savepoint.App.Features.Help;

/// <summary>The answer of one explanation, a callout that shows while it is open; its DataContext is a <see cref="ContextualHelpViewModel"/>.</summary>
public partial class HelpAnswerView : UserControl
{
    /// <summary>Creates the view.</summary>
    public HelpAnswerView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        ContextualHelpKeys.CloseOnEscape(e, DataContext);
    }
}
