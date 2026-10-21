using System.Globalization;

namespace Savepoint.FakeGame;

// What the command line asked for. Every value is optional; with none the game just runs until told to quit.
internal sealed record Options
{
    public const string Usage =
        "usage: FakeGame [--saves <dir>] [--save-every <ms>] [--exit-after <ms>] [--exit-code <n>] [--lock <file>] [--ready-file <path>] [--spawn <path> [args...]]";

    public string? SavesDirectory { get; init; }

    public int SaveEveryMilliseconds { get; init; }

    public int? ExitAfterMilliseconds { get; init; }

    public int ExitCode { get; init; }

    public string? LockFile { get; init; }

    public string? ReadyFile { get; init; }

    public string? SpawnPath { get; init; }

    public IReadOnlyList<string> SpawnArguments { get; init; } = [];

    // Everything after the spawn path belongs to the program being started, so --spawn must come last.
    public static bool TryParse(IReadOnlyList<string> args, out Options options, out string error)
    {
        options = new Options();
        error = string.Empty;
        for (var i = 0; i < args.Count; i++)
        {
            var name = args[i];
            switch (name)
            {
                case "--spawn":
                    if (!TryValue(args, i, out var path, out error))
                    {
                        return false;
                    }

                    options = options with { SpawnPath = path, SpawnArguments = [.. args.Skip(i + 2)] };
                    return true;
                case "--saves" or "--lock" or "--ready-file":
                    if (!TryValue(args, i, out var text, out error))
                    {
                        return false;
                    }

                    options = name switch
                    {
                        "--saves" => options with { SavesDirectory = text },
                        "--lock" => options with { LockFile = text },
                        _ => options with { ReadyFile = text },
                    };
                    i++;
                    break;
                case "--save-every" or "--exit-after":
                    if (!TryValue(args, i, out var milliseconds, out error))
                    {
                        return false;
                    }

                    if (!int.TryParse(milliseconds, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                    {
                        error = $"{name} needs a whole number of milliseconds, not '{milliseconds}'.";
                        return false;
                    }

                    options = name == "--save-every" ? options with { SaveEveryMilliseconds = parsed } : options with { ExitAfterMilliseconds = parsed };
                    i++;
                    break;
                case "--exit-code":
                    if (!TryValue(args, i, out var code, out error))
                    {
                        return false;
                    }

                    if (!int.TryParse(code, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var exitCode))
                    {
                        error = $"--exit-code needs a whole number, not '{code}'.";
                        return false;
                    }

                    options = options with { ExitCode = exitCode };
                    i++;
                    break;
                default:
                    error = $"Unknown argument '{name}'.";
                    return false;
            }
        }

        return true;
    }

    private static bool TryValue(IReadOnlyList<string> args, int index, out string value, out string error)
    {
        if (index + 1 >= args.Count)
        {
            value = string.Empty;
            error = $"{args[index]} needs a value.";
            return false;
        }

        value = args[index + 1];
        error = string.Empty;
        return true;
    }
}
