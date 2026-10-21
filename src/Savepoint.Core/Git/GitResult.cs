namespace Savepoint.Core.Git;

public sealed record GitResult(int ExitCode, string StandardOutput, string StandardError, byte[] StandardOutputBytes)
{
    // For -z output: splits the whole decoded text, so a name never breaks across read chunks.
    public IReadOnlyList<string> SplitStandardOutputOnNul() => GitOutput.SplitNul(StandardOutput);
}
