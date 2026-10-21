namespace Savepoint.Core.Launch;

/// <summary>What a launcher asks the OS to start; the seam tests read instead of starting a real process.</summary>
/// <param name="FileName">The program, or the link when <paramref name="UseShellExecute"/> is set.</param>
/// <param name="Arguments">One list item per argument; never joined into a command line by the launcher.</param>
/// <param name="WorkingDirectory">The folder the process starts in, or null for the caller's.</param>
/// <param name="UseShellExecute">True hands <paramref name="FileName"/> to the shell, which is how Windows opens links.</param>
internal sealed record ProcessStartRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    bool UseShellExecute = false);
