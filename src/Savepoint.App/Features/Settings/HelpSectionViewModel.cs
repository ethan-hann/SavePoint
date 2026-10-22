using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Shell;

namespace Savepoint.App.Features.Settings;

/// <summary>The Help section: the way into the "How SavePoint works" page.</summary>
public sealed class HelpSectionViewModel
{
    internal HelpSectionViewModel(IShellContext shell)
    {
        ShowHowItWorksCommand = new RelayCommand(shell.ShowHelp);
    }

    /// <summary>Opens the "How SavePoint works" page.</summary>
    public IRelayCommand ShowHowItWorksCommand { get; }
}
