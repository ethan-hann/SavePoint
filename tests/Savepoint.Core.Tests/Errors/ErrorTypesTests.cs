using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Tests.Errors;

public class ErrorTypesTests
{
    private sealed record ErrorCase(SavepointException Exception, string MessageId, (string Key, object? Value)[] Args);

    private static readonly IReadOnlyList<string> _roles = ["install", "saves"];
    private static readonly IReadOnlyList<string> _branches = ["main", "modded"];
    private static readonly IReadOnlyList<ValidationIssue> _issues = [new ValidationIssue("name", "error.validation.required")];

    private static readonly Dictionary<string, ErrorCase> _cases = BuildCases();

    public static TheoryData<string> CaseNames => new(_cases.Keys);

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Exception_reports_its_message_id_and_args(string caseName)
    {
        var expected = _cases[caseName];

        Assert.Equal(expected.MessageId, expected.Exception.MessageId);
        Assert.Equal(expected.Args.Select(a => a.Key).Order(), expected.Exception.Args.Keys.Order());
        foreach (var (key, value) in expected.Args)
        {
            Assert.Equal(value, expected.Exception.Args[key]);
        }
    }

    [Fact]
    public void Base_args_are_empty_but_never_null_when_none_are_given()
    {
        var error = new SampleException("error.sample", "dev text");

        Assert.NotNull(error.Args);
        Assert.Empty(error.Args);
    }

    [Fact]
    public void Base_exposes_the_args_it_was_given()
    {
        var args = new Dictionary<string, object?> { ["count"] = 3 };

        var error = new SampleException("error.sample", "dev text", args);

        Assert.Equal(3, error.Args.GetValueOrDefault("count"));
    }

    [Fact]
    public void Message_is_the_developer_text_and_inner_exception_is_kept()
    {
        var inner = new IOException("disk gone");

        var error = new SampleException("error.sample", "dev text", inner: inner);

        Assert.Equal("dev text", error.Message);
        Assert.Same(inner, error.InnerException);
    }

    [Fact]
    public void Subclasses_with_an_inner_parameter_keep_it()
    {
        var inner = new IOException("disk gone");

        Assert.Same(inner, new LaunchSpawnException(LaunchMethod.Exe, "could not start", inner).InnerException);
        Assert.Same(inner, new CatalogCorruptException("catalog.json", inner).InnerException);
        Assert.Same(inner, new GitUnavailableException("git.exe", "not runnable", inner).InnerException);
    }

    [Fact]
    public void Subclasses_with_a_developer_message_use_it_as_the_message()
    {
        Assert.Equal("could not start", new LaunchSpawnException(LaunchMethod.Exe, "could not start", null).Message);
        Assert.Equal("index.lock held", new GitOperationException("/games/alpha", "index.lock held").Message);
        Assert.Equal("not runnable", new GitUnavailableException("git.exe", "not runnable", null).Message);
    }

    [Fact]
    public void ErrorInfo_maps_a_savepoint_exception_to_its_message_id_and_args()
    {
        var error = new GameRunningException("game-1", "Alpha");

        var info = ErrorInfo.From(error);

        Assert.Equal("error.gameService.gameRunning", info.MessageId);
        Assert.Equal("game-1", info.Args["gameId"]);
        Assert.Equal("Alpha", info.Args["gameName"]);
        Assert.Equal(error.ToString(), info.Detail);
    }

    [Theory]
    [InlineData(LaunchMethod.Exe, "exe")]
    [InlineData(LaunchMethod.Uri, "uri")]
    public void ErrorInfo_gives_the_launch_method_to_the_copy_as_its_json_name(LaunchMethod method, string name)
    {
        var info = ErrorInfo.From(new LaunchSpawnException(method, "could not start", null));

        Assert.Equal(name, info.Args["method"]);
    }

