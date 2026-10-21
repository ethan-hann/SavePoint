using System.Globalization;
using System.Text.Json;
using Jeffijoe.MessageFormat;

namespace Savepoint.App.Localization;

/// <summary>
/// The messages of one locale, formatted as ICU MessageFormat. A locale is every JSON file in its folder under
/// <c>Localization/Assets/Lang/&lt;locale&gt;/</c>, merged; the same key in two files is an error, so each area of
/// the app owns its own file without risking a silent override.
/// </summary>
public sealed class LocalizationCatalog
{
    private const string _resourcePrefix = "Lang/";

    private static readonly IReadOnlyDictionary<string, object?> _noArgs = new Dictionary<string, object?>();
    private static LocalizationCatalog? _current;

    private readonly IReadOnlyDictionary<string, string> _messages;
    private readonly MessageFormatter _formatter;
    private readonly bool _throwOnMissingKey;
    private readonly Lock _formatterGate = new();

    /// <param name="messages">Key to ICU pattern.</param>
    /// <param name="locale">A language tag such as <c>en</c> or <c>en-GB</c>; plural rules and number formats follow it.</param>
    /// <param name="throwOnMissingKey">Overrides <see cref="ThrowOnMissingKeyByDefault"/> for this catalog.</param>
    public LocalizationCatalog(IReadOnlyDictionary<string, string> messages, string locale, bool? throwOnMissingKey = null)
    {
        _messages = messages;
        _throwOnMissingKey = throwOnMissingKey ?? ThrowOnMissingKeyByDefault;
        Locale = locale;
        _formatter = new MessageFormatter(useCache: true, culture: CultureInfo.GetCultureInfo(locale));
    }

    // Debug builds fail loudly on a missing key; release builds show the key so the UI still works.
    public static bool ThrowOnMissingKeyByDefault { get; set; } =
#if DEBUG
        true;
#else
        false;
#endif

    /// <summary>The catalog markup and view models read when none is passed in; set once at startup, before any XAML that uses <c>{loc:Loc}</c> loads.</summary>
    public static LocalizationCatalog Current
    {
        get => _current ?? throw new InvalidOperationException("No localization catalog has been set.");
        set => _current = value;
    }

    /// <summary>The language tag the catalog formats for.</summary>
    public string Locale { get; }

    /// <summary>Every key the catalog holds.</summary>
    public IReadOnlyCollection<string> Keys => _messages.Keys.ToList();

    /// <summary>Builds a catalog from one JSON object of strings.</summary>
    public static LocalizationCatalog FromJson(string json, string locale, bool? throwOnMissingKey = null) =>
        new(ParseMessages(json), locale, throwOnMissingKey);

    /// <summary>Merges the files of one locale; each is a JSON object of strings.</summary>
    /// <param name="files">Display name (used in errors) and content of each file.</param>
    /// <param name="locale">The language tag the catalog formats for.</param>
    /// <param name="throwOnMissingKey">Overrides <see cref="ThrowOnMissingKeyByDefault"/> for this catalog.</param>
    /// <exception cref="InvalidDataException">A file is not a JSON object of strings, or two files define the same key; the message names both.</exception>
    public static LocalizationCatalog FromFiles(IEnumerable<(string Name, string Json)> files, string locale, bool? throwOnMissingKey = null)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (name, json) in files)
        {
            Dictionary<string, string> messages;
            try
            {
                messages = ParseMessages(json);
            }
            catch (Exception error) when (error is JsonException or NotSupportedException)
            {
                throw new InvalidDataException($"{name} is not a JSON object of strings: {error.Message}", error);
            }

            foreach (var (key, pattern) in messages)
            {
                if (owners.TryGetValue(key, out var first))
                {
                    throw new InvalidDataException($"The message key '{key}' is defined in both {first} and {name}.");
                }

                owners[key] = name;
                merged[key] = pattern;
            }
        }

        return new LocalizationCatalog(merged, locale, throwOnMissingKey);
    }

    /// <summary>The locales shipped inside the app, one per folder under <c>Lang</c>, as language tags (<c>en</c>, <c>en-GB</c>); the language list in Settings is driven by this.</summary>
    public static IReadOnlyList<string> EmbeddedLocales() =>
        [.. EmbeddedFiles().Select(file => file.Locale).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Loads and merges every embedded file of a locale.</summary>
    /// <exception cref="FileNotFoundException">The app ships no files for the locale.</exception>
    /// <exception cref="InvalidDataException">See <see cref="FromFiles"/>.</exception>
    public static LocalizationCatalog LoadEmbedded(string locale, bool? throwOnMissingKey = null)
    {
        var assembly = typeof(LocalizationCatalog).Assembly;
        var files = EmbeddedFiles()
            .Where(file => string.Equals(file.Locale, locale, StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.Name, StringComparer.Ordinal)
            .Select(file =>
            {
                using var stream = assembly.GetManifestResourceStream(file.ResourceName)!;
                using var reader = new StreamReader(stream);
                return (file.Name, reader.ReadToEnd());
            })
            .ToList();

        return files.Count == 0
            ? throw new FileNotFoundException($"No message catalog for locale '{locale}'.", $"{_resourcePrefix}{locale}/")
            : FromFiles(files, locale, throwOnMissingKey);
    }

    /// <summary>Formats a message with ICU arguments.</summary>
    /// <exception cref="KeyNotFoundException">The key is missing and the catalog is set to throw.</exception>
    public string Format(string key, IReadOnlyDictionary<string, object?>? args = null)
    {
        if (!_messages.TryGetValue(key, out var pattern))
        {
            return _throwOnMissingKey
                ? throw new KeyNotFoundException($"Missing message key '{key}' for locale '{Locale}'.")
                : key;
        }

        // The formatter keeps a parse cache that is not safe to share across threads.
        lock (_formatterGate)
        {
            return _formatter.FormatMessage(pattern, args ?? _noArgs);
        }
    }

    // Resource names are Lang/<locale>/<file>.json, set through LogicalName in the project file; on Windows the folder part arrives with a backslash.
    private static IEnumerable<(string ResourceName, string Locale, string Name)> EmbeddedFiles()
    {
        foreach (var resourceName in typeof(LocalizationCatalog).Assembly.GetManifestResourceNames())
        {
            var path = resourceName.Replace('\\', '/');
            if (!path.StartsWith(_resourcePrefix, StringComparison.Ordinal) || !path.EndsWith(".json", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = path[_resourcePrefix.Length..].Split('/', 2);
            if (parts.Length == 2)
            {
                yield return (resourceName, parts[0], path[_resourcePrefix.Length..]);
            }
        }
    }

    private static Dictionary<string, string> ParseMessages(string json)
    {
        var options = new JsonSerializerOptions { AllowDuplicateProperties = false };
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json, options)
            ?? throw new JsonException("The message catalog must be a JSON object.");
    }
}
