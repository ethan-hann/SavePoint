using System.Globalization;
using Avalonia.Headless.XUnit;
using Savepoint.App.Theme;

namespace Savepoint.App.Tests.Theme;

public class UppercaseTests
{
    private static readonly CultureInfo _turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly CultureInfo _english = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void Dotted_i_becomes_a_dotted_capital_in_turkish_and_a_plain_one_in_english()
    {
        Assert.Equal("İNDİR", Uppercase.Of("indir", _turkish));
        Assert.Equal("INDIR", Uppercase.Of("indir", _english));
    }

    [Fact]
    public void Dotless_i_becomes_a_plain_capital_in_turkish()
    {
        Assert.Equal("IŞIK", Uppercase.Of("ışık", _turkish));
    }

    [Fact]
    public void Converter_uses_the_culture_it_is_given()
    {
        var converter = UppercaseConverter.Instance;

        Assert.Equal("İNDİR", converter.Convert("indir", typeof(string), null, _turkish));
        Assert.Equal("INDIR", converter.Convert("indir", typeof(string), null, _english));
    }

    [Fact]
    public void Converter_turns_null_into_empty_text_and_other_values_into_their_text()
    {
        var converter = UppercaseConverter.Instance;

        Assert.Equal(string.Empty, converter.Convert(null, typeof(string), null, _english));
        Assert.Equal("12.5", converter.Convert(12.5, typeof(string), null, _english));
    }

    [Fact]
    public void Converter_cannot_convert_back()
    {
        Assert.Throws<NotSupportedException>(() => UppercaseConverter.Instance.ConvertBack("A", typeof(string), null, _english));
    }

    [AvaloniaFact]
    public void Markup_extension_uppercases_the_inner_text_for_the_named_culture()
    {
        var extension = new UpperExtension("indir") { Culture = "tr-TR" };

        Assert.Equal("İNDİR", extension.ProvideValue(null!));
    }

    [AvaloniaFact]
    public void Markup_extension_defaults_to_the_catalog_language()
    {
        var extension = new UpperExtension("your games");

        Assert.Equal("YOUR GAMES", extension.ProvideValue(null!));
    }

    [AvaloniaFact]
    public void Markup_extension_without_text_gives_empty_text()
    {
        Assert.Equal(string.Empty, new UpperExtension().ProvideValue(null!));
    }
}
