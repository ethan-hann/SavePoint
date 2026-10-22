using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

internal readonly record struct HeadState(string? Branch, string? DetachedCommit)
{
    public bool IsDetached => Branch is null;
}

// Reads the few git files whose format is fixed and simple, which saves a git process per question.
internal static class GitDirFiles
{
    public const string EmptyTree = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";
    public const string BranchPrefix = "refs/heads/";

    private const string _symbolicPrefix = "ref: ";

    public static HeadState ReadHead(string gitDir)
    {
        var text = AtomicText.ReadOrNull(Path.Combine(gitDir, "HEAD"))?.Trim()
            ?? throw new GitOperationException($"'{gitDir}' has no HEAD; the repository does not exist or was not created by SavePoint.");

        if (text.StartsWith(_symbolicPrefix, StringComparison.Ordinal))
        {
            var target = text[_symbolicPrefix.Length..].Trim();
            return target.StartsWith(BranchPrefix, StringComparison.Ordinal)
                ? new HeadState(target[BranchPrefix.Length..], null)
                : throw new GitOperationException($"HEAD points at '{target}', which is not a branch.");
        }

        return IsObjectId(text)
            ? new HeadState(null, text)
            : throw new GitOperationException($"HEAD holds '{text}', which is neither a branch nor a commit.");
    }

    // The commit a loose ref file holds; null when the ref is packed, missing or unreadable (the caller then asks git).
    public static string? ReadLooseRef(string gitDir, string fullRef)
    {
        var text = AtomicText.ReadOrNull(Path.Combine(gitDir, fullRef.Replace('/', Path.DirectorySeparatorChar)))?.Trim();
        return text is not null && IsObjectId(text) ? text : null;
    }

    public static bool IsObjectId(string text) =>
        (text.Length == 40 || text.Length == 64) && text.All(char.IsAsciiHexDigitLower);
}
