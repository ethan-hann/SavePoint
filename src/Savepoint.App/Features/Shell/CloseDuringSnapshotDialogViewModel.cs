using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;

namespace Savepoint.App.Features.Shell;

/// <summary>What the user chose when the window was closed while a snapshot was being taken.</summary>
public enum CloseDuringSnapshotChoice
{
    /// <summary>Back to the app; nothing changes.</summary>
    Stay,

    /// <summary>Minimize the window and let the snapshot finish.</summary>
    Minimize,

    /// <summary>Stop the snapshot, keep nothing from it, and close.</summary>
    CloseAndStop,
}

/// <summary>
/// "A snapshot is still being taken": closing the window would stop it, so the dialog offers to minimize instead. Escape and the
/// scrim answer <see cref="CloseDuringSnapshotChoice.Stay"/>.
/// </summary>
public sealed partial class CloseDuringSnapshotDialogViewModel : DialogViewModel<CloseDuringSnapshotChoice>
{
    /// <param name="text">Formats the copy.</param>
    public CloseDuringSnapshotDialogViewModel(UiFormatter text)
        : base(text.Message("shell.exit.captureTitle"))
    {
        Message = text.Message("shell.exit.captureMessage");
        MinimizeLabel = text.Message("shell.exit.captureMinimize");
        CloseLabel = text.Message("shell.exit.captureClose");
        StayLabel = text.Message("shell.exit.stay");
    }

    /// <summary>What closing does and what minimizing does instead.</summary>
    public string Message { get; }

    /// <summary>The minimize button's text.</summary>
    public string MinimizeLabel { get; }

    /// <summary>The close button's text, which says the snapshot stops.</summary>
    public string CloseLabel { get; }

    /// <summary>The stay-open button's text.</summary>
    public string StayLabel { get; }

    /// <inheritdoc />
    public override string Description => Message;

    /// <inheritdoc />
    public override double DialogWidth => 480;

    /// <inheritdoc />
    protected override CloseDuringSnapshotChoice DismissResult => CloseDuringSnapshotChoice.Stay;

    [RelayCommand]
    private void Minimize() => Close(CloseDuringSnapshotChoice.Minimize);

    [RelayCommand]
    private void CloseAndStop() => Close(CloseDuringSnapshotChoice.CloseAndStop);

    [RelayCommand]
    private void Stay() => Close(CloseDuringSnapshotChoice.Stay);
}
