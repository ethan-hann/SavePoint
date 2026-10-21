using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jeffijoe.MessageFormat.Formatting;
using Jeffijoe.MessageFormat.Parsing;
using Savepoint.App.Localization;

namespace Savepoint.App.Tests.Localization;

internal sealed record IcuArgument(string Name, string Kind, IReadOnlyList<string> Options);

internal static class LintAssert
{
    public static void NoProblems(IReadOnlyCollection<string> problems)
    {
        if (problems.Count > 0)
        {
            Assert.Fail(string.Join(Environment.NewLine, problems));
        }
    }
}

internal static class CatalogLint
{
    private static readonly (string Word, Regex Pattern)[] _banned =
    [
        BannedWord("commit"),
        BannedWord("commits"),
        BannedWord("branch"),
        BannedWord("branches"),
        BannedWord("checkout"),
        ("check out", new Regex(@"\bcheck\s+out\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
        BannedWord("repo"),
        BannedWord("repos"),
        BannedWord("repository"),
        BannedWord("repositories"),
        BannedWord("git"),
        BannedWord("stash"),
        BannedWord("rebase"),
        BannedWord("staging"),
        BannedWord("detached"),
        ("HEAD", new Regex(@"\bHEAD\b", RegexOptions.CultureInvariant)),
    ];

    private static readonly string[] _operatingSystems = ["windows", "macos", "linux"];

    // Past this many argument combinations each argument is varied on its own, so a long message cannot make the test slow.
    private const int _maxCombinations = 2000;

    public static IReadOnlyDictionary<string, string> ParseCatalog(string json)
    {
        var options = new JsonSerializerOptions { AllowDuplicateProperties = false };
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json, options)
            ?? throw new JsonException("The catalog must be a JSON object.");
    }

    public static IReadOnlyDictionary<string, string> English() =>
        ReadLocale(Path.Combine(SourceLint.LangDirectory(), "en"));

    // Every file of one locale folder merged into one catalog; a key in two files or a file that is not a JSON object of strings is an error.
    public static IReadOnlyDictionary<string, string> ReadLocale(string localeDirectory)
    {
        var problems = new List<string>();
        var messages = ReadLocale(localeDirectory, problems);
        if (problems.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, problems));
        }

