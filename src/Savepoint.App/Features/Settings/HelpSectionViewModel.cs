using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Shell;

namespace Savepoint.App.Features.Settings;

/// <summary>The Help section: the way into the "How SavePoint works" page, and the way to see the first-run tour again.</summary>
public sealed partial class HelpSectionViewModel : ObservableObject
{
    private readonly IShellContext _shell;

    /// <summary>True when the tour can be shown now: the selected game has a profile for it to point at.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTourHint))]
    private bool _canShowTour;

    internal HelpSectionViewModel(IShellContext shell)
    {
        _shell = shell;
        ShowHowItWorksCommand = new RelayCommand(shell.ShowHelp);
        ShowTourCommand = new RelayCommand(shell.StartTour, () => CanShowTour);
        Refresh();
    }

    /// <summary>Opens the "How SavePoint works" page.</summary>
    public IRelayCommand ShowHowItWorksCommand { get; }

    /// <summary>Shows the first-run tour again, from its first step.</summary>
    public IRelayCommand ShowTourCommand { get; }

    /// <summary>True while the tour cannot be shown, which is when the screen says why.</summary>
    public bool ShowTourHint => !CanShowTour;

    /// <summary>Asks the shell again whether the tour can run, as the screen is shown, because a game or profile may have appeared or gone.</summary>
    internal void Refresh() => CanShowTour = _shell.CanShowTour;

    partial void OnCanShowTourChanged(bool value) => ShowTourCommand.NotifyCanExecuteChanged();
}
