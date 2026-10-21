using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Savepoint.App.Tests.Localization;

internal sealed record CSharpLiteral(string Value, int Start, int End, bool Interpolated);

internal static class SourceLint
{
    private static readonly Regex _locKey = new(
        @"\{\s*(?:[A-Za-z_]\w*:)?Loc(?:Extension)?\s+(?:Key\s*=\s*)?(?:'(?<quoted>[^']*)'|(?<bare>[^\s,}']+))",
        RegexOptions.CultureInvariant);

    // Format, Loc and LocExtension reach the catalog; string.Format takes a format string, not a key.
    private static readonly Regex _catalogCall = new(
        @"(?:\b(?:Loc|LocExtension)|(?<!\bstring\.)(?<!\bString\.)\bFormat)\s*\(\s*(?:key\s*:\s*)?$",
        RegexOptions.CultureInvariant);

    private static readonly Regex _errorOrValidationId = new(@"^(error|validation)\.[A-Za-z0-9.]+$", RegexOptions.CultureInvariant);

    private static readonly Regex _wholeKey = new(@"^[A-Za-z][A-Za-z0-9]*(\.[A-Za-z0-9]+)+$", RegexOptions.CultureInvariant);

    // An Upper wrapper only counts when it wraps another markup extension, so {theme:Upper Hello} is still hardcoded text.
    private static readonly Regex _allowedMarkup = new(
        @"^\{\s*(?:Binding|CompiledBinding|ReflectionBinding|TemplateBinding|x:Static|(?:[A-Za-z_]\w*:)?Loc(?:Extension)?)(?=[\s}])|^\{\s*(?:[A-Za-z_]\w*:)?Upper(?:Extension)?\s+\{",
        RegexOptions.CultureInvariant);

    private static readonly HashSet<string> _textProperties =
    [
        "Text",
        "Content",
        "Title",
        "Header",
        "Watermark",
        "PlaceholderText",
        "ToolTip.Tip",
        "AutomationProperties.Name",
        "AutomationProperties.HelpText",
    ];

    // Elements whose child text becomes visible text; other elements, such as FontFamily resources, hold text that is not copy.
    private static readonly HashSet<string> _textElements =
    [
        "TextBlock",
        "SelectableTextBlock",
        "Run",
        "Label",
        "Button",
        "CheckBox",
        "RadioButton",
        "ToggleButton",
        "ToggleSwitch",
        "MenuItem",
        "TabItem",
        "Expander",
    ];

