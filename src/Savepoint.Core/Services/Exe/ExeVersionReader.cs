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

    /// <summary>Reads the version from the bytes of a Windows executable (a PE file, including one run through Proton).</summary>
    /// <param name="bytes">The whole file, or at least everything up to its resource section.</param>
    /// <returns>
    /// The version text, or null when the input is not a PE file, has no version resource, only an empty or all-zero
    /// one, or cannot be parsed. Never throws for bad input. The product version string wins, then the file version
    /// string, then the fixed numeric file version with trailing zero parts dropped (keeping at least major.minor).
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
            return StringValue(versions, _productVersion) ?? StringValue(versions, _fileVersion) ?? FixedValue(versions);
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

    private static string? StringValue(List<VersionInfoResource> versions, string key)
    {
        foreach (var version in versions)
        {
            foreach (var fileInfo in version.GetChildren().OfType<StringFileInfo>())
            {
                foreach (var table in fileInfo.Tables)
                {
                    if (table.TryGetValue(key, out var raw) && Clean(raw) is { } text)
                    {
                        return text;
                    }
                }
            }
        }

        return null;
    }

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

        return string.Join('.', parts.Take(count));
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
