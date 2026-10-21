using Savepoint.Core.Platform;

namespace Savepoint.Core.Launch;

/// <summary>Starts one OS process; the only place the launchers touch <see cref="System.Diagnostics.Process"/>.</summary>
internal interface IProcessStarter
{
    /// <summary>Starts the process; throws what the OS call throws (the launcher turns that into a typed error).</summary>
    /// <param name="request">What to start.</param>
    /// <returns>The process, or null when the shell handled the request without giving SavePoint a process.</returns>
    IGameProcess? Start(ProcessStartRequest request);
}
