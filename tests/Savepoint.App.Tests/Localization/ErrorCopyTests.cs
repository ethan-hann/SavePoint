using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Localization;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Localization;

// The message for an error id may only use arguments that error carries, plus `os`, which the UI always adds.
public class ErrorCopyTests
{
    private static readonly IReadOnlyList<string> _operatingSystems = ["windows", "macos", "linux"];

    private static IEnumerable<ErrorInfo> Samples()
    {
        IReadOnlyList<string> roles = ["install", "mods", "external"];
        IReadOnlyList<string> branches = ["main"];
        IReadOnlyList<ValidationIssue> issues = [new ValidationIssue("name", ValidationMessageIds.GameNameRequired)];

        SavepointException[] errors =
        [
            new ArtworkNotAnImageException("/pictures/a.png"),
            new ArtworkTooLargeException("/pictures/a.png", 25),
            new PathSafetyException("/system", 1),
            new PathSafetyException("/home/me", 2),
            new ConsentMarkerException(ConsentFailure.Missing, "/games/alpha", RepoRole.Install, null, null),
            new ConsentMarkerException(ConsentFailure.Conflict, "/games/alpha", RepoRole.Install, "game-2", RepoRole.External),
            new DiskSpaceException("/games", 5_000L, 1_000L),
            new GitOperationException("/games/alpha/data.pak", "dev text"),
            new GitOperationException(null, "dev text"),
            new GitOperationException("dev text"),
            new RepositoryDamagedException("dev text"),
            new GameAlreadyExistsException("alpha"),
            new NoHistoryToReconnectException("alpha"),
            new AmendNotAvailableException(),
            new OperationCancelledByUserException(CancelledOperation.Capture, "game-1"),
            new OperationCancelledByUserException(CancelledOperation.Compaction, null),
            new CompactionPreemptedException("game-1"),
            new CompactionPreemptedException(null),
            new OperationCancelledByUserException(CancelledOperation.Launch, "game-1"),
            new OperationCancelledByUserException(CancelledOperation.StorageRelocation, null),
            new GameOverlapException("game-2", "Beta"),
            new FolderMismatchException(RepoRole.External, "/games/alpha/saves", 3, 212),
            new CompletionNotOfferedException("game-2", CompletionChoice.MoveForward),
            new ProfileActiveException("profile-1"),
            new ReconnectableGameException("game-2", roles, branches),
            new GameRunningException("game-1", "Alpha"),
            new SwitchInProgressException("game-1", "Alpha"),
            new LaunchAlreadyRunningException("game-1"),
            new NoActiveSessionException("game-1"),
            new SessionStillRunningException("game-1"),
            new SteamNotRunningException("game-1"),
            new SteamDidNotStartException("game-1"),
            new DirtyWorkingTreeException(RepoRole.Install),
            new InterruptedSwitchException(RepoRole.Install, "0123456789abcdef0123456789abcdef01234567"),
            new InterruptedSwitchException(RepoRole.External),
            new DetachedCaptureNotSupportedException(RepoRole.External),
            new LaunchSpawnException(LaunchMethod.Exe, "dev text", null),
            new LaunchSpawnException(LaunchMethod.Uri, "dev text", null),
            new LaunchWindowsProgramException("/games/alpha/Game.exe"),
            new PathMissingException("/games/alpha", RepoRole.Install),
            new PathMissingException("/games/alpha/saves", RepoRole.External),
            new StorageDestinationNotEmptyException("/storage"),
            new StorageDefaultFolderHasHistoryException("/storage"),
            new StorageStillReachableException("/storage"),
            new StorageHistoryNotFoundException("/elsewhere"),
            new FileUnreadableException("/games/alpha/data.pak"),
            new StorageRemoveFailedException("/storage/alpha", "dev text"),
            new StorageMoveUnfinishedException("/storage"),
            new StorageInsideInstallFolderException("/install/data"),
            new StorageFolderHasLinkException("/storage/other/install.git/hooks"),
            new StorageWriteFailedException("/storage/alpha", "dev text", null),
            new StorageSourceUnreadableException("/storage/alpha", "dev text", null),
            new ValidationException(issues),
            new CatalogCorruptException("/catalog/games.json", null),
            new GitUnavailableException("/app/git/git", "dev text", null),
            new RequestCancelledException(),
            new StorageUnreachableException("/storage", null),
            new StorageRelocationFailedException("/new/storage/a.pack", "dev text", null),
            new GameNotFoundException("game-1"),
            new ProfileNotFoundException("game-1", "profile-1"),
            new PointNotFoundException("point-1"),
            new NestedRepositoryException("/games/alpha/mod/.git", 1),
            new NestedRepositoryException("/games/alpha/mod/.git", 3),
            new LinkedFolderException("/games/alpha/Mods"),
            new LinkedFileException("/games/alpha/Mods/Cool Mod.esp"),
            new SnapshotHasLinksException("/games/alpha/Mods/Cool Mod.esp"),
            new FileFolderClashException(RepoRole.Install, FileFolderClashKind.FolderHoldsFiles, "/games/alpha/Mods/Foo", "/games/alpha/Mods/Foo/data.bin"),
            new FileFolderClashException(RepoRole.External, FileFolderClashKind.FileInTheWay, "/saves/alpha/Slots", "/saves/alpha/Slots"),
            new NamesDifferOnlyInCaseException(RepoRole.Install, "/games/alpha/Mods/Foo", "/games/alpha/Mods/foo"),
            new NamesDifferOnlyInCaseException(RepoRole.External, "/saves/alpha/Slot", "/saves/alpha/slot"),
            new FolderChangedException("/games/alpha", RepoRole.Install),
            new FolderChangedException("/saves/alpha", RepoRole.External),
            new SwitchNoteStuckException(RepoRole.Install, "/storage/alpha.git/savepoint-switch.json"),
            new SwitchNoteStuckException(RepoRole.External, "/storage/alpha.git/savepoint-switch.json"),
            new SessionMovedException(RepoRole.Install, "dev text"),
            new ProfileAlreadyExistsException("profile-1"),
            new ProfileHasNoPointsException("profile-1"),
            new ProfileNotLoadedException("profile-1", RepoRole.Install),
            new ProfileNotLoadedException("profile-1", RepoRole.External),
            new RemovedSnapshotNotFoundException("snapshot-1"),
            new RemovedSnapshotNotRestorableException("snapshot-1"),
        ];

        return errors.Select(ErrorInfo.From).Concat(ModsSamples()).Append(ErrorInfo.From(new InvalidOperationException("boom")));
    }

