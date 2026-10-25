using System.Xml;
using System.Xml.Linq;

namespace Savepoint.Core.Launch;

/// <summary>One program file found in an install folder.</summary>
/// <param name="RelativePath">The path below the install folder, with either kind of separator.</param>
/// <param name="Length">The file's size in bytes.</param>
public readonly record struct InstalledProgram(string RelativePath, long Length);

/// <summary>Picks the program that most likely is the running game, so a game started through a link can be followed.</summary>
/// <remarks>
/// Programs that are never the game (launchers, crash reporters, anti-cheat bootstraps, redistributables, uninstallers, tools) are
/// dropped by name and by folder. The rest are scored: shallow beats deep, large beats small, a name that resembles the game's
/// beats one that does not, and Unreal's shipping program beats the small starter beside it. A folder where nothing scores well
/// (a runtime, a tool suite) gets no suggestion rather than a bad one.
/// </remarks>
public static class ProcessNameSuggester
{
    private const double _depthPenalty = 6;
    private const double _toolFolderPenalty = 40;
    private const double _shippingBonus = 40;
    private const double _binariesFolderBonus = 10;
    private const double _variantPenalty = 30;
    private const double _namePenalty32 = 10;
    private const int _crowdedFolder = 6;
    private const double _minimumScore = -25;

    // A program whose file name contains one of these is a helper of the game, not the game.
    private static readonly string[] _helperNameParts =
    [
        "launch", "unins", "uninst", "setup", "install", "redist", "updater", "patcher", "crash", "reporter", "bugreport", "sendrpt",
        "errorreport", "dumpwriter", "minidump", "sndrpt", "handler", "helper", "subprocess", "webhelper", "cefclient", "cefsimple",
        "beservice", "easyanticheat", "start_protected_game", "bootstrap", "watchdog", "uploader", "sender", "converter", "editor",
        "creationkit", "benchmark", "dedicated", "srcds", "hlds", "hltv", "vconsole", "overlay", "startapp", "settings",
        "shadercompile", "ffmpeg", "python", "java", "checker", "dotnet", "restarter", "modtest",
    ];

    // Anti-cheat launchers add these to the game's own name, so they count only at its end.
    private static readonly string[] _helperNameEndings = ["_be", "_eac", "_uc", "_zk"];

    // Too short to match as a part of a longer name, or too common as one.
    private static readonly string[] _helperExactNames = ["dxc", "fxc", "ucc", "scc", "play", "start", "run", "cleaner", "codcleaner"];

    private static readonly string[] _helperFolders =
    [
        "tools", "tool", "sdk", "redist", "_commonredist", "support", "launcher", "engine", "thirdparty", "third_party", "plugins",
        "streamingassets", "mono", "jre", "jre_arm", "modtools", "dlc", "melonloader", "bepinex", "easyanticheat", "battleye", "cache",
        "devtools", "dist", "lib", "lib64", "distribution", "prerequisites", "install", "papyrus compiler", "launchpad", "crs", "crashreport",
        "dotnetcore", "setup", "old_xna_version", "improved camera-69-1-0", "webview2", "__overlay", "_internal", "cef", "takeover",
    ];

    private static readonly string[] _variantWords = ["original", "modtest", "demo", "old", "test", "backup", "debug"];

    private static readonly string[] _binaryFolders = ["bin", "bin64", "binaries", "win64", "win32", "x64", "x86", "retail", "game", "system"];

    /// <summary>Names the program in an install folder that most likely is the game itself.</summary>
    /// <param name="installFolderName">The install folder's own name, used as a hint.</param>
    /// <param name="gameName">The game's name as the player wrote it, or null; used as a second hint.</param>
    /// <param name="programs">The programs found in the install folder.</param>
    /// <returns>The file name with its extension, or null when nothing in the folder looks like a game.</returns>
    public static string? Suggest(string? installFolderName, string? gameName, IEnumerable<InstalledProgram> programs) =>
        SuggestProgram(installFolderName, gameName, programs)?.FileName;

