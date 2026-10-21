using System.Diagnostics;

namespace Savepoint.Core.Platform.MacOS;

public sealed class MacOSProcessInspector : IProcessInspector
{
    public IReadOnlyList<RunningProcess> List()
    {
        var result = new List<RunningProcess>();
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            return result;
        }

        // Once the runtime says it cannot report paths here, stop asking for every process.
        var pathsSupported = true;
        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    var path = pathsSupported ? ReadPath(process, ref pathsSupported) : null;
                    result.Add(new RunningProcess(process.Id, process.ProcessName, path));
                }
                catch (Exception ex) when (IsReadFailure(ex))
                {
                    // Exited between listing and reading, or off limits: skip it.
                }
            }
        }

        return result;
    }

    private static bool IsReadFailure(Exception ex) =>
        ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException or UnauthorizedAccessException;

    private static string? ReadPath(Process process, ref bool pathsSupported)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (PlatformNotSupportedException)
        {
            pathsSupported = false;
            return null;
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            return null;
        }
    }
}
