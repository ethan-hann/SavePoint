namespace Savepoint.Core.Git;

// The lock files git leaves in its directory when its process is killed between taking a lock and releasing it.
internal static class StaleLocks
{
    private static readonly string[] _topLevel = ["index.lock", "HEAD.lock", "packed-refs.lock", "config.lock", "shallow.lock"];

    public static string IndexLock(string gitDir) => Path.Combine(gitDir, "index.lock");

    // Removes every lock file and every temporary object file it can; the caller has established that no git process is
    // using the directory. A git killed while it writes a big file leaves a temporary pack or object that can be hundreds of
    // megabytes and that nothing finishes or removes.
    public static int RemoveAll(string gitDir) => RemoveLocks(gitDir) + RemoveTemporaryFiles(gitDir);

    private static int RemoveTemporaryFiles(string gitDir)
    {
        var objects = Path.Combine(gitDir, "objects");
        if (!Directory.Exists(objects))
        {
            return 0;
        }

        var removed = 0;
        var pack = Path.Combine(objects, "pack");
        if (Directory.Exists(pack))
        {
            foreach (var file in Directory.EnumerateFiles(pack, "tmp_*").ToList())
            {
                removed += TryDelete(file) ? 1 : 0;
            }
        }

        foreach (var folder in Directory.EnumerateDirectories(objects).ToList())
        {
            var name = Path.GetFileName(folder);
            if (name.Length != 2 || !name.All(char.IsAsciiHexDigit))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(folder, "tmp_obj_*").ToList())
            {
                removed += TryDelete(file) ? 1 : 0;
            }
        }

        return removed;
    }

    private static int RemoveLocks(string gitDir)
    {
        var removed = 0;
        foreach (var name in _topLevel)
        {
            removed += TryDelete(Path.Combine(gitDir, name)) ? 1 : 0;
        }

        foreach (var prefix in new[] { GitEngine.ClassifyIndexPrefix, GitEngine.AlignIndexPrefix })
        {
            foreach (var file in Directory.EnumerateFiles(gitDir, prefix + "*").ToList())
            {
                removed += TryDelete(file) ? 1 : 0;
            }
        }

        var refs = Path.Combine(gitDir, "refs");
        if (Directory.Exists(refs))
        {
            foreach (var file in Directory.EnumerateFiles(refs, "*.lock", SearchOption.AllDirectories).ToList())
            {
                removed += TryDelete(file) ? 1 : 0;
            }
        }

        return removed;
    }

    private static bool TryDelete(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        // A process that was just killed can hold its lock file for a moment longer on Windows.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                File.Delete(path);
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(50 * attempt);
            }
        }

        return false;
    }
}
