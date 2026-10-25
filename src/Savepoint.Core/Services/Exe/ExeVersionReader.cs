using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using AsmResolver;
using AsmResolver.PE;
using AsmResolver.PE.Win32Resources.Version;

namespace Savepoint.Core.Services.Exe;

/// <summary>Reads the version a game executable declares, from its bytes alone.</summary>
/// <remarks>
/// Works on every OS because it parses the file itself; nothing is loaded or run. Callers pass bytes because the
/// executable they care about is often a historical blob in git that does not exist on disk. Services show the
/// result as the detected version and never write it anywhere.
/// </remarks>
public static class ExeVersionReader
{
    private const string _productVersion = "ProductVersion";
    private const string _fileVersion = "FileVersion";
    private const string _bundleVersion = "CFBundleShortVersionString";

    private static readonly Regex _unityProductVersion = new(@"^\d+\.\d+\.\d+[abfp]\d+ \([0-9a-f]{12}\)$", RegexOptions.CultureInvariant);
    private static readonly Regex _changelistSuffix = new(@"-CL-\d+", RegexOptions.CultureInvariant);
    private static readonly Regex _dottedNumber = new(@"(?<!\d\.?)\d+(?:\.\d+){2,3}(?![\d.])", RegexOptions.CultureInvariant);

    /// <summary>Reads the version from the bytes of a Windows executable (a PE file, including one run through Proton).</summary>
    /// <param name="bytes">The whole file, or at least everything up to its resource section.</param>
    /// <returns>
    /// The version text, or null when the input is not a PE file, has no version resource, only an empty or all-zero
    /// one, cannot be parsed, or only holds a number that is not the game's: a Unity player's engine version, an
    /// Unreal build id with no game number in it (a number inside one, as in "++Game+release.1.4.7-CL-1", is
    /// returned alone), a string with no digit, or the 1.0 placeholder. Never throws for bad input. The product version
    /// string wins, then the file version string, then, only when the file has no version string at all and is not an
    /// engine vendor's, the fixed numeric file version with trailing zero parts dropped (keeping at least major.minor).
    /// A string is trimmed of whitespace and NULs, and a comma-separated form such as "1, 0, 0, 1" becomes "1.0.0.1".
    /// </returns>
    public static string? FromPe(byte[]? bytes)
    {
        if (bytes is null || bytes.Length < 0x40 || bytes[0] != (byte)'M' || bytes[1] != (byte)'Z')
        {
            return null;
        }

        try
        {
            // Errors in parts of the file the version never touches must not hide the version.
            var image = PEImage.FromBytes(bytes, new PEReaderParameters(EmptyErrorListener.Instance));
            if (image.Resources is not { } resources)
            {
                return null;
            }

            var versions = VersionInfoResource.FindAllFromDirectory(resources).OfType<VersionInfoResource>().ToList();
            var tables = versions
                .SelectMany(version => version.GetChildren().OfType<StringFileInfo>())
                .SelectMany(fileInfo => fileInfo.Tables)
                .ToList();
            if (IsUnityPlayer(tables) || IsEngineItself(tables))
            {
                return null;
            }

            var rejectedAny = false;
            foreach (var key in new[] { _productVersion, _fileVersion })
            {
                foreach (var table in tables)
                {
                    if (!table.TryGetValue(key, out var raw) || Clean(raw) is not { } text)
                    {
                        continue;
                    }

                    if (GameVersionIn(text) is { } found)
                    {
                        return found;
                    }

                    rejectedAny = true;
                }
            }

            // With strings present the fixed numbers are the engine's, and an engine vendor's executable never has a game's.
            return rejectedAny || IsEngineExecutable(tables) ? null : FixedValue(versions);
        }
        catch (Exception)
        {
            // Truncated and hostile files fail in many ways inside the parser; for a display value they all mean "unknown".
            return null;
        }
    }