    /// <summary>
    /// Names the process to follow for the program in an install folder that most likely is the game. The same pick as
    /// <see cref="Suggest(string?, string?, IEnumerable{InstalledProgram})"/>, except that a macOS <c>.app</c> bundle is named by the program inside it
    /// (the <c>CFBundleExecutable</c> of its <c>Info.plist</c>, or the bundle's own name without <c>.app</c> when that is missing or unreadable),
    /// because that is the name of the running process.
    /// </summary>
    /// <param name="installFolderName">The install folder's own name, used as a hint.</param>
    /// <param name="gameName">The game's name as the player wrote it, or null; used as a second hint.</param>
    /// <param name="programs">The programs found in the install folder, with paths relative to <paramref name="installFolder"/>.</param>
    /// <param name="installFolder">The install folder, where a bundle's <c>Info.plist</c> is read.</param>
    /// <returns>The process name, or null when nothing in the folder looks like a game.</returns>
    public static string? Suggest(string? installFolderName, string? gameName, IEnumerable<InstalledProgram> programs, string installFolder) =>
        SuggestProgram(installFolderName, gameName, programs) is not { } best
            ? null
            : best.FileName.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
                ? BundleProgramName(Path.Combine(installFolder, best.RelativePath), best.FileName)
                : best.FileName;

    /// <summary>
    /// Orders every program in an install folder by how likely it is the game itself, for a list the player picks from. The same pick as
    /// <see cref="Suggest(string?, string?, IEnumerable{InstalledProgram})"/> comes first. Programs the suggester would never offer
    /// (launchers, crash reporters, tools, and anything that scores too low) follow, shallowest and largest first, so a player who really
    /// does need the launcher can still choose it.
    /// </summary>
    /// <param name="installFolderName">The install folder's own name, used as a hint.</param>
    /// <param name="gameName">The game's name as the player wrote it, or null; used as a second hint.</param>
    /// <param name="programs">The programs found in the install folder.</param>
    /// <returns>The programs' <see cref="InstalledProgram.RelativePath"/> values as given, none dropped, best first.</returns>
    public static IReadOnlyList<string> Rank(string? installFolderName, string? gameName, IEnumerable<InstalledProgram> programs)
    {
        var parsed = programs.Select(Parse).ToList();
        var scored = RankCandidates(installFolderName, gameName, parsed);
        var rest = parsed
            .Except(scored)
            .OrderBy(p => p.Folders.Length)
            .ThenByDescending(p => p.Length)
            .ThenBy(p => p.RelativePath, StringComparer.Ordinal);
        return [.. scored.Concat(rest).Select(p => p.RelativePath)];
    }

