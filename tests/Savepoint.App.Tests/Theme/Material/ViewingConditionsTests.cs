using Savepoint.App.Theme.Material;

namespace Savepoint.App.Tests.Theme.Material;

public class ViewingConditionsTests
{
    [Fact]
    public void Default_matches_the_reference_derivation()
    {
        var expected = GoldenData.Current.ViewingConditions;
        var actual = ViewingConditions.Default;

        Numbers.AssertSame(expected.N, actual.N, "n");
        Numbers.AssertSame(expected.Aw, actual.Aw, "aw");
        Numbers.AssertSame(expected.Nbb, actual.Nbb, "nbb");
        Numbers.AssertSame(expected.Ncb, actual.Ncb, "ncb");
        Numbers.AssertSame(expected.C, actual.C, "c");
        Numbers.AssertSame(expected.Nc, actual.Nc, "nc");
        Numbers.AssertSame(expected.RgbD[0], actual.RgbD[0], "rgbD[0]");
        Numbers.AssertSame(expected.RgbD[1], actual.RgbD[1], "rgbD[1]");
        Numbers.AssertSame(expected.RgbD[2], actual.RgbD[2], "rgbD[2]");
        Numbers.AssertSame(expected.Fl, actual.Fl, "fl");
        Numbers.AssertSame(expected.FLRoot, actual.FLRoot, "fLRoot");
        Numbers.AssertSame(expected.Z, actual.Z, "z");
    }
}
