using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Savepoint.App.Localization;

namespace Savepoint.App.Theme;

public static class Uppercase
{
    // TextInfo rather than ToUpperInvariant: Turkish i and I do not pair the way the invariant culture assumes.
    public static string Of(string text, CultureInfo culture) => culture.TextInfo.ToUpper(text);
}

// Catalog text is stored in normal case; overline labels render it uppercase for the user's language.
public sealed class UppercaseConverter : IValueConverter
{
    public static UppercaseConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Uppercase.Of(System.Convert.ToString(value, culture) ?? string.Empty, culture);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// XAML form: Text="{theme:Upper {loc:Loc some.key}}".
public sealed class UpperExtension : MarkupExtension
{
    public UpperExtension()
    {
    }

    public UpperExtension(object text) => Text = text;

    public object? Text { get; set; }

    // A culture name such as "tr-TR"; defaults to the catalog's language.
    public string? Culture { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        Uppercase.Of(Text?.ToString() ?? string.Empty, CultureInfo.GetCultureInfo(Culture ?? LocalizationCatalog.Current.Locale));
}
