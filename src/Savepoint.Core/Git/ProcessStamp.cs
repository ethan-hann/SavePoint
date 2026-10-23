using System.ComponentModel;
using System.Diagnostics;

namespace Savepoint.Core.Git;

// A process as the system tells it apart from one that later takes its number: its id and the moment it started.
internal readonly record struct ProcessStamp(int Id, long StartedAtUtcTicks)
{
    // The system reads a start time from a clock that can be adjusted, so two reads of one process may differ by a little.
    private static readonly long _toleranceTicks = TimeSpan.FromSeconds(2).Ticks;

    public static ProcessStamp? Of(Process process)
    {
        try
        {
            return new ProcessStamp(process.Id, process.StartTime.ToUniversalTime().Ticks);
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Gone, or not one this account may inspect.
            return null;
        }
    }

    public static ProcessStamp Current()
    {
        using var self = Process.GetCurrentProcess();
        return Of(self) ?? new ProcessStamp(Environment.ProcessId, 0);
    }

    // True while the very process this stamp was taken from is still running; a process that took over its number does not count.
    public bool IsRunning()
    {
        try
        {
            using var process = Process.GetProcessById(Id);
            return !process.HasExited && Of(process) is { } now && Math.Abs(now.StartedAtUtcTicks - StartedAtUtcTicks) <= _toleranceTicks;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    public async Task WaitForExitAsync(CancellationToken ct)
    {
        try
        {
            using var process = Process.GetProcessById(Id);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // Already gone.
        }
    }
}
