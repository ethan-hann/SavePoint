using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Services;

/// <summary>Runs a git command against a git directory alone, for the few questions the engine has no call for.</summary>
internal static class RepoGit
{
    /// <summary>
    /// Runs git with the repository's git directory and no work tree. A failed command is reported as the same typed error the
    /// engine uses, so nothing outside Core ever sees git's own exception.
    /// </summary>
    /// <param name="runner">The runner for the bundled git.</param>
    /// <param name="gitDir">The git directory.</param>
    /// <param name="readOnly">True for a command that only reads, so it never takes a lock a capture needs.</param>
    /// <param name="cancellationToken">Stops git and its children.</param>
    /// <param name="stdin">Text for git's standard input, or null.</param>
    /// <param name="binary">True to read standard output as raw bytes.</param>
    /// <param name="arguments">The command line, one list item per argument.</param>
    public static async Task<GitResult> RunAsync(
        GitRunner runner,
        string gitDir,
        bool readOnly,
        CancellationToken cancellationToken,
        string? stdin = null,
        bool binary = false,
        params string[] arguments)
    {
        try
        {
            return await runner.RunAsync(
                new GitInvocation(arguments) { GitDir = gitDir, WorkingDirectory = gitDir, ReadOnly = readOnly, Stdin = stdin, BinaryStdout = binary },
                cancellationToken).ConfigureAwait(false);
        }
        catch (GitCommandException e)
        {
            throw new GitOperationException(null, e.Message);
        }
    }
}
