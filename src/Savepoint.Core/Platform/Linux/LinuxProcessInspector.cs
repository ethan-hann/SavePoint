using System.Globalization;
using System.Text;

namespace Savepoint.Core.Platform.Linux;

/// <summary>Reads /proc: comm (the kernel's name), the exe link, and argv0 from cmdline.</summary>
public sealed class LinuxProcessInspector : IProcessInspector
{
    private const string _deletedSuffix = " (deleted)";

    private readonly string _procRoot;

    /// <param name="procRoot">Injectable so the parsing can be tested against a made-up /proc on any OS.</param>
    public LinuxProcessInspector(string procRoot = "/proc")
    {
        _procRoot = procRoot;
    }

    /// <inheritdoc />
    public IReadOnlyList<RunningProcess> List()
    {
        var result = new List<RunningProcess>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(_procRoot))
            {
                if (int.TryParse(Path.GetFileName(dir), NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
                    && TryRead(pid, dir) is { } process)
                {
                    result.Add(process);
                }
            }
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            // /proc itself went away or cannot be listed: report what was read.
        }

        return result;
    }

    private static RunningProcess? TryRead(int pid, string dir)
    {
        try
        {
            // A process that exits mid-read has no comm any more; skip it.
            var name = File.ReadAllText(Path.Combine(dir, "comm")).TrimEnd('\n', '\r');
            return new RunningProcess(pid, name, ReadExecutable(dir), ReadFirstArgument(dir));
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            return null;
        }
    }

    private static string? ReadExecutable(string dir)
    {
        try
        {
            var target = new FileInfo(Path.Combine(dir, "exe")).LinkTarget;
            return target is not null && target.EndsWith(_deletedSuffix, StringComparison.Ordinal)
                ? target[..^_deletedSuffix.Length]
                : target;
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            return null;
        }
    }

    private static string? ReadFirstArgument(string dir)
    {
        try
        {
            var bytes = File.ReadAllBytes(Path.Combine(dir, "cmdline"));
            var length = Array.IndexOf(bytes, (byte)0);
            var argv0 = Encoding.UTF8.GetString(bytes, 0, length < 0 ? bytes.Length : length);
            return argv0.Length == 0 ? null : argv0;
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            return null;
        }
    }

    private static bool IsReadFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
