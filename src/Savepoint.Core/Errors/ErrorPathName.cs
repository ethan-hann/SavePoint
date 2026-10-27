namespace Savepoint.Core.Errors;

/// <summary>Derives the short name an error carries from the path it was raised for.</summary>
internal static class ErrorPathName
{
    /// <summary>
    /// The last segment of <paramref name="path"/>, splitting on both separators: a Windows path in an error's arguments
    /// still names its file when the error is built or read on Linux or macOS, where a backslash is an ordinary character.
    /// </summary>
    internal static string LastSegment(string path) => path[(path.LastIndexOfAny(['/', '\\']) + 1)..];
}
