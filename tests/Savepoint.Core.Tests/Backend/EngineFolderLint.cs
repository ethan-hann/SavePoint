using Savepoint.Core.Api;

namespace Savepoint.Core.Tests.Backend;

// What a job line may never show a player: the storage engine's own folders, by any path or text argument.
internal static class EngineFolderLint
{
    private static readonly string[] _engineNames = ["objects", "refs", "packed-refs", "pack", "info", "logs", "index", "HEAD", "hooks"];

    // A game's own file may be called "logs" or "info", so a file path only counts when it passes through a git directory.
    public static bool NamesEngineFolder(string text, bool isFilePath = false)
    {
        var segments = text.Split('/', '\\');
        return segments.Any(segment => segment.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            || (!isFilePath && segments.Any(segment => _engineNames.Contains(segment, StringComparer.Ordinal)));
    }

    // The job's lines that name an engine folder, each as the text that does.
    public static IReadOnlyList<string> Leaks(JobSnapshot job) =>
    [
        .. job.RecentLines.SelectMany(line => line switch
        {
            JobLine.FilePath file => NamesEngineFolder(file.Path, isFilePath: true) ? [file.Path] : [],
            JobLine.Step step => step.Args.Values.OfType<string>().Where(text => NamesEngineFolder(text)).Select(text => $"{step.MessageId}: {text}"),
            _ => [],
        }),
    ];

    public static void AssertNoEngineFolder(JobSnapshot job) => Assert.Empty(Leaks(job));
}
