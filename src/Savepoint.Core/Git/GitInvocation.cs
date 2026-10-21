using System.Text;

namespace Savepoint.Core.Git;

public sealed record GitInvocation
{
    public GitInvocation(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        Arguments = arguments.ToArray();
    }

    // Always passed through ProcessStartInfo.ArgumentList, never joined into a command line.
    public IReadOnlyList<string> Arguments { get; init; }

    public string? GitDir { get; init; }

    public string? WorkTree { get; init; }

    // Defaults to WorkTree, then to the current directory.
    public string? WorkingDirectory { get; init; }

    // Adds GIT_OPTIONAL_LOCKS=0 so a read never takes index.lock.
    public bool ReadOnly { get; init; }

    // At most one of the three stdin forms; each is written in full and then closed.
    public string? Stdin { get; init; }

    public byte[]? StdinBytes { get; init; }

    // Copied to stdin until it ends; the caller keeps ownership of the stream.
    public Stream? StdinStream { get; init; }

    // Lines end at \n, \r or \r\n and arrive on a thread-pool thread; a call waits for a running callback, and GitResult.StandardOutput stays empty.
    public Action<string>? OnStdoutLine { get; init; }

    public Action<string>? OnStderrLine { get; init; }

    public bool BinaryStdout { get; init; }

    // Applied after the base environment; a null value removes the variable.
    public IReadOnlyDictionary<string, string?>? ExtraEnvironment { get; init; }

    public IReadOnlyCollection<int> AllowedExitCodes { get; init; } = [0];

    // Keeps a long path list off the command line (Windows allows about 32k characters); the command must take --pathspec-from-file.
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
