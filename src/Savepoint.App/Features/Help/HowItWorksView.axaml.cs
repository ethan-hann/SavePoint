using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Savepoint.App.Features.Help;

/// <summary>The "How SavePoint works" pane; its DataContext is a <see cref="HowItWorksViewModel"/>.</summary>
public partial class HowItWorksView : UserControl
{
    private HowItWorksViewModel? _page;

    /// <summary>Creates the view.</summary>
    public HowItWorksView()
    {
        InitializeComponent();

        // The page replaces the screen the player was on, so focus is given to it rather than left behind: to the topic a link
        // pointed at, otherwise to Back.
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(
            () =>
            {
                if (!ShowRequestedSection())
                {
                    BackButton.Focus();
                }
            },
            DispatcherPriority.Background);
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_page is not null)
        {
            _page.SectionRequested -= OnSectionRequested;
        }

        _page = DataContext as HowItWorksViewModel;
        if (_page is not null)
        {
            _page.SectionRequested += OnSectionRequested;
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape && DataContext is HowItWorksViewModel page)
        {
            page.BackCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnSectionRequested(object? sender, EventArgs e) => ShowRequestedSection();

    // Scrolls the topic's heading to the top of the page and gives it focus, so a screen reader reads from there.
    private bool ShowRequestedSection()
    {
        if (_page?.Section is not { } section)
        {
            return false;
        }

        var heading = TopicList.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(block => block.Focusable && block.DataContext is HowItWorksTopic topic && topic.Section == section);
        if (heading is null)
        {
            return false;
        }

        heading.Focus();
        ScrollToTop(heading);

        // The pictures above the topic settle to their final height only after the first layout, which moves the heading.
        Dispatcher.UIThread.Post(() => ScrollToTop(heading), DispatcherPriority.Background);
        return true;
    }

    private void ScrollToTop(Control heading)
    {
        UpdateLayout();
        if (heading.TranslatePoint(new Point(0, 0), PageScroll) is { } top)
        {
            PageScroll.Offset = new Vector(PageScroll.Offset.X, Math.Max(0, PageScroll.Offset.Y + top.Y - 16));
        }
    }
}
