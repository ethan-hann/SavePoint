namespace Savepoint.App.Features.Common.Errors;

/// <summary>
/// Picks the catalog key of an error's text for the operation it happened in. Every key is written out in full, so the catalog lint
/// sees each one; an id or operation with no key of its own falls back to the id's neutral text.
/// </summary>
public static class ErrorCopy
{
    private const string _fileHeld = "error.gitOperation.checkoutFailed";
    private const string _diskFull = "error.diskSpace.low";

    /// <summary>The key of the card's body text.</summary>
    /// <param name="messageId">The error's message id, which is also the neutral text's key.</param>
    /// <param name="operation">What SavePoint was doing.</param>
    public static string BodyKey(string messageId, ErrorOperation operation) => messageId switch
    {
        _fileHeld => operation switch
        {
            ErrorOperation.Switch => "errorCard.body.fileInUse.switch",
            ErrorOperation.Snapshot => "errorCard.body.fileInUse.snapshot",
            ErrorOperation.FreeUpSpace => "errorCard.body.fileInUse.freeUpSpace",
            ErrorOperation.MoveSnapshots => "errorCard.body.fileInUse.moveSnapshots",
            ErrorOperation.ClearData => "errorCard.body.fileInUse.clearData",
            _ => messageId,
        },
        _diskFull => operation switch
        {
            ErrorOperation.Switch => "errorCard.body.noRoom.switch",
            ErrorOperation.Snapshot => "errorCard.body.noRoom.snapshot",
            ErrorOperation.FreeUpSpace => "errorCard.body.noRoom.freeUpSpace",
            ErrorOperation.MoveSnapshots => "errorCard.body.noRoom.moveSnapshots",
            _ => messageId,
        },
        _ => messageId,
    };

    /// <summary>The key of the card's headline.</summary>
    /// <param name="messageId">The error's message id.</param>
    /// <param name="operation">What SavePoint was doing.</param>
    public static string TitleKey(string messageId, ErrorOperation operation) => messageId == _fileHeld
        ? operation switch
        {
            ErrorOperation.Switch => "errorCard.title.fileInUse.switch",
            ErrorOperation.Snapshot => "errorCard.title.fileInUse.snapshot",
            ErrorOperation.FreeUpSpace => "errorCard.title.fileInUse.freeUpSpace",
            ErrorOperation.MoveSnapshots => "errorCard.title.fileInUse.moveSnapshots",
            ErrorOperation.ClearData => "errorCard.title.fileInUse.clearData",
            _ => ErrorPresentation.For(messageId).TitleKey,
        }
        : ErrorPresentation.For(messageId).TitleKey;
}
