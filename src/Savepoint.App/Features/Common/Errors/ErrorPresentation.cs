using Savepoint.App.Theme;

namespace Savepoint.App.Features.Common.Errors;

/// <summary>How serious an error card is drawn: the icon takes the matching color.</summary>
public enum ErrorTone
{
    /// <summary>Something failed or was refused; the error color.</summary>
    Error,

    /// <summary>Something needs attention but nothing was lost; the warning color.</summary>
    Warning,

    /// <summary>Informational, or the user's own cancellation; a neutral color.</summary>
    Neutral,
}

/// <summary>The icon, tone and headline that go with one error message id.</summary>
/// <param name="Icon">An <see cref="Icons"/> glyph.</param>
/// <param name="Tone">Which color the icon takes.</param>
/// <param name="TitleKey">Catalog key of the card's headline.</param>
public sealed record ErrorKind(string Icon, ErrorTone Tone, string TitleKey);

/// <summary>
/// Maps an error message id to how the error card presents it. The table is static and every key is written out in
/// full, so the catalog lint sees each one; a test requires every <c>error.*</c> message to have an entry, which makes
/// adding an error id a decision about its icon and headline rather than an accident.
/// </summary>
public static class ErrorPresentation
{
    private static readonly ErrorKind _generic = new(Icons.Error, ErrorTone.Error, "errorCard.title.generic");

