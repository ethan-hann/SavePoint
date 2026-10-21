using Avalonia.Headless.XUnit;
using Avalonia.Media;

namespace Savepoint.App.Tests.Theme.Fonts;

public class FontFamilyTests
{
    [AvaloniaTheory]
    [InlineData("RobotoFont", "Roboto", "Roboto-Regular.ttf")]
    [InlineData("RobotoMonoFont", "Roboto Mono", "RobotoMono-Regular.ttf")]
    [InlineData("SymbolsFont", "Material Symbols Rounded", "MaterialSymbolsRounded.ttf")]
    [InlineData("SymbolsFilledFont", "Material Symbols Rounded Filled", "MaterialSymbolsRoundedFilled.ttf")]
    public void Font_resource_resolves_to_its_embedded_face(string resourceKey, string familyName, string fileName)
    {
        var family = FontTestSupport.Resource(resourceKey);

        var glyphTypeface = FontTestSupport.Resolve(family);

        Assert.Equal(familyName, glyphTypeface.FamilyName);
        Assert.Equal(FontTestSupport.HashOfEmbeddedFile(fileName), FontTestSupport.HashOfResolvedFile(glyphTypeface));
    }

    [AvaloniaTheory]
    [InlineData("RobotoFont", FontWeight.Normal, "Roboto-Regular.ttf")]
    [InlineData("RobotoFont", FontWeight.Medium, "Roboto-Medium.ttf")]
    [InlineData("RobotoFont", FontWeight.Bold, "Roboto-Bold.ttf")]
    [InlineData("RobotoMonoFont", FontWeight.Normal, "RobotoMono-Regular.ttf")]
    [InlineData("RobotoMonoFont", FontWeight.Medium, "RobotoMono-Medium.ttf")]
    public void Weight_selects_its_own_face_without_simulation(string resourceKey, FontWeight weight, string fileName)
    {
        var family = FontTestSupport.Resource(resourceKey);

        var glyphTypeface = FontTestSupport.Resolve(family, weight);

        Assert.Equal(weight, glyphTypeface.Weight);
        Assert.Equal(FontSimulations.None, glyphTypeface.FontSimulations);
        Assert.Equal(FontTestSupport.HashOfEmbeddedFile(fileName), FontTestSupport.HashOfResolvedFile(glyphTypeface));
    }
}
