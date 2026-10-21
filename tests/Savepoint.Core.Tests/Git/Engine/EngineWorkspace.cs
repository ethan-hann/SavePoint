using System.Text;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Paths;
using Savepoint.Core.Platform;
using Savepoint.Core.Tests.Platform;

namespace Savepoint.Core.Tests.Git.Engine;

// A storage root, a games folder and an engine over the real bundled git, all inside one throwaway folder.
internal sealed class EngineWorkspace : IDisposable
{
    private readonly object _gate = new();
    private readonly List<GitInvocation> _invocations = [];
    private int _folders;

    public EngineWorkspace(Func<GitEngineOptions, GitEngineOptions>? configure = null)
    {
        Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "savepoint-engine-" + Guid.NewGuid().ToString("N")));
        StorageRoot = Path.Combine(Root, "storage");
        GamesRoot = Path.Combine(Root, "games");
        Directory.CreateDirectory(StorageRoot);
        Directory.CreateDirectory(GamesRoot);

        Platform = FakePlatforms.ForHost(Path.Combine(Root, "home"));
        PathSafety = CreatePathSafety(Platform, StorageRoot);
        Binary = GitBinary.Locate(Path.Combine(Root, "gitconfig"));
        Runner = new GitRunner(Binary);
        Space = new FakeFreeSpace();

        var options = new GitEngineOptions { InvocationObserver = Observe };
        Engine = new GitEngine(Runner, PathSafety, Space.Probe, configure is null ? options : configure(options));
    }

    public string Root { get; }

    public string StorageRoot { get; }

    public string GamesRoot { get; }

    public IPlatform Platform { get; }

    public PathSafety PathSafety { get; }

    public GitBinary Binary { get; }

    public GitRunner Runner { get; }

    public FakeFreeSpace Space { get; }

    public GitEngine Engine { get; }

    // Runs before each git call the engine makes, after it has been recorded.
    public Action<GitInvocation>? OnInvoke { get; set; }

    public IReadOnlyList<GitInvocation> Invocations
    {
        get
        {
            lock (_gate)
            {
                return [.. _invocations];
            }
        }
    }

    // The one place that builds the path judge, so a change to its constructor touches a single line.
    public static PathSafety CreatePathSafety(IPlatform platform, string storageRoot) => new(platform, storageRoot);

    // A repo reference whose work tree exists and is empty; the git directory is not created.
    public RepoRef NewRepo(string gameId = "game-1", RepoRole role = RepoRole.Install)
    {
        var folder = Interlocked.Increment(ref _folders);
        var workTree = Path.Combine(GamesRoot, $"{gameId}-{role.ToString().ToLowerInvariant()}-{folder}");
        Directory.CreateDirectory(workTree);
        return new RepoRef(Path.Combine(StorageRoot, gameId, $"{role.ToString().ToLowerInvariant()}-{folder}.git"), workTree, gameId, role);
    }

    // A second repo reference for the same work tree, as another game or role would have.
    public static RepoRef Claim(RepoRef repo, string gameId, RepoRole role) =>
        new(repo.GitDir + "-" + gameId, repo.WorkTree, gameId, role);

    public async Task<RepoRef> InitializedRepoAsync(string gameId = "game-1", RepoRole role = RepoRole.Install)
    {
        var repo = NewRepo(gameId, role);
        await Engine.InitAsync(repo, ct: TestContext.Current.CancellationToken);
        RequireGitDir(repo);
        return repo;
    }

    public string FileOf(RepoRef repo, string relativePath) => Path.Combine(repo.WorkTree, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public string Write(RepoRef repo, string relativePath, string content)
    {
        var path = FileOf(repo, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    public string Write(RepoRef repo, string relativePath, byte[] content)
    {
        var path = FileOf(repo, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    public void Delete(RepoRef repo, string relativePath) => File.Delete(FileOf(repo, relativePath));

    public static string MarkerPath(RepoRef repo) => Path.Combine(repo.WorkTree, ConsentMarker.FileName);

    // The valid marker in a folder, or null for a missing or unreadable one.
    public static (string GameId, RepoRole Role, DateTimeOffset ConfirmedAt)? ReadMarker(RepoRef repo) =>
        ConsentMarker.Read(repo.WorkTree) is ConsentMarkerState.Valid valid
            ? (valid.Marker.GameId, valid.Marker.Role, valid.Marker.ConfirmedAt)
            : null;

    // Fails the test (rather than throwing from inside a helper) when the folder holds no valid marker.
    public static (string GameId, RepoRole Role, DateTimeOffset ConfirmedAt) RequireMarker(RepoRef repo)
    {
        var marker = ReadMarker(repo);
        if (marker is null)
        {
            Assert.Fail($"expected a valid consent marker in {repo.WorkTree}");
        }

        return marker.Value;
    }

    // Reads a file from the git directory, failing the test by assertion when the engine never made it.
    public static string GitDirText(RepoRef repo, string relativePath)
    {
        var path = Path.Combine(repo.GitDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"expected the engine to have written {path}");
        return File.ReadAllText(path);
    }

    public static string[] GitDirLines(RepoRef repo, string relativePath) =>
        GitDirText(repo, relativePath).Split('\n', StringSplitOptions.TrimEntries);

    public static void WriteMarker(RepoRef repo, string gameId, RepoRole role, DateTimeOffset confirmedAt) =>
        ConsentMarker.Write(repo.WorkTree, gameId, role, confirmedAt);

    public static void CorruptMarker(RepoRef repo) => File.WriteAllText(MarkerPath(repo), "{ not a marker");

    // Runs git directly against the repo, bypassing the engine, so a test can inspect what the engine left behind.
    public Task<GitResult> GitAsync(RepoRef repo, params string[] arguments)
    {
        RequireGitDir(repo);
        return Runner.RunAsync(new GitInvocation(arguments) { GitDir = repo.GitDir, WorkTree = repo.WorkTree, ReadOnly = true }, TestContext.Current.CancellationToken);
    }

    // For commands that change the repository, such as putting it in a state a test needs.
    public Task<GitResult> GitWriteAsync(RepoRef repo, params string[] arguments)
    {
        RequireGitDir(repo);
        return Runner.RunAsync(new GitInvocation(arguments) { GitDir = repo.GitDir, WorkTree = repo.WorkTree }, TestContext.Current.CancellationToken);
    }

    public async Task<string> GitOutAsync(RepoRef repo, params string[] arguments) =>
        (await GitAsync(repo, arguments)).StandardOutput.TrimEnd('\n', '\r');

    public Task<CaptureOutcome> CaptureAsync(
        RepoRef repo,
        string branch = "profile-1",
        string message = "A point",
        IReadOnlyList<string>? ignore = null,
        IProgress<FileProgress>? progress = null) =>
        Engine.CaptureAsync(
            repo,
            new CaptureRequest(branch, message, Guid.NewGuid().ToString()) { IgnorePatterns = ignore ?? [] },
            progress,
            TestContext.Current.CancellationToken);

    // The full ref HEAD points at, or null when HEAD is detached.
    public async Task<string?> HeadRefAsync(RepoRef repo)
    {
        RequireGitDir(repo);
        var result = await Runner.RunAsync(
            new GitInvocation(["symbolic-ref", "-q", "HEAD"]) { GitDir = repo.GitDir, WorkTree = repo.WorkTree, ReadOnly = true, AllowedExitCodes = [0, 1] },
            TestContext.Current.CancellationToken);
        return result.ExitCode == 0 ? result.StandardOutput.Trim() : null;
    }

    // The commit a revision resolves to, or null when it does not resolve.
    public async Task<string?> ResolveAsync(RepoRef repo, string revision)
    {
        RequireGitDir(repo);
        var result = await Runner.RunAsync(
            new GitInvocation(["rev-parse", "--verify", "-q", revision + "^{commit}"]) { GitDir = repo.GitDir, WorkTree = repo.WorkTree, ReadOnly = true, AllowedExitCodes = [0, 1] },
            TestContext.Current.CancellationToken);
        return result.ExitCode == 0 ? result.StandardOutput.Trim() : null;
    }

    public Task<string?> TipAsync(RepoRef repo, string branch) => ResolveAsync(repo, "refs/heads/" + branch);

    // Fails the test by assertion, rather than with a git error, when a revision a test depends on does not exist.
    public async Task RequireRevisionAsync(RepoRef repo, string revision) =>
        Assert.True(await ResolveAsync(repo, revision) is not null, $"expected the revision '{revision}' to exist");

    // Paths recorded in a revision's tree.
    public async Task<string[]> TreeAsync(RepoRef repo, string revision)
    {
        await RequireRevisionAsync(repo, revision);
        return [.. (await GitAsync(repo, "ls-tree", "-r", "-z", "--name-only", revision)).SplitStandardOutputOnNul()];
    }

    public async Task<int> CountAsync(RepoRef repo, string revision)
    {
        await RequireRevisionAsync(repo, revision);
        return int.Parse(await GitOutAsync(repo, "rev-list", "--count", revision), System.Globalization.CultureInfo.InvariantCulture);
    }

    // One field of a revision's commit, as git's --format prints it.
    public async Task<string> LogAsync(RepoRef repo, string revision, string format)
    {
        await RequireRevisionAsync(repo, revision);
        return await GitOutAsync(repo, "log", "-1", "--format=" + format, revision, "--");
    }

    // Many small files, written in parallel, named {folder}/{prefix}{n}.txt.
    public void WriteMany(RepoRef repo, string folder, int count, string prefix = "f", string content = "content")
    {
        Directory.CreateDirectory(FileOf(repo, folder));
        Parallel.For(0, count, i => File.WriteAllText(FileOf(repo, $"{folder}/{prefix}{i}.txt"), $"{content} {i}"));
    }

    // Null when the key is not set.
    public async Task<string?> ConfigAsync(RepoRef repo, string key)
    {
        RequireGitDir(repo);
        var result = await Runner.RunAsync(
            new GitInvocation(["config", "--local", "--get", key]) { GitDir = repo.GitDir, WorkTree = repo.WorkTree, ReadOnly = true, AllowedExitCodes = [0, 1] },
            TestContext.Current.CancellationToken);
        return result.ExitCode == 0 ? result.StandardOutput.Trim() : null;
    }

    public void Dispose() => GitWorkspace.DeleteDirectory(Root);

    private static void RequireGitDir(RepoRef repo) =>
        Assert.True(File.Exists(Path.Combine(repo.GitDir, "HEAD")), $"expected a repository at {repo.GitDir}");

    private void Observe(GitInvocation invocation)
    {
        lock (_gate)
        {
            _invocations.Add(invocation);
        }

        OnInvoke?.Invoke(invocation);
    }
}

// Answers free-space questions from a table so a test can make a volume look full.
internal sealed class FakeFreeSpace
{
    private readonly object _gate = new();
    private readonly List<string> _queries = [];

    public long Free { get; set; } = long.MaxValue / 4;

    // Overrides Free for paths it returns a value for.
    public Func<string, long?>? PerPath { get; set; }

    public IReadOnlyList<string> Queries
    {
        get
        {
            lock (_gate)
            {
                return [.. _queries];
            }
        }
    }

    public long Probe(string existingDirectory)
    {
        lock (_gate)
        {
            _queries.Add(existingDirectory);
        }

        return PerPath?.Invoke(existingDirectory) ?? Free;
    }
}
