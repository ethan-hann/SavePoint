using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Games;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Features.History;

/// <summary>
/// Asks for the name of the profile a recently removed snapshot comes back as, then makes it through the Api. The dialog stays open on a
/// refusal, with the reason under the name, so nothing the player typed is lost, and it answers true once the profile exists.
/// </summary>
public sealed class BringBackDialogViewModel : ProfileFormDialogViewModel
{
    private readonly Func<string, Task<OperationResult<Profile>>> _bringBack;

    /// <param name="snapshotName">The snapshot's name, which is also the name field's first value.</param>
    /// <param name="bringBack">Brings the snapshot back as a profile of the given name, through the Api.</param>
    /// <param name="text">Formats the dialog's copy.</param>
    public BringBackDialogViewModel(string snapshotName, Func<string, Task<OperationResult<Profile>>> bringBack, UiFormatter text)
        : base(text.Message("history.removed.dialog.title"), snapshotName, text)
    {
        _bringBack = bringBack;
        Body = text.Message("history.removed.dialog.body", ("name", snapshotName));
    }

    /// <summary>What the new profile starts from, and that nothing else changes.</summary>
    public string Body { get; }

    /// <inheritdoc />
    public override string Description => Body;

    /// <inheritdoc />
    protected override async Task<ErrorInfo?> SubmitAsync()
    {
        var brought = await _bringBack(Name.Trim());
        return brought.Ok ? null : brought.Error;
    }
}
