using System.Globalization;
using System.Text;

namespace Savepoint.Core.Platform.Linux;

/// <summary>Reads /proc: comm (the kernel's name), the exe link, and argv0 from cmdline.</summary>
public sealed class LinuxProcessInspector : IProcessInspector
{
    private const string _deletedSuffix = " (deleted)";

    // The kernel counts a process's start in clock ticks after boot; the tick rate is 100 a second on every Linux that matters.
    private const long _ticksPerSecond = 100;

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
            var boot = ReadBootTime();
            foreach (var dir in Directory.EnumerateDirectories(_procRoot))
            {
                if (int.TryParse(Path.GetFileName(dir), NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
                    && TryRead(pid, dir, boot) is { } process)
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

    // The boot time anchors start times to the clock. Without it they still order processes against each other, which is all that matters.
    private DateTimeOffset ReadBootTime()
    {
        try
        {
            foreach (var line in File.ReadLines(Path.Combine(_procRoot, "stat")))
            {
                if (line.StartsWith("btime ", StringComparison.Ordinal)
                    && long.TryParse(line.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
                {
                    return DateTimeOffset.FromUnixTimeSeconds(seconds);
                }
            }
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            // Fall through to the epoch.
        }

        return DateTimeOffset.UnixEpoch;
    }

    /// <summary>Reads the parent id (field 4) and the start in ticks after boot (field 22) from a <c>/proc/&lt;pid&gt;/stat</c> line.</summary>
    /// <param name="statLine">The stat file's text; the fields follow the process name, which is in brackets and may hold brackets itself.</param>
    /// <returns>Both values, or nulls for a line that is too short or not numeric.</returns>
    internal static (int? ParentPid, long? StartTicks) ParseParentAndStart(string statLine)
    {
        var close = statLine.LastIndexOf(')');
        if (close < 0)
        {
            return (null, null);
        }

        // After the bracket: state is field 3, so the parent is the second item and the start the twentieth.
        var fields = statLine[(close + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int? parent = fields.Length > 1 && int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ppid) ? ppid : null;
        long? start = fields.Length > 19 && long.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ? ticks : null;
        return (parent, start);
    }

    private static RunningProcess? TryRead(int pid, string dir, DateTimeOffset boot)
    {
        try
        {
            // A process that exits mid-read has no comm any more; skip it.
            var name = File.ReadAllText(Path.Combine(dir, "comm")).TrimEnd('\n', '\r');
            var stat = ReadStat(dir);
            if (stat is not null && IsExitedState(stat))
            {
                return null;
            }

            var (parent, ticks) = stat is null ? (null, null) : ParseParentAndStart(stat);
            DateTimeOffset? started = ticks is { } t ? boot.AddTicks(t * (TimeSpan.TicksPerSecond / _ticksPerSecond)) : null;
            return new RunningProcess(pid, name, ReadExecutable(dir), ReadFirstArgument(dir), parent, started);
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// True when a <c>/proc/&lt;pid&gt;/stat</c> line says the process has exited: a zombie (Z) waits for its parent to collect it,
    /// and a dead one (X) is about to vanish. Both still have a folder in /proc but run nothing.
    /// </summary>
    /// <param name="statLine">The stat file's text; the state letter follows the process name, which is in brackets and may hold brackets itself.</param>
    internal static bool IsExitedState(string statLine)
    {
        var close = statLine.LastIndexOf(')');
        var state = close >= 0 ? statLine.AsSpan(close + 1).TrimStart() : default;
        return !state.IsEmpty && state[0] is 'Z' or 'X' or 'x';
    }

    /// <summary>Whether the stat of one process says it has exited; a process with no readable stat counts as not exited.</summary>
    /// <param name="pid">The process id.</param>
    /// <param name="procRoot">The /proc folder; injectable so this can be tested against a made-up one on any OS.</param>
    internal static bool HasExited(int pid, string procRoot = "/proc") =>
        HasExited(Path.Combine(procRoot, pid.ToString(CultureInfo.InvariantCulture)));

    // A process without a readable stat (a made-up /proc, or one that is mid-exit) counts as running; the other files decide.
    private static bool HasExited(string dir) => ReadStat(dir) is { } stat && IsExitedState(stat);

    private static string? ReadStat(string dir)
    {
        try
        {
            return File.ReadAllText(Path.Combine(dir, "stat"));
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
