using Savepoint.App.Localization;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Localization;

// The message for an error id may only use arguments that error carries, plus `os`, which the UI always adds.
public class ErrorCopyTests
{
    private static readonly IReadOnlyList<string> _operatingSystems = ["windows", "macos", "linux"];

    private static IEnumerable<ErrorInfo> Samples()
    {
        IReadOnlyList<string> roles = ["install", "external"];
        IReadOnlyList<string> branches = ["main"];
        IReadOnlyList<ValidationIssue> issues = [new ValidationIssue("name", ValidationMessageIds.GameNameRequired)];

        SavepointException[] errors =
        [
            new PathSafetyException("/system", 1),
            new PathSafetyException("/home/me", 2),
            new ConsentMarkerException(ConsentFailure.Missing, "/games/alpha", "install", null, null),
            new ConsentMarkerException(ConsentFailure.Conflict, "/games/alpha", "install", "game-2", "external"),
            new DiskSpaceException("/games", 5_000L, 1_000L),
            new GitOperationException("/games/alpha/data.pak", "dev text"),
            new GitOperationException(null, "dev text"),
            new AmendNotAvailableException(),
            new OperationCancelledByUserException(CancelledOperation.Capture, "game-1"),
            new OperationCancelledByUserException(CancelledOperation.Compaction, null),
            new OperationCancelledByUserException(CancelledOperation.Launch, "game-1"),
            new OperationCancelledByUserException(CancelledOperation.StorageRelocation, null),
            new GameOverlapException("game-2", "Beta"),
            new ProfileActiveException("profile-1"),
            new ReconnectableGameException("game-2", roles, branches),
            new GameRunningException("game-1", "Alpha"),
            new LaunchAlreadyRunningException("game-1"),
            new NoActiveSessionException("game-1"),
            new SessionStillRunningException("game-1"),
            new DirtyWorkingTreeException("install"),
            new DetachedCaptureNotSupportedException("external"),
            new LaunchSpawnException("exe", "dev text", null),
            new PathMissingException("/games/alpha", "install"),
            new PathMissingException("/games/alpha/saves", "external"),
            new PathMissingException("/games/alpha/saves", "saves"),
            new StorageDestinationNotEmptyException("/storage"),
            new ValidationException(issues),
            new CatalogCorruptException("/catalog/games.json", null),
            new GitUnavailableException("/app/git/git", "dev text", null),
        ];

        return errors.Select(ErrorInfo.From).Append(ErrorInfo.From(new InvalidOperationException("boom")));
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

    private static string? FormatProblem(LocalizationCatalog catalog, ErrorInfo info, string os)
    {
        var args = new Dictionary<string, object?>(info.Args) { ["os"] = os };
        try
        {
            var text = catalog.Format(info.MessageId, args);
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
}
