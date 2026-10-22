using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git;

// A throwaway folder with a split git dir and work tree, driven through the real bundled git.
internal sealed class GitWorkspace : IDisposable
{
    public GitWorkspace(GitRunnerOptions? options = null)
    {
        Root = Path.Combine(Path.GetTempPath(), "savepoint-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(WorkTree);

        // git init makes the git dir itself but not the folder above it.
        Directory.CreateDirectory(Path.GetDirectoryName(GitDir)!);
        Binary = GitBinary.Locate(ConfigDir);
        Runner = new GitRunner(Binary, options);
    }

    public string Root { get; }

    public string ConfigDir => Path.Combine(Root, "config");

    public string GitDir => Path.Combine(Root, "store", "repo.git");

    public string WorkTree => Path.Combine(Root, "work");

    public GitBinary Binary { get; }

    public GitRunner Runner { get; }

    // Where GitBinary.Locate looks when nothing overrides it.
    public static string ExpectedBundleRoot =>
        Environment.GetEnvironmentVariable(GitBinary.RootEnvironmentVariable) is { Length: > 0 } overridden
            ? overridden
            : Path.Combine(AppContext.BaseDirectory, "git");

    public Task InitAsync() => RunAsync("init");

    public Task<GitResult> RunAsync(params string[] arguments) =>
        Runner.RunAsync(new GitInvocation(arguments) { GitDir = GitDir, WorkTree = WorkTree }, TestContext.Current.CancellationToken);

    public Task<GitResult> RunAsync(GitInvocation invocation) =>
        Runner.RunAsync(invocation with { GitDir = GitDir, WorkTree = WorkTree }, TestContext.Current.CancellationToken);

    // For commands that need no repository.
    public Task<GitResult> RunPlainAsync(params string[] arguments) => RunPlainAsync(new GitInvocation(arguments));

    public Task<GitResult> RunPlainAsync(GitInvocation invocation) =>
        Runner.RunAsync(OutsideAnyRepository(invocation), TestContext.Current.CancellationToken);

    // The temp folder sits inside the checkout, whose .git can be a worktree file git cannot follow; the ceiling stops discovery at the temp folder.
    public GitInvocation OutsideAnyRepository(GitInvocation invocation)
    {
        var environment = new Dictionary<string, string?>(invocation.ExtraEnvironment ?? new Dictionary<string, string?>())
        {
            ["GIT_CEILING_DIRECTORIES"] = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())),
        };
        return invocation with { WorkingDirectory = invocation.WorkingDirectory ?? WorkTree, ExtraEnvironment = environment };
    }

    public string Write(string relativePath, string content)
    {
        var path = Path.Combine(WorkTree, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose() => DeleteDirectory(Root);

    internal static void DeleteDirectory(string path)
    {
        for (var attempt = 0; attempt < 5 && Directory.Exists(path); attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                // Pack files are read-only, and a just-killed process may still hold a handle for a moment. Git may also remove a
                // temporary pack between the listing and the attribute change, which leaves nothing to clear.
                try
                {
                    foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            File.SetAttributes(file, FileAttributes.Normal);
                        }
                        catch (IOException)
                        {
                        }
                    }
                }
                catch (IOException)
                {
                }

                Thread.Sleep(100);
            }
        }
    }
}
