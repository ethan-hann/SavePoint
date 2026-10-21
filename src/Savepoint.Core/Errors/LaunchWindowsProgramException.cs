namespace Savepoint.Core.Errors;

/// <summary>The game is a Windows program and this OS cannot start one directly.</summary>
public sealed class LaunchWindowsProgramException : SavepointException
{
    /// <summary>Creates the error for one program.</summary>
    /// <param name="path">The Windows program the game definition names.</param>
    public LaunchWindowsProgramException(string path)
        : base(
            "error.launch.windowsProgram",
            $"'{path}' is a Windows program, which this OS cannot start directly.",
            new Dictionary<string, object?> { ["path"] = path })
    {
    }
}
