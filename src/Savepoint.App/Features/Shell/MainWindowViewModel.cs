using CommunityToolkit.Mvvm.ComponentModel;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Toasts;

namespace Savepoint.App.Features.Shell;

/// <summary>
/// What the main window shows: the startup screens until the checks pass, then the shell, with the dialog overlay and
/// the toasts above both.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly Func<ShellViewModel> _createShell;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Content))]
    private ShellViewModel? _shell;

    /// <param name="startup">The startup checks and their screens.</param>
    /// <param name="createShell">Builds the shell once the checks pass.</param>
    /// <param name="dialogs">The modal stack shown above everything.</param>
    /// <param name="toasts">The transient messages shown above everything.</param>
    public MainWindowViewModel(StartupViewModel startup, Func<ShellViewModel> createShell, DialogHostViewModel dialogs, ToastHostViewModel toasts)
    {
        Startup = startup;
        _createShell = createShell;
        Dialogs = dialogs;
        Toasts = toasts;
        startup.Ready += (_, _) => _ = ShowShellAsync();
    }

    /// <summary>The startup checks.</summary>
    public StartupViewModel Startup { get; }

    /// <summary>The modal stack.</summary>
    public DialogHostViewModel Dialogs { get; }

    /// <summary>The transient messages.</summary>
    public ToastHostViewModel Toasts { get; }

    /// <summary>The shell, or null until the startup checks have passed.</summary>
    public object Content => Shell ?? (object)Startup;

    /// <summary>Runs the startup checks; the shell appears when they pass.</summary>
    public Task InitializeAsync() => Startup.CheckAsync();

    private async Task ShowShellAsync()
    {
        if (Shell is not null)
        {
            return;
        }

        var shell = _createShell();
        await shell.InitializeAsync();
        Shell = shell;
    }
}