    private static readonly IReadOnlyDictionary<string, ErrorKind> _table = new Dictionary<string, ErrorKind>(StringComparer.Ordinal)
    {
        ["error.artwork.notAnImage"] = new(Icons.Warning, ErrorTone.Warning, "errorCard.title.notAnImage"),
        ["error.artwork.tooLarge"] = new(Icons.Warning, ErrorTone.Warning, "errorCard.title.imageTooLarge"),
        ["error.capture.cancelled"] = new(Icons.Cancel, ErrorTone.Neutral, "errorCard.title.canceled"),
        ["error.capture.linkedFolder"] = new(Icons.Folder, ErrorTone.Warning, "errorCard.title.folderIsShortcut"),
        ["error.capture.nestedRepository"] = new(Icons.Folder, ErrorTone.Warning, "errorCard.title.folderKeepsHistory"),
        ["error.catalog.corrupt"] = new(Icons.Report, ErrorTone.Error, "errorCard.title.dataUnreadable"),
        ["error.compaction.cancelled"] = new(Icons.Cancel, ErrorTone.Neutral, "errorCard.title.canceled"),
        ["error.compaction.preempted"] = new(Icons.Schedule, ErrorTone.Neutral, "errorCard.title.optimizingPaused"),
        ["error.consentMarker.conflict"] = new(Icons.Block, ErrorTone.Error, "errorCard.title.folderBelongsElsewhere"),
        ["error.consentMarker.missing"] = new(Icons.VerifiedUser, ErrorTone.Warning, "errorCard.title.folderUnconfirmed"),
        ["error.diskSpace.low"] = new(Icons.Storage, ErrorTone.Warning, "errorCard.title.noRoom"),
        ["error.game.notFound"] = new(Icons.Info, ErrorTone.Neutral, "errorCard.title.notFound"),
        ["error.gameService.alreadyExists"] = new(Icons.Info, ErrorTone.Neutral, "errorCard.title.alreadyInLibrary"),
        ["error.gameService.noHistoryToReconnect"] = new(Icons.History, ErrorTone.Warning, "errorCard.title.noHistoryToReconnect"),
        ["error.gameService.gameRunning"] = new(Icons.SportsEsports, ErrorTone.Warning, "errorCard.title.gameRunning"),
        ["error.gameService.folderMismatch"] = new(Icons.Folder, ErrorTone.Warning, "errorCard.title.folderLooksWrong"),
        ["error.gameService.pathOverlap"] = new(Icons.Block, ErrorTone.Error, "errorCard.title.folderOverlaps"),
        ["error.gameService.profileActive"] = new(Icons.Info, ErrorTone.Neutral, "errorCard.title.profileActive"),
        ["error.gameService.reconnectable"] = new(Icons.History, ErrorTone.Neutral, "errorCard.title.historyFound"),
        ["error.git.unavailable"] = new(Icons.Error, ErrorTone.Error, "errorCard.title.toolMissing"),
        ["error.gitOperation.amendNotAvailable"] = new(Icons.Info, ErrorTone.Neutral, "errorCard.title.generic"),
        ["error.gitOperation.failed"] = new(Icons.Warning, ErrorTone.Error, "errorCard.title.generic"),
        ["error.gitOperation.checkoutFailed"] = new(Icons.Lock, ErrorTone.Error, "errorCard.title.fileInUse"),
        ["error.gitOperation.unreadableFile"] = new(Icons.Lock, ErrorTone.Error, "errorCard.title.fileUnreadable"),
        ["error.history.pointNotFound"] = new(Icons.History, ErrorTone.Neutral, "errorCard.title.notFound"),
        ["error.launch.alreadyRunning"] = new(Icons.SportsEsports, ErrorTone.Neutral, "errorCard.title.gameRunning"),
        ["error.launch.canceled"] = new(Icons.Cancel, ErrorTone.Neutral, "errorCard.title.canceled"),
        ["error.launch.detachedCaptureNotSupported"] = new(Icons.History, ErrorTone.Warning, "errorCard.title.olderSave"),
        ["error.launch.completionNotOffered"] = new(Icons.Info, ErrorTone.Neutral, "errorCard.title.generic"),
        ["error.launch.dirtyWorkingTree"] = new(Icons.Warning, ErrorTone.Warning, "errorCard.title.unsavedChanges"),
        ["error.launch.fileFolderClash"] = new(Icons.Folder, ErrorTone.Warning, "errorCard.title.fileAndFolderShareAName"),
        ["error.launch.noActiveSession"] = new(Icons.Info, ErrorTone.Neutral, "errorCard.title.generic"),
        ["error.launch.sessionMoved"] = new(Icons.History, ErrorTone.Warning, "errorCard.title.progressNotKept"),
        ["error.launch.sessionStillRunning"] = new(Icons.SportsEsports, ErrorTone.Neutral, "errorCard.title.gameRunning"),
        ["error.launch.spawnFailed"] = new(Icons.Error, ErrorTone.Error, "errorCard.title.gameDidNotStart"),
        ["error.launch.switchInterrupted"] = new(Icons.SwapHoriz, ErrorTone.Warning, "errorCard.title.switchUnfinished"),
        ["error.launch.windowsProgram"] = new(Icons.Error, ErrorTone.Error, "errorCard.title.gameDidNotStart"),
        ["error.operation.cancelled"] = new(Icons.Cancel, ErrorTone.Neutral, "errorCard.title.canceled"),
        ["error.path.missing"] = new(Icons.FolderOff, ErrorTone.Neutral, "errorCard.title.folderGone"),
        ["error.pathSafety.tier1Blocked"] = new(Icons.Block, ErrorTone.Error, "errorCard.title.folderTooBroad"),
        ["error.pathSafety.tier2Blocked"] = new(Icons.Block, ErrorTone.Error, "errorCard.title.folderTooBroad"),
        ["error.profile.alreadyExists"] = new(Icons.Info, ErrorTone.Neutral, "errorCard.title.profileExists"),
        ["error.profile.noPoints"] = new(Icons.Info, ErrorTone.Neutral, "errorCard.title.nothingSavedYet"),
        ["error.profile.notFound"] = new(Icons.Info, ErrorTone.Neutral, "errorCard.title.notFound"),
        ["error.removed.notFound"] = new(Icons.Info, ErrorTone.Neutral, "errorCard.title.notFound"),
        ["error.removed.notRestorable"] = new(Icons.Info, ErrorTone.Warning, "errorCard.title.cantBringBack"),
        ["error.storage.defaultFolderHasHistory"] = new(Icons.Folder, ErrorTone.Warning, "errorCard.title.folderNotEmpty"),
        ["error.storage.folderHasLink"] = new(Icons.Block, ErrorTone.Error, "errorCard.title.folderHoldsShortcut"),
        ["error.storage.noHistoryHere"] = new(Icons.FolderOff, ErrorTone.Warning, "errorCard.title.historyNotHere"),
        ["error.storage.removeFailed"] = new(Icons.Lock, ErrorTone.Error, "errorCard.title.removeFailed"),
        ["error.storage.stillReachable"] = new(Icons.Storage, ErrorTone.Neutral, "errorCard.title.storageReachable"),
        ["error.storage.unreachable"] = new(Icons.Storage, ErrorTone.Error, "errorCard.title.storageUnreachable"),
        ["error.storage.writeFailed"] = new(Icons.Lock, ErrorTone.Error, "errorCard.title.storageUnwritable"),
        ["error.storageRelocation.cancelled"] = new(Icons.Cancel, ErrorTone.Neutral, "errorCard.title.canceled"),
        ["error.storageRelocation.copyFailed"] = new(Icons.Storage, ErrorTone.Warning, "errorCard.title.generic"),
        ["error.storageRelocation.destinationNotEmpty"] = new(Icons.Folder, ErrorTone.Warning, "errorCard.title.folderNotEmpty"),
        ["error.storageRelocation.insideInstallFolder"] = new(Icons.Block, ErrorTone.Error, "errorCard.title.folderInsideApp"),
        ["error.storageRelocation.moveUnfinished"] = new(Icons.Folder, ErrorTone.Warning, "errorCard.title.folderNotEmpty"),
        ["error.storageRelocation.sourceUnreadable"] = new(Icons.Storage, ErrorTone.Warning, "errorCard.title.storageUnreadable"),
        ["error.unknown"] = new(Icons.Error, ErrorTone.Error, "errorCard.title.generic"),
        ["error.validation.invalidPayload"] = new(Icons.Warning, ErrorTone.Warning, "errorCard.title.checkFields"),
    };

    /// <summary>Every message id with a dedicated presentation.</summary>
    public static IReadOnlyDictionary<string, ErrorKind> Table => _table;

    /// <summary>The presentation for a message id; an id the table does not know (a newer Core than this app) gets the generic error look.</summary>
    public static ErrorKind For(string messageId) => _table.TryGetValue(messageId, out var kind) ? kind : _generic;
}