        return messages;
    }

    // Problems with the layout of the Lang folder itself: stray files, unreadable files and keys defined twice, in every locale folder.
    public static IReadOnlyList<string> LayoutProblems(string langDirectory)
    {
        var problems = new List<string>();

        foreach (var stray in Directory.EnumerateFiles(langDirectory, "*", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
        {
            problems.Add($"{Path.GetFileName(stray)} sits outside a locale folder; messages live in Lang/<locale>/<area>.json");
        }

        foreach (var directory in LocaleDirectories(langDirectory))
        {
            ReadLocale(directory, problems);
        }

        return problems;
    }

    public static IReadOnlyList<string> BannedVocabulary(IReadOnlyDictionary<string, string> catalog) =>
        catalog.OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .SelectMany(entry => _banned
                .Where(banned => banned.Pattern.IsMatch(entry.Value))
                .Select(banned => $"'{entry.Key}' uses the banned word '{banned.Word}': {entry.Value}"))
            .ToList();

    public static IReadOnlyList<IcuArgument> Arguments(string message)
    {
        var found = new List<IcuArgument>();
        Collect(message, found);
        return found;
    }

    public static IReadOnlyList<string> FormatProblems(string locale, IReadOnlyDictionary<string, string> catalog)
    {
        LocalizationCatalog formatter;
        try
        {
            formatter = new LocalizationCatalog(catalog, locale, throwOnMissingKey: true);
        }
        catch (CultureNotFoundException)
        {
            return [$"{locale}: '{locale}' is not a valid language tag"];
        }

        var problems = new List<string>();
        foreach (var (key, message) in catalog.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var problem = FormatProblem(formatter, key, message);
            if (problem is not null)
            {
                problems.Add($"{locale}: {problem}");
            }
        }

        return problems;
    }

    public static IReadOnlyList<string> AdditionalLocaleProblems(string langDirectory)
    {
        var english = ReadLocale(Path.Combine(langDirectory, "en"));
        var problems = new List<string>();

        foreach (var directory in LocaleDirectories(langDirectory))
        {
            var locale = Path.GetFileName(directory);
            if (locale.Equals("en", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fileProblems = new List<string>();
            var other = ReadLocale(directory, fileProblems);
            if (fileProblems.Count > 0)
            {
                problems.AddRange(fileProblems.Select(problem => $"{locale}: {problem}"));
                continue;
            }

            problems.AddRange(LocaleParity(locale, english, other));
            problems.AddRange(FormatProblems(locale, other));
        }

        return problems;
    }

    private static IEnumerable<string> LocaleDirectories(string langDirectory) =>
        Directory.EnumerateDirectories(langDirectory).Order(StringComparer.Ordinal);

    private static Dictionary<string, string> ReadLocale(string localeDirectory, List<string> problems)
    {
        var messages = new Dictionary<string, string>(StringComparer.Ordinal);
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        var locale = Path.GetFileName(localeDirectory);

        foreach (var file in Directory.EnumerateFiles(localeDirectory, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var name = $"{locale}/{Path.GetRelativePath(localeDirectory, file).Replace('\\', '/')}";
            IReadOnlyDictionary<string, string> parsed;
            try
            {
                parsed = ParseCatalog(File.ReadAllText(file));
            }
            catch (JsonException error)
            {
                problems.Add($"{name} is not a JSON object of strings: {error.Message}");
                continue;
            }

            foreach (var (key, message) in parsed)
            {
                if (owners.TryGetValue(key, out var first))
                {
                    problems.Add($"'{key}' is defined in both {first} and {name}");
                    continue;
                }

                owners[key] = name;
                messages[key] = message;
            }
        }

        return messages;
    }

    private static List<string> LocaleParity(
        string locale,
        IReadOnlyDictionary<string, string> english,
        IReadOnlyDictionary<string, string> other)
    {
        var problems = new List<string>();

        problems.AddRange(english.Keys.Where(key => !other.ContainsKey(key)).Order(StringComparer.Ordinal)
            .Select(key => $"{locale}: '{key}' is missing"));
        problems.AddRange(other.Keys.Where(key => !english.ContainsKey(key)).Order(StringComparer.Ordinal)
            .Select(key => $"{locale}: '{key}' is not in English"));

        foreach (var key in english.Keys.Where(other.ContainsKey).Order(StringComparer.Ordinal))
        {
            // A message that does not parse is reported by FormatProblems.
            if (!TryArgumentNames(english[key], out var englishNames) || !TryArgumentNames(other[key], out var otherNames))
            {
                continue;
            }

            if (!englishNames.SetEquals(otherNames))
            {
                problems.Add($"{locale}: '{key}' uses arguments [{string.Join(", ", otherNames.Order())}] but English uses [{string.Join(", ", englishNames.Order())}]");
            }
        }

        return problems;
    }

    private static bool TryArgumentNames(string message, out HashSet<string> names)
    {
        try
        {
            names = Arguments(message).Select(argument => argument.Name).ToHashSet(StringComparer.Ordinal);
            return true;
        }
        catch (Exception)
        {
            names = [];
            return false;
        }
    }

    private static string? FormatProblem(LocalizationCatalog formatter, string key, string message)
    {
        IReadOnlyList<IcuArgument> arguments;
        try
        {
            arguments = Arguments(message);
        }
        catch (Exception error)
        {
            return $"'{key}' does not parse: {error.Message}";
        }

        foreach (var args in Combinations(arguments))
        {
            string text;
            try
            {
                text = formatter.Format(key, args);
            }
            catch (Exception error)
            {
                return $"'{key}' cannot be formatted with {Describe(args)}: {error.Message}";
            }

            // A quoted or stray brace does not throw; it just leaves a placeholder in the text.
            if (text.Contains('{', StringComparison.Ordinal) || text.Contains('}', StringComparison.Ordinal))
            {
                return $"'{key}' leaves a brace in its text with {Describe(args)}: {text}";
            }
        }

        return null;
    }

    private static string Describe(IReadOnlyDictionary<string, object?> args) =>
        args.Count == 0 ? "no arguments" : string.Join(", ", args.Select(pair => $"{pair.Key}={pair.Value}"));

    private static IEnumerable<Dictionary<string, object?>> Combinations(IReadOnlyList<IcuArgument> arguments)
    {
        var samples = arguments.Select(argument => (argument.Name, Values: Samples(argument))).ToList();
        var count = samples.Aggregate(1L, (total, sample) => total * sample.Values.Count);

        if (count > _maxCombinations)
        {
            var baseline = samples.ToDictionary(sample => sample.Name, sample => sample.Values[0]);
            yield return baseline;
            foreach (var (name, values) in samples)
            {
                foreach (var value in values.Skip(1))
                {
                    yield return new Dictionary<string, object?>(baseline) { [name] = value };
                }
            }

            yield break;
        }

        IEnumerable<Dictionary<string, object?>> all = [new Dictionary<string, object?>()];
        foreach (var (name, values) in samples)
        {
            all = all.SelectMany(partial => values.Select(value => new Dictionary<string, object?>(partial) { [name] = value })).ToList();
        }

        foreach (var combination in all)
        {
            yield return combination;
        }
    }

    // `os` is always one of the three the UI reports; other selects get every branch plus a value that matches none.
    private static IReadOnlyList<object?> Samples(IcuArgument argument) => argument.Kind switch
    {
        "plural" or "selectordinal" or "number" => [0, 1, 2, 5, 21],
        "date" or "time" => [new DateTime(2026, 10, 20, 15, 30, 0, DateTimeKind.Utc)],
        "select" when argument.Name == "os" => [.. _operatingSystems],
        "select" => [.. argument.Options.Where(option => option != "other"), "unlisted"],
        _ => ["sample text"],
    };

    private static void Collect(string text, List<IcuArgument> found)
    {
        foreach (var request in new PatternParser(new LiteralParser()).Parse(new StringBuilder(text)))
        {
            var kind = string.IsNullOrEmpty(request.FormatterName) ? "plain" : request.FormatterName;
            var blocks = kind is "plural" or "select" or "selectordinal" ? KeyedBlocks(request) : [];

            Merge(found, request.Variable, kind, blocks.Select(block => block.Key));
            foreach (var block in blocks)
            {
                Collect(block.Text, found);
            }
        }
    }

    private static void Merge(List<IcuArgument> found, string name, string kind, IEnumerable<string> options)
    {
        var index = found.FindIndex(argument => argument.Name == name);
        if (index < 0)
        {
            found.Add(new IcuArgument(name, kind, options.ToList()));
            return;
        }

        var existing = found[index];
        found[index] = new IcuArgument(
            name,
            Rank(kind) > Rank(existing.Kind) ? kind : existing.Kind,
            existing.Options.Concat(options).Distinct().ToList());
    }

    private static int Rank(string kind) => kind switch
    {
        "plain" => 0,
        "number" => 1,
        "date" or "time" => 2,
        _ => 3,
    };

    // The keyed blocks of a plural or select, such as "one {# save} other {# saves}"; an "offset:1" prefix has no block and is skipped.
    private static List<(string Key, string Text)> KeyedBlocks(FormatterRequest request)
    {
        var arguments = request.FormatterArguments ?? string.Empty;
        var blocks = new List<(string Key, string Text)>();
        var i = 0;

        while (i < arguments.Length)
        {
            while (i < arguments.Length && char.IsWhiteSpace(arguments[i]))
            {
                i++;
            }

            var keyStart = i;
            while (i < arguments.Length && !char.IsWhiteSpace(arguments[i]) && arguments[i] != '{')
            {
                i++;
            }

            var key = arguments[keyStart..i];
            while (i < arguments.Length && char.IsWhiteSpace(arguments[i]))
            {
                i++;
            }

            if (i >= arguments.Length || arguments[i] != '{')
            {
                continue;
            }

            var start = ++i;
            var depth = 1;
            while (i < arguments.Length && depth > 0)
            {
                switch (arguments[i])
                {
                    case '\'':
                        i = SkipQuote(arguments, i);
                        continue;
                    case '{':
                        depth++;
                        break;
                    case '}':
                        depth--;
                        break;
                }

                i++;
            }

            blocks.Add((key, arguments[start..(i - 1)]));
        }

        return blocks;
    }

    // An apostrophe quotes a following brace up to the next apostrophe; anywhere else it is an ordinary character.
    private static int SkipQuote(string text, int apostrophe)
    {
        if (apostrophe + 1 >= text.Length)
        {
            return text.Length;
        }

        if (text[apostrophe + 1] == '\'')
        {
            return apostrophe + 2;
        }

        if (text[apostrophe + 1] is '{' or '}')
        {
            var close = text.IndexOf('\'', apostrophe + 2);
            return close < 0 ? text.Length : close + 1;
        }

        return apostrophe + 1;
    }

    private static (string Word, Regex Pattern) BannedWord(string word) =>
        (word, new Regex($@"\b{word}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
}