    /// <summary>Reads the version from the <c>Contents/Info.plist</c> of a macOS <c>.app</c> bundle.</summary>
    /// <param name="infoPlist">The bytes of the XML property list.</param>
    /// <returns>
    /// The <c>CFBundleShortVersionString</c> value, or null when it is missing or empty, the list is malformed, or
    /// it is in the binary format, which is not read. Never throws for bad input and never resolves external entities.
    /// </returns>
    public static string? FromInfoPlist(byte[]? infoPlist)
    {
        if (infoPlist is null || infoPlist.Length == 0 || infoPlist.AsSpan().StartsWith("bplist"u8))
        {
            return null;
        }

        try
        {
            // Info.plist files carry a DOCTYPE; it is skipped, never fetched.
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, IgnoreComments = true };
            using var stream = new MemoryStream(infoPlist, writable: false);
            using var reader = XmlReader.Create(stream, settings);
            var dictionary = XDocument.Load(reader).Root?.Element("dict");
            var key = dictionary?.Elements("key").FirstOrDefault(e => e.Value == _bundleVersion);
            return key?.ElementsAfterSelf().FirstOrDefault() is { Name.LocalName: "string" } value ? Clean(value.Value) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Unity writes its own version into every player, so the numbers say nothing about the game.
    private static bool IsUnityPlayer(List<StringTable> tables) =>
        tables.Any(table => table.ContainsKey("Unity Version")
            || (table.TryGetValue(_productVersion, out var product) && _unityProductVersion.IsMatch(product.Trim())));

    // Unreal stamps the engine's numbers on every executable it builds, and those never name the game.
    private static bool IsEngineExecutable(List<StringTable> tables) =>
        tables.Any(table => table.TryGetValue("CompanyName", out var company) && company.StartsWith("Epic Games", StringComparison.Ordinal)
            || table.TryGetValue("InternalName", out var internalName) && internalName == "UnrealEngine");

    // Some Unreal games ship an executable still named after the engine, with the engine's own version.
    private static bool IsEngineItself(List<StringTable> tables) =>
        tables.Any(table => table.TryGetValue("ProductName", out var productName) && productName == "Unreal Engine");

    // Unreal build ids ("++Game+release.1.4.7-CL-196638") carry the game's number only sometimes.
    private static string? GameVersionIn(string text)
    {
        if (!text.Any(char.IsAsciiDigit))
        {
            return null;
        }

        if (text.StartsWith("++", StringComparison.Ordinal) || _changelistSuffix.IsMatch(text))
        {
            var match = _dottedNumber.Match(text);
            return match.Success ? match.Value : null;
        }

        return IsPlaceholder(text) ? null : text;
    }

    // Many projects never replace the template's 1.0.0.0.
    private static bool IsPlaceholder(string text) => text is "1.0.0.0" or "1.0.0" or "1.0";

    private static string? FixedValue(List<VersionInfoResource> versions)
    {
        foreach (var version in versions)
        {
            if (version.FixedVersionInfo?.FileVersion is { } fixedVersion && Format(fixedVersion) is { } text)
            {
                return text;
            }
        }

        return null;
    }

    private static string? Format(Version version)
    {
        int[] parts = [version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision)];
        if (parts.All(part => part == 0))
        {
            return null;
        }

        var count = parts.Length;
        while (count > 2 && parts[count - 1] == 0)
        {
            count--;
        }

        var text = string.Join('.', parts.Take(count));
        return IsPlaceholder(text) ? null : text;
    }

    private static string? Clean(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        var text = raw.AsSpan();
        var start = 0;
        var end = text.Length;
        while (start < end && IsJunk(text[start]))
        {
            start++;
        }

        while (end > start && IsJunk(text[end - 1]))
        {
            end--;
        }

        if (start == end)
        {
            return null;
        }

        var trimmed = text[start..end].ToString();
        if (IsCommaSeparatedNumbers(trimmed))
        {
            trimmed = string.Join('.', trimmed.Split(',').Select(part => part.Trim()));
        }

        // "0.0.0.0" is what a build writes when nobody set a version.
        return trimmed.All(c => c is '0' or '.') ? null : trimmed;
    }

    private static bool IsJunk(char c) => char.IsWhiteSpace(c) || char.IsControl(c);

    // Old resource compilers write "1, 0, 0, 1".
    private static bool IsCommaSeparatedNumbers(string text)
    {
        var parts = text.Split(',');
        return parts.Length > 1 && parts.All(part => part.Trim() is { Length: > 0 } digits && digits.All(c => c is >= '0' and <= '9'));
    }
}
