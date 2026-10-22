using Avalonia.Controls;
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

    // The window stays open while the exit guard asks; it closes itself again once the guard says it may.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeApproved || DataContext is not MainWindowViewModel main || e.CloseReason is WindowCloseReason.OSShutdown or WindowCloseReason.ApplicationShutdown)
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

        if (_closeApproved)
        {
            Close();
        }
    }
}