    // Errors the Api can raise for the mods folder, which the copy has to name as such.
    private static IEnumerable<ErrorInfo> ModsSamples() =>
        new SavepointException[]
        {
            new FolderMismatchException(RepoRole.Mods, "/mods", 3, 212),
            new NamesDifferOnlyInCaseException(RepoRole.Mods, "/mods/Foo", "/mods/foo"),
            new FileFolderClashException(RepoRole.Mods, FileFolderClashKind.FolderHoldsFiles, "/mods/Foo", "/mods/Foo/data.bin"),
            new FileFolderClashException(RepoRole.Mods, FileFolderClashKind.FileInTheWay, "/mods/Slots", "/mods/Slots"),
            new InterruptedSwitchException(RepoRole.Mods, "0123456789abcdef0123456789abcdef01234567"),
            new InterruptedSwitchException(RepoRole.Mods),
            new SwitchNoteStuckException(RepoRole.Mods, "/storage/alpha.git/savepoint-switch.json"),
            new FolderChangedException("/mods", RepoRole.Mods),
            new PathMissingException("/mods", RepoRole.Mods),
            new ProfileNotLoadedException("profile-1", RepoRole.Mods),
        }.Select(ErrorInfo.From);

    [Fact]
    public void Every_error_about_the_mods_folder_calls_it_the_mods_folder()
    {
        var catalog = LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true);
        var problems = new List<string>();

        foreach (var info in ModsSamples())
        {
            var text = new UiFormatter(catalog, os: "windows").Message(info);
            if (!text.Contains("mods folder", StringComparison.Ordinal))
            {
                problems.Add($"{info.MessageId} does not say \"mods folder\": {text}");
            }
        }

        LintAssert.NoProblems(problems);
    }

    [Fact]
    public void Every_error_message_formats_with_the_arguments_its_error_carries()
    {
        var catalog = LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true);
        var problems = new List<string>();

        foreach (var info in Samples())
        {
            var problem = _operatingSystems.Select(os => FormatProblem(catalog, info, os)).FirstOrDefault(found => found is not null);
            if (problem is not null)
            {
                problems.Add(problem);
            }
        }

        LintAssert.NoProblems(problems.Distinct().ToList());
    }

    [Fact]
    public void A_folder_that_is_not_confirmed_says_where_to_confirm_it()
    {
        var info = ErrorInfo.From(new ConsentMarkerException(ConsentFailure.Missing, "/games/alpha", RepoRole.Install, null, null));

        var text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: "windows").Message(info);

        Assert.Equal("This folder isn't confirmed yet. Open Edit game and choose Save changes to confirm it.", text);
    }

    [Theory]
    [InlineData("windows", @"Try something like C:\Program Files (x86)\Steam\steamapps\common\YourGame.")]
    [InlineData("macos", "Try something like ~/Library/Application Support/Steam/steamapps/common/YourGame.")]
    [InlineData("linux", "Try something like ~/.local/share/Steam/steamapps/common/YourGame.")]
    public void A_folder_that_is_too_broad_suggests_what_a_game_folder_looks_like(string os, string suggestion)
    {
        var catalog = LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true);

        foreach (var tier in new[] { 1, 2 })
        {
            var text = new UiFormatter(catalog, os: os).Message(ErrorInfo.From(new PathSafetyException("/system", tier)));

            Assert.EndsWith(suggestion, text, StringComparison.Ordinal);
        }
    }

    private static string? FormatProblem(LocalizationCatalog catalog, ErrorInfo info, string os)
    {
        try
        {
            var text = new UiFormatter(catalog, os: os).Message(info);
            return text.Contains('{', StringComparison.Ordinal) || text.Contains('}', StringComparison.Ordinal)
                ? $"{info.MessageId} (os={os}) leaves a brace in its text: {text}"
                : null;
        }
        catch (Exception error)
        {
            return $"{info.MessageId} (os={os}): {error.Message}";
        }
    }

    [Fact]
    public void Every_error_key_in_the_catalog_has_a_sample_error()
    {
        var sampled = Samples().Select(info => info.MessageId).ToHashSet(StringComparer.Ordinal);

        var unsampled = CatalogLint.English().Keys
            .Where(key => key.StartsWith("error.", StringComparison.Ordinal) && !sampled.Contains(key))
            .ToList();

        LintAssert.NoProblems(unsampled.Select(key => $"'{key}' has no sample error in ErrorCopyTests.Samples").ToList());
    }

    [Fact]
    public void Error_messages_leave_paths_to_the_error_card()
    {
        var inlined = CatalogLint.English()
            .Where(entry => entry.Key.StartsWith("error.", StringComparison.Ordinal))
            .Where(entry => CatalogLint.Arguments(entry.Value).Any(argument => argument.Name == "path"))
            .Select(entry => $"'{entry.Key}' puts {{path}} in its text; the card shows Args[\"path\"] on its own surface")
            .ToList();

        LintAssert.NoProblems(inlined);
    }
}
