using System.Collections.Concurrent;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Services.Exe;

namespace Savepoint.Core.Services;

/// <summary>
/// What can be read about a game's install at one point of its history: how many mod files it holds and which version its
/// executable declares. Points never change, so every answer is remembered for the life of the process (D13.15).
/// </summary>
internal sealed class GameInsights
{
    private readonly BackendServices _services;
    private readonly ConcurrentDictionary<string, int> _modCounts = new(StringComparer.Ordinal);

    public GameInsights(BackendServices services)
    {
        _services = services;
    }

    /// <summary>Whether a file belongs to the mods the game's hints name: the hint itself, or anything inside it. Case-sensitive, as git is.</summary>
    public static bool MatchesHint(string relativePath, IReadOnlyList<string> hints)
    {
        foreach (var hint in hints)
        {
            var normalized = NormalizeHint(hint);
            if (normalized.Length > 0 && (relativePath == normalized || relativePath.StartsWith(normalized + "/", StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The number of mod files in the install at a commit; null when the game names no mod folders.</summary>
    public async Task<int?> ModFileCountAsync(RepoRef repo, string commit, IReadOnlyList<string> hints, CancellationToken cancellationToken)
    {
        var usable = hints.Select(NormalizeHint).Where(hint => hint.Length > 0 && !hint.Split('/').Contains("..")).Distinct(StringComparer.Ordinal).ToList();
        if (usable.Count == 0)
        {
            return null;
        }

        var key = $"{repo.GitDir}|{commit}|{string.Join('\u001f', usable)}";
        if (_modCounts.TryGetValue(key, out var known))
        {
            return known;
        }

        // Only the hinted paths are listed, so a huge install is never walked whole.
        var listing = await _services.Runner.RunAsync(
            new GitInvocation(["ls-tree", "-r", "-z", "--name-only", commit, "--", .. usable]) { GitDir = repo.GitDir, WorkingDirectory = repo.GitDir, ReadOnly = true },
            cancellationToken).ConfigureAwait(false);
        var count = listing.SplitStandardOutputOnNul().Count(path => MatchesHint(path, usable));
        _modCounts[key] = count;
        return count;
    }

    /// <summary>The version the game's executable declares at a commit; null for a URI launch, a missing exe or one with no version.</summary>
    public async Task<string?> DetectedVersionAsync(RepoRef repo, string commit, LaunchConfig launch, CancellationToken cancellationToken)
    {
        if (launch.Method != LaunchMethod.Exe || string.IsNullOrWhiteSpace(launch.ExePath))
        {
            return null;
        }

        var exe = launch.ExePath.Replace('\\', '/').Trim('/');
        var isBundle = exe.EndsWith(".app", StringComparison.OrdinalIgnoreCase);
        var path = isBundle ? exe + "/Contents/Info.plist" : exe;

        // The commit is a full id from a branch tip, so one process names the file's content and a second reads it only when it is new.
        var named = await ReadOnlyAsync(repo, cancellationToken, stdin: $"{commit}:{path}\n", "cat-file", "--batch-check").ConfigureAwait(false);
        var fields = named.StandardOutput.Trim().Split(' ');
        if (fields.Length != 3 || fields[1] != "blob")
        {
            return null;
        }

        var blobId = fields[0];
        return await _services.ExeVersions.GetOrReadAsync(
            (isBundle ? "plist:" : "pe:") + blobId,
            async token =>
            {
                var content = await _services.Runner.RunAsync(
                    new GitInvocation(["cat-file", "blob", blobId]) { GitDir = repo.GitDir, WorkingDirectory = repo.GitDir, ReadOnly = true, BinaryStdout = true },
                    token).ConfigureAwait(false);
                return isBundle ? ExeVersionReader.FromInfoPlist(content.StandardOutputBytes) : ExeVersionReader.FromPe(content.StandardOutputBytes);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private Task<GitResult> ReadOnlyAsync(RepoRef repo, CancellationToken cancellationToken, string? stdin, params string[] arguments) =>
        _services.Runner.RunAsync(
            new GitInvocation(arguments) { GitDir = repo.GitDir, WorkingDirectory = repo.GitDir, ReadOnly = true, Stdin = stdin },
            cancellationToken);

    private static string NormalizeHint(string hint) => hint.Replace('\\', '/').Trim('/', ' ');
}
