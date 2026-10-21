namespace Savepoint.Core.Git;

// Internal: the engine maps it to a typed error, so nothing outside Core sees it.
internal sealed class GitCommandException : Exception
{
    private const int _maxMessageLength = 2000;

    public GitCommandException(IReadOnlyList<string> arguments, int exitCode, string standardError)
        : base(BuildMessage(arguments, exitCode, standardError))
    {
        Arguments = arguments;
        ExitCode = exitCode;
        StandardError = standardError;
    }

    public IReadOnlyList<string> Arguments { get; }

    public int ExitCode { get; }

    public string StandardError { get; }

    private static string BuildMessage(IReadOnlyList<string> arguments, int exitCode, string standardError)
    {
        var message = $"git {string.Join(' ', arguments)} exited with code {exitCode}: {standardError.Trim()}";
        return message.Length <= _maxMessageLength ? message : message[.._maxMessageLength] + "...";
    }
}