    // The running process is the bundle's program, not the bundle folder.
    private static string BundleProgramName(string bundleFolder, string bundleFileName)
    {
        try
        {
            var plist = Path.Combine(bundleFolder, "Contents", "Info.plist");
            if (File.Exists(plist))
            {
                using var reader = XmlReader.Create(plist, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
                var document = XDocument.Load(reader);
                var entries = document.Descendants("dict").FirstOrDefault()?.Elements().ToList() ?? [];
                for (var i = 0; i + 1 < entries.Count; i++)
                {
                    if (entries[i].Name == "key" && entries[i].Value == "CFBundleExecutable" && entries[i + 1].Name == "string" && entries[i + 1].Value.Trim() is { Length: > 0 } name)
                    {
                        return name;
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or XmlException)
        {
            // A binary or damaged plist falls through to the bundle's own name.
        }

        return bundleFileName[..^4];
    }

    private static ParsedProgram? SuggestProgram(string? installFolderName, string? gameName, IEnumerable<InstalledProgram> programs) =>
        RankCandidates(installFolderName, gameName, programs.Select(Parse).ToList()).FirstOrDefault();

    // The programs worth offering as the game, best first; helpers and programs that score too low are left out.
    private static List<ParsedProgram> RankCandidates(string? installFolderName, string? gameName, List<ParsedProgram> parsed)
    {
        var candidates = parsed.Where(p => !IsHelper(p)).ToList();

        // Many programs side by side are a tool suite; helpers beside a game do not count toward that.
        var crowded = candidates
            .GroupBy(p => string.Join('/', p.Folders).ToLowerInvariant())
            .Where(group => group.Count() >= _crowdedFolder)
            .Select(group => group.Key)
            .ToHashSet();

        var hints = new[] { installFolderName, gameName }
            .Select(Normalize)
            .Where(hint => hint.Length > 0)
            .ToArray();

        return
        [
            .. candidates
                .Select(p => (Program: p, Score: Score(p, hints, crowded.Contains(string.Join('/', p.Folders).ToLowerInvariant()))))
                .Where(entry => entry.Score >= _minimumScore)
                .OrderByDescending(entry => entry.Score)
                .ThenByDescending(entry => entry.Program.Length)
                .ThenBy(entry => entry.Program.FileName, StringComparer.OrdinalIgnoreCase)
                .Select(entry => entry.Program),
        ];
    }

    private static ParsedProgram Parse(InstalledProgram program)
    {
        var parts = program.RelativePath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        var fileName = parts.Length == 0 ? string.Empty : parts[^1];
        return new ParsedProgram(fileName, parts.Length <= 1 ? [] : parts[..^1], program.Length, program.RelativePath);
    }

    private static bool IsHelper(ParsedProgram program)
    {
        if (program.FileName.Length == 0)
        {
            return true;
        }

        var stem = Path.GetFileNameWithoutExtension(program.FileName).ToLowerInvariant();
        if (_helperExactNames.Contains(stem) || IsPlayWrapper(program.FileName))
        {
            return true;
        }

        if (_helperNameParts.Any(part => stem.Contains(part, StringComparison.Ordinal)) || _helperNameEndings.Any(ending => stem.EndsWith(ending, StringComparison.Ordinal)))
        {
            return true;
        }

        return program.Folders.Any(folder => _helperFolders.Contains(folder.ToLowerInvariant()) || folder.EndsWith("_Data", StringComparison.OrdinalIgnoreCase));
    }

    // "PlayGTAV.exe" starts a game; "PlayaVR.exe" is one.
    private static bool IsPlayWrapper(string fileName) => fileName.Length > 4 && fileName.StartsWith("Play", StringComparison.Ordinal) && !char.IsLower(fileName[4]);

    private static double Score(ParsedProgram program, string[] hints, bool inCrowdedFolder)
    {
        var stem = Path.GetFileNameWithoutExtension(program.FileName);
        var lower = stem.ToLowerInvariant();
        var score = -_depthPenalty * program.Folders.Length;

        if (lower.EndsWith("-shipping", StringComparison.Ordinal) || lower.StartsWith("shippingpc", StringComparison.Ordinal))
        {
            score += _shippingBonus;
        }

        if (program.Folders.Length > 0 && _binaryFolders.Contains(program.Folders[^1].ToLowerInvariant()))
        {
            score += _binariesFolderBonus;
        }

        if (inCrowdedFolder)
        {
            score -= _toolFolderPenalty;
        }

        if (_variantWords.Any(word => lower.EndsWith("_" + word, StringComparison.Ordinal) || lower.EndsWith(" " + word, StringComparison.Ordinal)))
        {
            score -= _variantPenalty;
        }

        if (lower.EndsWith("32", StringComparison.Ordinal) || lower.EndsWith("_x86", StringComparison.Ordinal))
        {
            score -= _namePenalty32;
        }

        var megabytes = Math.Max(program.Length, 1) / (1024.0 * 1024.0);
        score += Math.Clamp(3 * Math.Log2(megabytes), -15, 30);

        return score + hints.Select(hint => Similarity(hint, Normalize(Core(stem)))).DefaultIfEmpty(0).Max();
    }

    // The game's own name without the suffixes builds add: "SB-Win64-Shipping", "Game_DX11", "Game_x64".
    private static string Core(string stem)
    {
        var lower = stem.ToLowerInvariant();
        foreach (var suffix in new[] { "-win64-shipping", "-win32-shipping", "_dx11", "_dx12", "_dx9", "_x64", ".x64", "x64", "64" })
        {
            if (lower.EndsWith(suffix, StringComparison.Ordinal) && lower.Length > suffix.Length)
            {
                lower = lower[..^suffix.Length];
            }
        }

        return lower;
    }

    private static double Similarity(string hint, string stem)
    {
        if (stem.Length < 3)
        {
            return 0;
        }

        // A program named after the game starts with its name; a helper only mentions it in the middle ("ExecPubg").
        if (hint.Contains(stem, StringComparison.Ordinal) || stem.StartsWith(hint, StringComparison.Ordinal))
        {
            return 25;
        }

        var shared = 0;
        while (shared < hint.Length && shared < stem.Length && hint[shared] == stem[shared])
        {
            shared++;
        }

        return shared >= 4 ? Math.Min(25, 2 * shared) : 0;
    }

    private static string Normalize(string? text) =>
        text is null ? string.Empty : new string([.. text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);

    private sealed record ParsedProgram(string FileName, string[] Folders, long Length, string RelativePath);
}
