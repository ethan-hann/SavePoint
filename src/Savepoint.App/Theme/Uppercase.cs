using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Savepoint.App.Localization;

namespace Savepoint.App.Theme;

/// <summary>
/// Culture-aware uppercasing for overline labels. The catalog stores normal-case text, so XAML renders a label in
/// capitals through <see cref="UppercaseConverter"/> or <see cref="UpperExtension"/> instead of storing it shouted.
/// </summary>
public static class Uppercase
{
    /// <summary>
    /// Uppercases with the culture's own casing rules (Turkish dotted and dotless i differ from the invariant
    /// pairing), so the label is right for the catalog's language.
    /// </summary>
    public static string Of(string text, CultureInfo culture) => culture.TextInfo.ToUpper(text);
}

/// <summary>
/// Binding converter that uppercases the bound value's text for the binding's culture; null becomes empty text.
/// One way only.
/// </summary>
public sealed class UppercaseConverter : IValueConverter
{
    /// <summary>The shared instance, for <c>{x:Static theme:UppercaseConverter.Instance}</c>.</summary>
    public static UppercaseConverter Instance { get; } = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Uppercase.Of(System.Convert.ToString(value, culture) ?? string.Empty, culture);

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always: the original casing is lost.</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Markup extension that uppercases static text once, at load: <c>Text="{theme:Upper {loc:Loc some.key}}"</c>.
/// For text that changes at runtime use <see cref="UppercaseConverter"/> in a binding instead.
/// </summary>
public sealed class UpperExtension : MarkupExtension
{
    /// <summary>Creates the extension for property-style use; set <see cref="Text"/>.</summary>
    public UpperExtension()
    {
    }

    /// <param name="text">The text to uppercase, usually a nested <c>{loc:Loc ...}</c>.</param>
    public UpperExtension(object text) => Text = text;

    /// <summary>The text to uppercase; null gives empty text.</summary>
    public object? Text { get; set; }

    /// <summary>A culture name such as "tr-TR"; defaults to the language of the current message catalog.</summary>
    public string? Culture { get; set; }

    /// <inheritdoc />
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        Uppercase.Of(Text?.ToString() ?? string.Empty, CultureInfo.GetCultureInfo(Culture ?? LocalizationCatalog.Current.Locale));
}
