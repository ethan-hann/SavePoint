using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Savepoint.Core.Platform.Windows;

public sealed class WindowsProcessInspector : IProcessInspector
{
    private const uint _processQueryLimitedInformation = 0x1000;

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

        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    result.Add(new RunningProcess(process.Id, process.ProcessName, ReadImagePath(process.Id)));
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

    // The limited-information right is granted for most processes, where MainModule needs far more and is slow.
    private static string? ReadImagePath(int pid)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var handle = OpenProcess(_processQueryLimitedInformation, false, (uint)pid);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = new char[1024];
            var size = (uint)buffer.Length;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, char[] exeName, ref uint size);
}
