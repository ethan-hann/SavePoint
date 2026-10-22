using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;

namespace Savepoint.App.Features.Launch;

/// <summary>What SavePoint checked before it started a game without asking: a short list with a Close button.</summary>
public sealed partial class LaunchChecksDialogViewModel : DialogViewModel<bool>
{
    /// <param name="checks">The lines to show.</param>
    /// <param name="text">Formats the title.</param>
    public LaunchChecksDialogViewModel(IReadOnlyList<LaunchCheckLine> checks, UiFormatter text)
        : base(text.Message("launch.checks.title"))
    {
        Checks = checks;
    }

    /// <summary>The checks, in order.</summary>
    public IReadOnlyList<LaunchCheckLine> Checks { get; }

    /// <inheritdoc />
    public override double DialogWidth => 520;

    /// <inheritdoc />
    public override bool DismissesOnScrimClick => true;

    /// <inheritdoc />
    protected override bool DismissResult => true;

    [RelayCommand]
    private void CloseDialog() => Close(true);
}
