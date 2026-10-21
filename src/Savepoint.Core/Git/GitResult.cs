namespace Savepoint.Core.Git;

/// <summary>What a finished git process printed and how it ended.</summary>
/// <param name="ExitCode">The process's exit code; it is always one of <see cref="GitInvocation.AllowedExitCodes"/>, because the runner fails the call for any other.</param>
/// <param name="StandardOutput">Standard output decoded as UTF-8; empty when the invocation asked for binary output or a line callback.</param>
/// <param name="StandardError">Standard error decoded as UTF-8. Only its end is kept when it is very long (at least the last megabyte), since the end is what explains a failure.</param>
/// <param name="StandardOutputBytes">Standard output as raw bytes when the invocation asked for <see cref="GitInvocation.BinaryStdout"/>; empty otherwise.</param>
public sealed record GitResult(int ExitCode, string StandardOutput, string StandardError, byte[] StandardOutputBytes)
{
    /// <summary>Splits <see cref="StandardOutput"/> (from a <c>-z</c> command) into its entries; see <see cref="GitOutput.SplitNul"/>.</summary>
    /// <returns>The entries, without their NUL terminators.</returns>
    public IReadOnlyList<string> SplitStandardOutputOnNul() => GitOutput.SplitNul(StandardOutput);
}