    public static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Savepoint.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Savepoint.slnx was not found above the test binaries.");
    }

    public static string LangDirectory() =>
        Path.Combine(RepoRoot(), "src", "Savepoint.App", "Localization", "Assets", "Lang");

    public static string RelativePath(string path) =>
        Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/');

    public static IReadOnlyList<string> SourceFiles(string extension)
    {
        return Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*" + extension, SearchOption.AllDirectories)
            .Where(path => !RelativePath(path).Split('/').Any(part => part is "bin" or "obj"))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    public static IReadOnlyList<CSharpLiteral> Literals(string source)
    {
        var literals = new List<CSharpLiteral>();
        var i = 0;

        while (i < source.Length)
        {
            if (StartsWith(source, i, "//"))
            {
                var end = source.IndexOf('\n', i);
                i = end < 0 ? source.Length : end + 1;
            }
            else if (StartsWith(source, i, "/*"))
            {
                var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? source.Length : end + 2;
            }
            else if (source[i] == '\'')
            {
                i = SkipCharLiteral(source, i);
            }
            else if (source[i] == '"')
            {
                i = ReadString(source, i, literals);
            }
            else
            {
                i++;
            }
        }

        return literals.OrderBy(literal => literal.Start).ToList();
    }

    // Comments document markup usage; a key named there is not a reference.
    public static IReadOnlyList<string> XamlLocKeys(string axaml) =>
        _locKey.Matches(Regex.Replace(axaml, "<!--.*?-->", string.Empty, RegexOptions.Singleline))
            .Select(match => match.Groups["quoted"].Success ? match.Groups["quoted"].Value : match.Groups["bare"].Value)
            .ToList();

    public static IReadOnlyList<string> CatalogCallKeys(string source) =>
        Literals(source)
            .Where(literal => !literal.Interpolated)
            .Where(literal => _catalogCall.IsMatch(source[Math.Max(0, literal.Start - 80)..literal.Start]))
            .Select(literal => literal.Value)
            .ToList();

    // Keys the code asks the catalog for: catalog calls and XAML markup anywhere, plus error and validation ids wherever they are written.
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> CatalogReferences()
    {
        var references = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        void Add(string key, string path)
        {
            var file = RelativePath(path);
            if (!references.TryGetValue(key, out var files))
            {
                references[key] = files = [];
            }

            if (!files.Contains(file))
            {
                files.Add(file);
            }
        }

        foreach (var path in SourceFiles(".cs"))
        {
            var source = File.ReadAllText(path);
            foreach (var key in CatalogCallKeys(source))
            {
                Add(key, path);
            }

            foreach (var literal in Literals(source).Where(l => !l.Interpolated && _errorOrValidationId.IsMatch(l.Value)))
            {
                Add(literal.Value, path);
            }
        }

        foreach (var path in SourceFiles(".axaml"))
        {
            foreach (var key in XamlLocKeys(File.ReadAllText(path)))
            {
                Add(key, path);
            }
        }

        return references.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value);
    }

    // Every literal in code and every XAML key counts as a use, so a key reached through any helper still counts.
    public static IReadOnlyCollection<string> UsedKeys()
    {
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in SourceFiles(".cs"))
        {
            used.UnionWith(Literals(File.ReadAllText(path)).Where(literal => !literal.Interpolated).Select(literal => literal.Value));
        }

        foreach (var path in SourceFiles(".axaml"))
        {
            used.UnionWith(XamlLocKeys(File.ReadAllText(path)));
        }

        return used;
    }

    public static IReadOnlyList<string> MissingKeys(
        IReadOnlyDictionary<string, IReadOnlyList<string>> references,
        IReadOnlyCollection<string> catalogKeys)
    {
        var known = catalogKeys.ToHashSet(StringComparer.Ordinal);
        return references
            .Where(reference => !known.Contains(reference.Key))
            .OrderBy(reference => reference.Key, StringComparer.Ordinal)
            .Select(reference => $"'{reference.Key}' is used in {string.Join(", ", reference.Value)} but is not in the catalog")
            .ToList();
    }

    public static IReadOnlyList<string> UnusedKeys(IReadOnlyCollection<string> catalogKeys, IReadOnlyCollection<string> usedKeys)
    {
        var used = usedKeys.ToHashSet(StringComparer.Ordinal);
        return catalogKeys
            .Where(key => !used.Contains(key))
            .Order(StringComparer.Ordinal)
            .Select(key => $"'{key}' is in the catalog but no source file uses it")
            .ToList();
    }

    public static IReadOnlyList<string> HardcodedXamlText(string fileName, string axaml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(axaml);
        }
        catch (XmlException error)
        {
            return [$"{fileName}: not well-formed XML: {error.Message}"];
        }

        const string Fix = "use {loc:Loc key}, a binding or {x:Static ...}";
        var problems = new List<string>();

        foreach (var element in document.Descendants())
        {
            var name = element.Name.LocalName;

            foreach (var attribute in element.Attributes().Where(a => a.Name.Namespace == XNamespace.None && _textProperties.Contains(a.Name.LocalName)))
            {
                if (IsHardcoded(attribute.Value))
                {
                    problems.Add($"{fileName}: <{name} {attribute.Name.LocalName}=\"{attribute.Value}\"> is hardcoded text; {Fix}");
                }
            }

            if (name == "Setter"
                && element.Attribute("Property") is { } property
                && element.Attribute("Value") is { } value
                && IsTextProperty(property.Value)
                && IsHardcoded(value.Value))
            {
                problems.Add($"{fileName}: <Setter Property=\"{property.Value}\" Value=\"{value.Value}\"> is hardcoded text; {Fix}");
            }

            if (IsTextProperty(name) || _textElements.Contains(name))
            {
                foreach (var text in element.Nodes().OfType<XText>().Where(t => HasLetter(t.Value)))
                {
                    problems.Add($"{fileName}: <{name}> holds the text '{text.Value.Trim()}'; {Fix}");
                }
            }
        }

        return problems;
    }

    public static IReadOnlyList<string> DynamicKeyConstruction(string fileName, string source, IReadOnlyCollection<string> namespaces)
    {
        var problems = new List<string>();

        foreach (var literal in Literals(source))
        {
            if (!namespaces.Any(ns => literal.Value.StartsWith(ns + ".", StringComparison.Ordinal)))
            {
                continue;
            }

            var complete = !literal.Interpolated && _wholeKey.IsMatch(literal.Value);
            var joined = source[..literal.Start].TrimEnd().EndsWith('+') || source[literal.End..].TrimStart().StartsWith('+');
            if (complete && !joined)
            {
                continue;
            }

            problems.Add($"{fileName}: \"{literal.Value}\" looks like part of a catalog key built at run time; write each key out in full");
        }

        return problems;
    }

    private static bool IsTextProperty(string name) =>
        _textProperties.Contains(name) || (name.Contains('.', StringComparison.Ordinal) && _textProperties.Contains(name[(name.IndexOf('.', StringComparison.Ordinal) + 1)..]));

    // A binding, a catalog lookup or a static reference is fine; so is a value with no letters, such as an icon code point or a separator.
    private static bool IsHardcoded(string value) => HasLetter(value) && !_allowedMarkup.IsMatch(value);

    private static bool HasLetter(string value) => value.Any(char.IsLetter);

    private static bool StartsWith(string source, int index, string prefix) =>
        string.CompareOrdinal(source, index, prefix, 0, prefix.Length) == 0;

    private static int SkipCharLiteral(string source, int quote)
    {
        var i = quote + 1;
        while (i < source.Length && source[i] != '\'')
        {
            i += source[i] == '\\' ? 2 : 1;
        }

        return Math.Min(i + 1, source.Length);
    }

    private static int ReadString(string source, int quote, List<CSharpLiteral> literals)
    {
        var prefixStart = quote;
        while (prefixStart > 0 && source[prefixStart - 1] is '$' or '@')
        {
            prefixStart--;
        }

        var prefix = source[prefixStart..quote];
        var interpolated = prefix.Contains('$', StringComparison.Ordinal);
        var verbatim = prefix.Contains('@', StringComparison.Ordinal);

        var quotes = 0;
        while (quote + quotes < source.Length && source[quote + quotes] == '"')
        {
            quotes++;
        }

        if (!verbatim && quotes >= 3)
        {
            return ReadRawString(source, prefixStart, quote, quotes, interpolated, literals);
        }

        var value = new StringBuilder();
        var i = quote + 1;
        while (i < source.Length)
        {
            var c = source[i];
            if (c == '"')
            {
                if (verbatim && i + 1 < source.Length && source[i + 1] == '"')
                {
                    value.Append('"');
                    i += 2;
                    continue;
                }

                i++;
                literals.Add(new CSharpLiteral(value.ToString(), prefixStart, i, interpolated));
                return i;
            }

            if (interpolated && c == '{')
            {
                if (StartsWith(source, i, "{{"))
                {
                    value.Append('{');
                    i += 2;
                }
                else
                {
                    i = ReadInterpolationHole(source, i, value, literals);
                }

                continue;
            }

            if (interpolated && StartsWith(source, i, "}}"))
            {
                value.Append('}');
                i += 2;
                continue;
            }

            if (!verbatim && c == '\\' && i + 1 < source.Length)
            {
                i = ReadEscape(source, i, value);
                continue;
            }

            value.Append(c);
            i++;
        }

        return i;
    }

    private static int ReadRawString(string source, int prefixStart, int quote, int quotes, bool interpolated, List<CSharpLiteral> literals)
    {
        var contentStart = quote + quotes;
        var close = source.IndexOf(new string('"', quotes), contentStart, StringComparison.Ordinal);
        if (close < 0)
        {
            return source.Length;
        }

        var end = close + quotes;
        while (end < source.Length && source[end] == '"')
        {
            end++;
        }

        literals.Add(new CSharpLiteral(source[contentStart..(end - quotes)], prefixStart, end, interpolated));
        return end;
    }

    // The hole's own text stays in the value so a prefix such as $"error.{code}" is still visible; literals inside it are found too.
    private static int ReadInterpolationHole(string source, int open, StringBuilder value, List<CSharpLiteral> literals)
    {
        var i = open;
        var depth = 0;

        while (i < source.Length)
        {
            var c = source[i];
            if (c == '"')
            {
                i = ReadString(source, i, literals);
                continue;
            }

            if (c == '\'')
            {
                i = SkipCharLiteral(source, i);
                continue;
            }

            i++;
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}' && --depth == 0)
            {
                break;
            }
        }

        value.Append(source, open, i - open);
        return i;
    }

    private static int ReadEscape(string source, int backslash, StringBuilder value)
    {
        var escaped = source[backslash + 1];
        if (escaped == 'u' && backslash + 5 < source.Length
            && int.TryParse(source.AsSpan(backslash + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
        {
            value.Append((char)code);
            return backslash + 6;
        }

        value.Append(escaped switch
        {
            'n' => '\n',
            't' => '\t',
            'r' => '\r',
            '0' => '\0',
            _ => escaped,
        });
        return backslash + 2;
    }
}
