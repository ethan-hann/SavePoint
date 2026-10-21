using System.Collections.Concurrent;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A repository state that costs dozens of git processes to build is built once per test run and copied for each test that
// needs it. Nothing in a repository or in the marker holds an absolute path, so a copy works wherever it is put.
internal static class RepoTemplates
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<Template>>> _built = new();
    private static readonly object _gate = new();
    private static readonly List<EngineWorkspace> _workspaces = [];

    static RepoTemplates()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DisposeAll();
    }

    public static async Task<(RepoRef Repo, T Data)> CopyAsync<T>(string name, EngineWorkspace into, Func<EngineWorkspace, Task<(RepoRef Repo, T Data)>> build)
    {
        var template = await _built.GetOrAdd(name, _ => new Lazy<Task<Template>>(() => BuildAsync(build))).Value;
        var copy = into.NewRepo();
        CopyFolder(template.Repo.GitDir, copy.GitDir);
        CopyFolder(template.Repo.WorkTree, copy.WorkTree);
        return (copy, (T)template.Data!);
    }

    private static async Task<Template> BuildAsync<T>(Func<EngineWorkspace, Task<(RepoRef Repo, T Data)>> build)
    {
        var workspace = new EngineWorkspace();
        lock (_gate)
        {
            _workspaces.Add(workspace);
        }

        var (repo, data) = await build(workspace);
        return new Template(repo, data);
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
        }
    }

    private static void DisposeAll()
    {
        lock (_gate)
        {
            foreach (var workspace in _workspaces)
            {
                workspace.Dispose();
            }

            _workspaces.Clear();
        }
    }

    private sealed record Template(RepoRef Repo, object? Data);
}
