using System.Globalization;
using System.Text.Json;
using Jeffijoe.MessageFormat;

namespace Savepoint.App.Localization;

public sealed class LocalizationCatalog
{
    private static readonly IReadOnlyDictionary<string, object?> _noArgs = new Dictionary<string, object?>();
    private static LocalizationCatalog? _current;

    private readonly IReadOnlyDictionary<string, string> _messages;
    private readonly MessageFormatter _formatter;
    private readonly bool _throwOnMissingKey;
    private readonly Lock _formatterGate = new();

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

    public static LocalizationCatalog Current
    {
        get => _current ?? throw new InvalidOperationException("No localization catalog has been set.");
        set => _current = value;
    }

    public string Locale { get; }

    public static LocalizationCatalog FromJson(string json, string locale, bool? throwOnMissingKey = null) =>
        new(ParseMessages(json), locale, throwOnMissingKey);

    public static LocalizationCatalog LoadEmbedded(string locale, bool? throwOnMissingKey = null)
    {
        var resourceName = $"{typeof(LocalizationCatalog).Namespace}.Assets.Lang.{locale}.json";
        using var stream = typeof(LocalizationCatalog).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"No message catalog for locale '{locale}'.", resourceName);
        using var reader = new StreamReader(stream);
        return FromJson(reader.ReadToEnd(), locale, throwOnMissingKey);
    }

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

    private static Dictionary<string, string> ParseMessages(string json)
    {
        var options = new JsonSerializerOptions { AllowDuplicateProperties = false };
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json, options)
            ?? throw new JsonException("The message catalog must be a JSON object.");
    }
}