    [Fact]
    public void ErrorInfo_maps_any_other_exception_to_unknown()
    {
        var error = CaptureThrown(new InvalidOperationException("boom"));

        var info = ErrorInfo.From(error);

        Assert.Equal("error.unknown", info.MessageId);
        Assert.Empty(info.Args);
        Assert.Equal(error.ToString(), info.Detail);
    }

    private static Exception CaptureThrown(Exception error)
    {
        try
        {
            throw error;
        }
        catch (Exception caught)
        {
            return caught;
        }
    }

    private static Dictionary<string, ErrorCase> BuildCases()
    {
        var cases = new Dictionary<string, ErrorCase>();

        void Add(string name, SavepointException exception, string messageId, params (string Key, object? Value)[] args) =>
            cases.Add(name, new ErrorCase(exception, messageId, args));

        Add("Artwork not an image", new ArtworkNotAnImageException("/pictures/a.png"), "error.artwork.notAnImage", ("path", "/pictures/a.png"));
        Add("Artwork too large", new ArtworkTooLargeException("/pictures/a.png", 25), "error.artwork.tooLarge",
            ("path", "/pictures/a.png"), ("maxMegabytes", 25));
        Add("PathSafety tier 1", new PathSafetyException("/system", 1), "error.pathSafety.tier1Blocked",
            ("path", "/system"), ("tier", 1));
        Add("PathSafety tier 2", new PathSafetyException("/home/me", 2), "error.pathSafety.tier2Blocked",
            ("path", "/home/me"), ("tier", 2));
        Add("ConsentMarker missing", new ConsentMarkerException(ConsentFailure.Missing, "/games/alpha", RepoRole.Install, null, null),
            "error.consentMarker.missing",
            ("reason", ConsentFailure.Missing), ("path", "/games/alpha"), ("role", RepoRole.Install),
            ("existingGameId", null), ("existingRole", null));
        Add("ConsentMarker conflict", new ConsentMarkerException(ConsentFailure.Conflict, "/games/alpha", RepoRole.Install, "game-2", RepoRole.External),
            "error.consentMarker.conflict",
            ("reason", ConsentFailure.Conflict), ("path", "/games/alpha"), ("role", RepoRole.Install),
            ("existingGameId", "game-2"), ("existingRole", RepoRole.External));
        Add("DiskSpace", new DiskSpaceException("/games", 5_000L, 1_000L), "error.diskSpace.low",
            ("path", "/games"), ("requiredBytes", 5_000L), ("freeBytes", 1_000L));
        Add("GitOperation", new GitOperationException("/games/alpha", "checkout failed"), "error.gitOperation.checkoutFailed",
            ("path", "/games/alpha"));
        Add("GitOperation without path", new GitOperationException(null, "checkout failed"), "error.gitOperation.checkoutFailed",
            ("path", null));
        Add("GitOperation not a locked file", new GitOperationException("the repository does not exist"), "error.gitOperation.failed");
        Add("GameAlreadyExists", new GameAlreadyExistsException("alpha"), "error.gameService.alreadyExists", ("gameId", "alpha"));
        Add("NoHistoryToReconnect", new NoHistoryToReconnectException("alpha"), "error.gameService.noHistoryToReconnect", ("gameId", "alpha"));
        Add("AmendNotAvailable", new AmendNotAvailableException(), "error.gitOperation.amendNotAvailable");
        Add("Cancelled capture", new OperationCancelledByUserException(CancelledOperation.Capture, "game-1"),
            "error.capture.cancelled", ("op", CancelledOperation.Capture), ("gameId", "game-1"));
        Add("Cancelled compaction", new OperationCancelledByUserException(CancelledOperation.Compaction, null),
            "error.compaction.cancelled", ("op", CancelledOperation.Compaction), ("gameId", null));
        Add("Compaction preempted", new CompactionPreemptedException("game-1"), "error.compaction.preempted",
            ("gameId", "game-1"));
        Add("Compaction preempted for every game", new CompactionPreemptedException(null), "error.compaction.preempted",
            ("gameId", null));
        Add("Cancelled launch", new OperationCancelledByUserException(CancelledOperation.Launch, "game-1"),
            "error.launch.canceled", ("op", CancelledOperation.Launch), ("gameId", "game-1"));
        Add("Cancelled storage relocation", new OperationCancelledByUserException(CancelledOperation.StorageRelocation, null),
            "error.storageRelocation.cancelled", ("op", CancelledOperation.StorageRelocation), ("gameId", null));
        Add("GameOverlap", new GameOverlapException("game-2", "Beta"), "error.gameService.pathOverlap",
            ("conflictingGameId", "game-2"), ("conflictingGameName", "Beta"));
        Add("ProfileActive", new ProfileActiveException("profile-1"), "error.gameService.profileActive",
            ("profileId", "profile-1"));
        Add("ReconnectableGame", new ReconnectableGameException("game-2", _roles, _branches), "error.gameService.reconnectable",
            ("existingGameId", "game-2"), ("roles", _roles), ("branchNames", _branches));
        Add("GameRunning", new GameRunningException("game-1", "Alpha"), "error.gameService.gameRunning",
            ("gameId", "game-1"), ("gameName", "Alpha"));
        Add("LaunchAlreadyRunning", new LaunchAlreadyRunningException("game-1"), "error.launch.alreadyRunning",
            ("gameId", "game-1"));
        Add("NoActiveSession", new NoActiveSessionException("game-1"), "error.launch.noActiveSession",
            ("gameId", "game-1"));
        Add("SessionStillRunning", new SessionStillRunningException("game-1"), "error.launch.sessionStillRunning",
            ("gameId", "game-1"));
        Add("DirtyWorkingTree", new DirtyWorkingTreeException(RepoRole.Install), "error.launch.dirtyWorkingTree",
            ("role", RepoRole.Install));
        Add("InterruptedSwitch", new InterruptedSwitchException(RepoRole.Install, "0123456789abcdef0123456789abcdef01234567"), "error.launch.switchInterrupted",
            ("role", RepoRole.Install), ("targetPoint", "0123456789abcdef0123456789abcdef01234567"));
        Add("InterruptedSwitch without a target", new InterruptedSwitchException(RepoRole.External), "error.launch.switchInterrupted",
            ("role", RepoRole.External), ("targetPoint", null));
        Add("DetachedCaptureNotSupported", new DetachedCaptureNotSupportedException(RepoRole.External), "error.launch.detachedCaptureNotSupported",
            ("role", RepoRole.External));
        Add("LaunchSpawn", new LaunchSpawnException(LaunchMethod.Exe, "could not start", null), "error.launch.spawnFailed",
            ("method", LaunchMethod.Exe));
        Add("LaunchWindowsProgram", new LaunchWindowsProgramException("/games/alpha/Game.exe"), "error.launch.windowsProgram",
            ("path", "/games/alpha/Game.exe"));
        Add("PathMissing", new PathMissingException("/games/alpha", RepoRole.Install), "error.path.missing",
            ("path", "/games/alpha"), ("role", RepoRole.Install));
        Add("PathMissing external", new PathMissingException("/saves/alpha", RepoRole.External), "error.path.missing",
            ("path", "/saves/alpha"), ("role", RepoRole.External));
        Add("StorageDestinationNotEmpty", new StorageDestinationNotEmptyException("/storage"), "error.storageRelocation.destinationNotEmpty",
            ("path", "/storage"));
        Add("StorageInsideInstallFolder", new StorageInsideInstallFolderException("/install/data"), "error.storageRelocation.insideInstallFolder",
            ("path", "/install/data"));
        Add("StorageSourceUnreadable", new StorageSourceUnreadableException("/storage/alpha", "dev text", null), "error.storageRelocation.sourceUnreadable",
            ("path", "/storage/alpha"));
        Add("StorageHistoryNotFound", new StorageHistoryNotFoundException("/elsewhere"), "error.storage.noHistoryHere",
            ("path", "/elsewhere"));
        Add("FileUnreadable", new FileUnreadableException("/games/alpha/data.pak"), "error.gitOperation.unreadableFile",
            ("path", "/games/alpha/data.pak"));
        Add("StorageRemoveFailed", new StorageRemoveFailedException("/storage/alpha", "dev text"), "error.storage.removeFailed",
            ("path", "/storage/alpha"));
        Add("StorageMoveUnfinished", new StorageMoveUnfinishedException("/storage"), "error.storageRelocation.moveUnfinished",
            ("path", "/storage"));
        Add("Validation", new ValidationException(_issues), "error.validation.invalidPayload",
            ("issues", _issues));
        Add("CatalogCorrupt", new CatalogCorruptException("catalog.json", null), "error.catalog.corrupt",
            ("path", "catalog.json"));
        Add("GitUnavailable", new GitUnavailableException("git.exe", "not runnable", null), "error.git.unavailable",
            ("path", "git.exe"));
        Add("SessionMoved", new SessionMovedException(RepoRole.External, "dev text"), "error.launch.sessionMoved",
            ("role", RepoRole.External));
        Add("PointNotFound", new PointNotFoundException("point-1"), "error.history.pointNotFound",
            ("pointId", "point-1"));
        Add("ProfileAlreadyExists", new ProfileAlreadyExistsException("profile-1"), "error.profile.alreadyExists",
            ("profileId", "profile-1"));
        Add("ProfileHasNoPoints", new ProfileHasNoPointsException("profile-1"), "error.profile.noPoints",
            ("profileId", "profile-1"));
        Add("NestedRepository", new NestedRepositoryException("/games/alpha/mod/.git", 2), "error.capture.nestedRepository",
            ("path", "/games/alpha/mod/.git"), ("count", 2));
        Add("LinkedFolder", new LinkedFolderException(@"C:\Games\Alpha\Mods"), "error.capture.linkedFolder",
            ("path", @"C:\Games\Alpha\Mods"));
        Add("SwitchNoteStuck", new SwitchNoteStuckException(RepoRole.External, @"D:\SavePoint\alpha.git\savepoint-switch.json"), "error.launch.switchNoteStuck",
            ("role", RepoRole.External), ("path", @"D:\SavePoint\alpha.git\savepoint-switch.json"));
        Add("FolderChanged", new FolderChangedException(@"C:\Games\Alpha", RepoRole.Install), "error.path.changed",
            ("path", @"C:\Games\Alpha"), ("role", RepoRole.Install));
        Add("FileFolderClash folder", new FileFolderClashException(RepoRole.Install, FileFolderClashKind.FolderHoldsFiles, "/games/alpha/Mods/Foo", "/games/alpha/Mods/Foo/data.bin"), "error.launch.fileFolderClash",
            ("role", RepoRole.Install), ("kind", FileFolderClashKind.FolderHoldsFiles), ("folder", "/games/alpha/Mods/Foo"), ("name", "Foo"), ("path", "/games/alpha/Mods/Foo/data.bin"));
        Add("FileFolderClash file", new FileFolderClashException(RepoRole.External, FileFolderClashKind.FileInTheWay, "/saves/alpha/Slots", "/saves/alpha/Slots"), "error.launch.fileFolderClash",
            ("role", RepoRole.External), ("kind", FileFolderClashKind.FileInTheWay), ("folder", "/saves/alpha/Slots"), ("name", "Slots"), ("path", "/saves/alpha/Slots"));

        Add("NamesDifferOnlyInCase", new NamesDifferOnlyInCaseException(RepoRole.Install, "/games/alpha/Mods/Foo", "/games/alpha/Mods/foo"), "error.launch.caseClash",
            ("role", RepoRole.Install), ("path", "/games/alpha/Mods/Foo"), ("name", "Foo"), ("other", "foo"));

        return cases;
    }

    private sealed class SampleException : SavepointException
    {
        public SampleException(string messageId, string devMessage, IReadOnlyDictionary<string, object?>? args = null, Exception? inner = null)
            : base(messageId, devMessage, args, inner)
        {
        }
    }
}
