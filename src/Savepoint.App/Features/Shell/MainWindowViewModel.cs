using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Toasts;

namespace Savepoint.App.Features.Shell;

/// <summary>
/// What the main window shows: the startup screens until the checks pass, then the shell, with the dialog overlay and
/// the toasts above both.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly Func<ShellViewModel> _createShell;
    private readonly ExitGuard? _exit;
    private bool _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Content), nameof(ShowTour))]
    private ShellViewModel? _shell;

    /// <param name="startup">The startup checks and their screens.</param>
    /// <param name="createShell">Builds the shell once the checks pass.</param>
    /// <param name="dialogs">The modal stack shown above everything.</param>
    /// <param name="toasts">The transient messages shown above everything.</param>
    /// <param name="exit">Decides whether the window may close while jobs run; the window always closes when null.</param>
    public MainWindowViewModel(StartupViewModel startup, Func<ShellViewModel> createShell, DialogHostViewModel dialogs, ToastHostViewModel toasts, ExitGuard? exit = null)
    {
        _exit = exit;
        Startup = startup;
        _createShell = createShell;
        Dialogs = dialogs;
        Toasts = toasts;
        startup.Ready += OnStartupReady;
        dialogs.PropertyChanged += OnDialogsChanged;
    }

    private void OnStartupReady(object? sender, EventArgs e) => _ = ShowShellAsync();

    private void OnDialogsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DialogHostViewModel.IsOpen))
        {
            OnPropertyChanged(nameof(ShowTour));
        }
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.ShowTour))
        {
            OnPropertyChanged(nameof(ShowTour));
        }
    }

    partial void OnShellChanging(ShellViewModel? value)
    {
        if (Shell is not null)
        {
            Shell.PropertyChanged -= OnShellPropertyChanged;
        }
    }

    partial void OnShellChanged(ShellViewModel? value)
    {
        if (value is not null)
        {
            value.PropertyChanged += OnShellPropertyChanged;
        }
    }

    /// <summary>The startup checks.</summary>
    public StartupViewModel Startup { get; }

    /// <summary>The modal stack.</summary>
    public DialogHostViewModel Dialogs { get; }

    /// <summary>The transient messages.</summary>
    public ToastHostViewModel Toasts { get; }

    /// <summary>
    /// True while the first-run tour's card and outline should show: the shell says the tour is on a step in view, and no dialog covers
    /// it. The tour waits behind a dialog, such as the one that made the first profile, and shows when the dialog closes.
    /// </summary>
    public bool ShowTour => Shell?.ShowTour == true && !Dialogs.IsOpen;

    /// <summary>The shell, or null until the startup checks have passed.</summary>
    public object Content => Shell ?? (object)Startup;

    /// <summary>Called when the window is asked to close; asks, and stops what can be stopped, when jobs run.</summary>
    /// <returns>True when the window may close.</returns>
    public Task<bool> PrepareToCloseAsync() => _exit?.PrepareToCloseAsync() ?? Task.FromResult(true);

    /// <summary>Runs the startup checks; the shell appears when they pass.</summary>
    public Task InitializeAsync() => Startup.CheckAsync();

    /// <summary>
    /// Opens the "How SavePoint works" page for the help key. It does nothing before the shell exists and while a dialog is open,
    /// because a page that opened behind a dialog could not be read or used.
    /// </summary>
    /// <returns>True when the page opened.</returns>
    public bool TryShowHelp()
    {
        if (Shell is null || Dialogs.IsOpen)
        {
            return false;
        }

        Shell.ShowHelp();
        return true;
    }

    /// <summary>
    /// Ends the window's view model: stops following the startup checks, the dialogs and the shell, then disposes the shell and the toasts,
    /// which this view model owns. Safe to call more than once.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Startup.Ready -= OnStartupReady;
        Dialogs.PropertyChanged -= OnDialogsChanged;
        if (Shell is { } shell)
        {
            shell.PropertyChanged -= OnShellPropertyChanged;
            shell.Dispose();
        }

        Toasts.Dispose();
    }

    private async Task ShowShellAsync()
    {
        if (Shell is not null || _disposed)
        {
            return;
        }

        var shell = _createShell();
        await shell.InitializeAsync();
        if (_disposed)
        {
            // The window ended while the shell loaded; nothing will show it.
            shell.Dispose();
            return;
        }

        Shell = shell;
    }
}
