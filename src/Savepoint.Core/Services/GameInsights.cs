using System.Collections.Concurrent;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Services.Exe;

namespace Savepoint.Core.Services;

/// <summary>
/// What can be read about a game's install at one point of its history: how many mod files it holds, how large it is and
/// which version its executable declares. Points never change, so every answer is remembered for the life of the process (D13.15).
/// </summary>
internal sealed class GameInsights
{
    private readonly BackendServices _services;
    private readonly ConcurrentDictionary<string, int> _modCounts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _treeBytes = new(StringComparer.Ordinal);

    public GameInsights(BackendServices services)
    {
        _services = services;
    }

    /// <summary>
    /// Whether a file belongs to the mods the game's hints name: the hint itself, or anything inside it. Case-sensitive, as git is.
    /// A log or crash dump a mod writes while the game runs is not a mod file, whatever its folder and whatever its letter case: this
    /// only counts files for display, so it never needs git's case setting (unlike a leave-out rule, which does).
    /// </summary>
    public static bool MatchesHint(string relativePath, IReadOnlyList<string> hints)
    {
        if (RunTimeOutput.IsRunTimeOutput(relativePath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

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

    /// <summary>
    /// The number of mod files a profile holds: the install files at a commit that the game's hints name, plus every file of the mods
    /// folder at its commit. Null when neither is known: the game names no mod folders and the profile has no mods commit.
    /// </summary>
    /// <param name="repo">The install folder's repository.</param>
    /// <param name="commit">The install commit.</param>
    /// <param name="hints">The game's mod path hints, relative to the install folder.</param>
    /// <param name="mods">The mods folder's repository, or null when the game has none.</param>
    /// <param name="modsCommit">The mods commit, or null when the profile has none in the mods folder.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<int?> ModFileCountAsync(RepoRef repo, string commit, IReadOnlyList<string> hints, RepoRef? mods, string? modsCommit, CancellationToken cancellationToken)
    {
        var installCount = await ModFileCountAsync(repo, commit, hints, cancellationToken).ConfigureAwait(false);
        if (mods is null || modsCommit is null)
        {
            return installCount;
        }

        var key = $"{mods.GitDir}|{modsCommit}|*";
        if (!_modCounts.TryGetValue(key, out var modsCount))
        {
            var listing = await RepoGit.RunAsync(_services.Runner, mods.GitDir, true, cancellationToken, null, false, ["ls-tree", "-r", "-z", "--name-only", modsCommit]).ConfigureAwait(false);
            modsCount = listing.SplitStandardOutputOnNul().Count();
            _modCounts[key] = modsCount;
        }

        return (installCount ?? 0) + modsCount;
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
        var listing = await RepoGit.RunAsync(_services.Runner, repo.GitDir, true, cancellationToken, null, false, ["ls-tree", "-r", "-z", "--name-only", commit, "--", .. usable]).ConfigureAwait(false);
        var count = listing.SplitStandardOutputOnNul().Count(path => MatchesHint(path, usable));
        _modCounts[key] = count;
        return count;
    }

    /// <summary>The total size, in bytes, of every file a commit holds. One git process lists the whole tree with its sizes.</summary>
    public async Task<long> TreeBytesAsync(RepoRef repo, string commit, CancellationToken cancellationToken)
    {
        var key = $"{repo.GitDir}|{commit}";
        if (_treeBytes.TryGetValue(key, out var known))
        {
            return known;
        }

        var listing = await RepoGit.RunAsync(_services.Runner, repo.GitDir, true, cancellationToken, null, false, "ls-tree", "-r", "-l", "-z", commit).ConfigureAwait(false);
        var total = listing.SplitStandardOutputOnNul().Sum(SizeOfEntry);
        _treeBytes[key] = total;
        return total;
    }

    // An entry reads "<mode> <type> <object> <size, padded>\t<path>"; a submodule's size is "-" and counts for nothing.
    private static long SizeOfEntry(string entry)
    {
        var tab = entry.IndexOf('\t', StringComparison.Ordinal);
        var fields = (tab < 0 ? entry : entry[..tab]).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length == 4 && long.TryParse(fields[3], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var size) ? size : 0;
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
        var named = await RepoGit.RunAsync(_services.Runner, repo.GitDir, true, cancellationToken, $"{commit}:{path}\n", false, "cat-file", "--batch-check").ConfigureAwait(false);
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
                var content = await RepoGit.RunAsync(_services.Runner, repo.GitDir, true, token, null, true, "cat-file", "blob", blobId).ConfigureAwait(false);
                return isBundle ? ExeVersionReader.FromInfoPlist(content.StandardOutputBytes) : ExeVersionReader.FromPe(content.StandardOutputBytes);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeHint(string hint) => hint.Replace('\\', '/').Trim('/', ' ');
}
