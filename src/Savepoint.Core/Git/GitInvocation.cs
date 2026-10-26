using System.Text;

namespace Savepoint.Core.Git;

/// <summary>
/// One git command for <see cref="GitRunner"/>: its arguments, where it runs, what it is fed and how its output is
/// delivered. Arguments are always passed as separate strings, never joined into a command line.
/// </summary>
public sealed record GitInvocation
{
    /// <summary>Builds an invocation from git's arguments (everything after <c>git</c>).</summary>
    /// <param name="arguments">The arguments, one string each; they are copied.</param>
    public GitInvocation(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        Arguments = arguments.ToArray();
    }

    /// <summary>The arguments, passed through <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/> and never joined into a command line.</summary>
    public IReadOnlyList<string> Arguments { get; init; }

    /// <summary>The repository's git directory, handed to git as <c>GIT_DIR</c>; null leaves git without a repository.</summary>
    public string? GitDir { get; init; }

    /// <summary>The work tree, handed to git as <c>GIT_WORK_TREE</c>; it needs <see cref="GitDir"/>.</summary>
    public string? WorkTree { get; init; }

    /// <summary>The folder git runs in; defaults to <see cref="WorkTree"/>, then to the current directory. It must exist.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>True for a command that only reads: <c>GIT_OPTIONAL_LOCKS=0</c> stops it taking <c>index.lock</c>, so it cannot get in the way of a write.</summary>
    public bool ReadOnly { get; init; }

    /// <summary>Text written to git's standard input (UTF-8) and then closed. At most one of the three stdin forms may be set.</summary>
    public string? Stdin { get; init; }

    /// <summary>Bytes written to git's standard input and then closed. At most one of the three stdin forms may be set.</summary>
    public byte[]? StdinBytes { get; init; }

    /// <summary>A stream copied to git's standard input until it ends; the caller keeps ownership of it. At most one of the three stdin forms may be set.</summary>
    public Stream? StdinStream { get; init; }

    /// <summary>
    /// Called for each line of standard output (lines end at <c>\n</c>, <c>\r</c> or <c>\r\n</c>) on a thread-pool thread.
    /// A call waits for a callback that is running, and an exception from it stops git and is rethrown by the call.
    /// <see cref="GitResult.StandardOutput"/> stays empty when this is set.
    /// </summary>
    public Action<string>? OnStdoutLine { get; init; }

    /// <summary>Called for each line of standard error, with the same line rules and failure behavior as <see cref="OnStdoutLine"/>; the text is still collected in the result.</summary>
    public Action<string>? OnStderrLine { get; init; }

    /// <summary>True to collect standard output as raw bytes (<see cref="GitResult.StandardOutputBytes"/>) instead of text; it excludes <see cref="OnStdoutLine"/>.</summary>
    public bool BinaryStdout { get; init; }

    /// <summary>Environment changes applied after the runner's base environment; a null value removes the variable.</summary>
    public IReadOnlyDictionary<string, string?>? ExtraEnvironment { get; init; }

    /// <summary>The exit codes that count as success; the call fails for any other. Defaults to 0 only.</summary>
    public IReadOnlyCollection<int> AllowedExitCodes { get; init; } = [0];

    // Called with the new process as soon as it has started, before anything is read from it. A reclaim notes it so a later start can find it.
    internal Action<ProcessStamp>? Started { get; init; }

    // A git that is left to finish when SavePoint is stopped, because a note of its own makes the next start wait for it instead of ending it (a reclaim's).
    // It is kept out of the job that ends the others with SavePoint and out of the note of running git.
    internal bool SurvivesExit { get; init; }

    /// <summary>
    /// Returns a copy that reads its paths from standard input as a NUL-separated list (<c>--pathspec-from-file=-</c>) and
    /// takes them literally (<c>GIT_LITERAL_PATHSPECS=1</c>), which keeps a long list off the command line (Windows allows
    /// about 32,000 characters) and stops a name like <c>[draft].esp</c> from being a pattern. The command must accept
    /// <c>--pathspec-from-file</c>.
    /// </summary>
    /// <param name="paths">The paths; at least one, none containing NUL. An empty list is refused because git would read "no paths" as "every path".</param>
    /// <returns>The invocation with the list on stdin.</returns>
    /// <exception cref="InvalidOperationException">The invocation already has a stdin.</exception>
    /// <exception cref="ArgumentException">The list is empty or a path contains NUL.</exception>
    public GitInvocation WithPathspecs(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (Stdin is not null || StdinBytes is not null || StdinStream is not null)
        {
            throw new InvalidOperationException("A path list is sent on stdin, which this invocation already uses.");
        }

        var list = new StringBuilder();
        var count = 0;
        foreach (var path in paths)
        {
            if (path.Contains('\0'))
            {
                throw new ArgumentException("A path cannot contain a NUL character.", nameof(paths));
            }

            list.Append(path).Append('\0');
            count++;
        }

        // No pathspec means every path to git, the opposite of listing nothing.
        if (count == 0)
        {
            throw new ArgumentException("The path list is empty.", nameof(paths));
        }

        var environment = new Dictionary<string, string?>(ExtraEnvironment ?? new Dictionary<string, string?>())
        {
            // Without this a name like [draft].esp is a pattern and matches other files.
            ["GIT_LITERAL_PATHSPECS"] = "1",
        };

        return this with
        {
            Arguments = [.. Arguments, "--pathspec-from-file=-", "--pathspec-file-nul"],
            Stdin = list.ToString(),
            ExtraEnvironment = environment,
        };
    }
}
