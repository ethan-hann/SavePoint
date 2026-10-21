namespace Savepoint.Core.Git;

// The rules of git check-ref-format for a name under refs/heads, checked here so a bad name fails before any file is hashed.
internal static class BranchNames
{
    private const int _maxLength = 200;

    public static bool IsValid(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > _maxLength || name == "@" || name == "HEAD")
        {
            return false;
        }

        if (name[0] is '-' or '/' || name[^1] is '/' or '.' || name.Contains("..", StringComparison.Ordinal)
            || name.Contains("@{", StringComparison.Ordinal) || name.Contains("//", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var c in name)
        {
            if (c < ' ' || c == '\x7f' || c is ' ' or '~' or '^' or ':' or '?' or '*' or '[' or '\\')
            {
                return false;
            }
        }

        return name.Split('/').All(part => part.Length > 0 && part[0] != '.' && !part.EndsWith(".lock", StringComparison.Ordinal));
    }

    public static void Require(string? name)
    {
        if (!IsValid(name))
        {
            throw new ArgumentException($"'{name}' is not a valid branch name.", nameof(name));
        }
    }
}
