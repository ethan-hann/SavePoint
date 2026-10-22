using Avalonia.Controls;
using Avalonia.Threading;
using Savepoint.App.Features.Shell;

namespace Savepoint.App.Views;

public partial class MainWindow : Window
{
    private bool _closeApproved;
    private bool _closeRequested;

    public MainWindow()
    {
        InitializeComponent();
    }

    // The window stays open while the exit guard asks; it closes itself again once the guard says it may. A quit from the application
    // (Cmd+Q on macOS) arrives as ApplicationShutdown and honors the cancel; only the system ending the session cannot be held up.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeApproved || DataContext is not MainWindowViewModel main || e.CloseReason is WindowCloseReason.OSShutdown)
        {
            return;
        }

        e.Cancel = true;
        if (!_closeRequested)
        {
            _closeRequested = true;
            _ = CloseWhenAllowedAsync(main);
        }
    }

    private async Task CloseWhenAllowedAsync(MainWindowViewModel main)
    {
        try
        {
            _closeApproved = await main.PrepareToCloseAsync();
        }
        finally
        {
            _closeRequested = false;
        }

        // Posted, so the close never runs inside the closing event that asked for it (that happens whenever nothing is running).
        if (_closeApproved)
        {
            Dispatcher.UIThread.Post(Close);
        }
    }
}
