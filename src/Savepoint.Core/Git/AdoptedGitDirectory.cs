namespace Savepoint.Core.Git;

/// <summary>
/// Makes a git directory that came from somewhere else safe to use: git reads a repository's own config and hooks folder, and either
/// can name a program to run. Every git process SavePoint starts is already told to use no hooks and no file system monitor, so this
/// is the second guard: what a foreign repository carries is removed before anything reads it.
/// </summary>
internal static class AdoptedGitDirectory
{
    /// <summary>Rewrites the config to the settings git init writes plus SavePoint's own, whatever stamp it carries, and empties the hooks folder.</summary>
    /// <param name="gitDir">A git directory SavePoint is about to start using.</param>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">The config file cannot be read, so it was left as it is.</exception>
    public static void MakeSafe(string gitDir)
    {
        RepoConfigFile.Apply(gitDir, OperatingSystem.IsWindows());
        EmptyHooks(Path.Combine(gitDir, "hooks"));
    }

    // Best effort per entry: the environment keeps hooks from running whatever is left here.
    private static void EmptyHooks(string hooks)
    {
        if (!Directory.Exists(hooks))
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(hooks).ToList())
        {
            try
            {
                if (Directory.Exists(entry) && !new DirectoryInfo(entry).Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    Directory.Delete(entry, recursive: true);
                }
                else
                {
                    File.Delete(entry);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A hook that cannot be removed is also one that cannot run: the folder git is told to use for hooks is another.
            }
        }
    }
}
