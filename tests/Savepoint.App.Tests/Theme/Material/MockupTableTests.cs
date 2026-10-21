using Savepoint.App.Theme.Material;

namespace Savepoint.App.Tests.Theme.Material;

// The README token table was drawn from another derivation, so rows pin the library value and record which cells agree.
public class MockupTableTests
{
    internal const uint BlueSeed = 0xFF2986CC;

    private static readonly (string Mode, string Role, string Library, string Readme)[] _rows =
    [
        ("dark", "background", "101418", "111416"),
        ("dark", "surface", "101418", "111416"),
        ("dark", "surfaceContainerLow", "181C20", "191C1E"),
        ("dark", "surfaceContainer", "1D2024", "1D2022"),
        ("dark", "surfaceContainerHigh", "272A2F", "22262A|282A2C"),
        ("dark", "onSurface", "E0E2E8", "E1E2E5"),
        ("dark", "onSurfaceVariant", "C2C7CF", "C0C7CE"),
        ("dark", "outline", "8C9199", "8A9199"),
        ("dark", "outlineVariant", "42474E", "2A3136|40484E"),
        ("dark", "primary", "9DCBFB", "9BCBFF"),
        ("dark", "onPrimary", "003355", "003354"),
        ("dark", "primaryContainer", "124A73", "00497D"),
        ("dark", "onPrimaryContainer", "CFE5FF", "D0E4FF"),
        ("dark", "error", "FFB4AB", "FFB4AB"),
        ("light", "background", "F7F9FF", "F8F9FC"),
        ("light", "surface", "F7F9FF", "F8F9FC"),
        ("light", "surfaceContainerLow", "F2F3F9", "ECEEF1"),
        ("light", "surfaceContainer", "ECEEF4", "FFFFFF"),
        ("light", "surfaceContainerHigh", "E6E8EE", "E6E8EC"),
        ("light", "onSurface", "181C20", "191C1E"),
        ("light", "onSurfaceVariant", "42474E", "40484E"),
        ("light", "outline", "72777F", "70787E"),
        ("light", "outlineVariant", "C2C7CF", "D6DBE0|C0C7CE"),
        ("light", "primary", "31628D", "00639D"),
        ("light", "onPrimary", "FFFFFF", "FFFFFF"),
        ("light", "primaryContainer", "CFE5FF", "D0E4FF"),
        ("light", "onPrimaryContainer", "124A73", "001D34"),
        ("light", "error", "BA1A1A", "BA1A1A"),
    ];

    private static readonly string[] _agreeingCells = ["dark/error", "light/error", "light/onPrimary"];

    public static TheoryData<string, string, string> LibraryRows()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var (mode, role, library, _) in _rows)
        {
            data.Add(mode, role, library);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LibraryRows))]
    public void Blue_seed_role_is_the_library_value(string mode, string role, string library)
    {
        var roles = Md3Palette.Build(BlueSeed, mode == "dark").ToRoleMap();

        Assert.Equal("#" + library, Md3Palette.ToHex(roles[role]));
    }

    [Fact]
    public void Only_the_recorded_cells_agree_with_the_README_table()
    {
        var palettes = new Dictionary<string, IReadOnlyDictionary<string, uint>>
        {
            ["dark"] = Md3Palette.Build(BlueSeed, dark: true).ToRoleMap(),
            ["light"] = Md3Palette.Build(BlueSeed, dark: false).ToRoleMap(),
        };

        var agreeing = _rows
            .Where(row => row.Readme.Split('|').Contains(Md3Palette.ToHex(palettes[row.Mode][row.Role]).TrimStart('#')))
            .Select(row => $"{row.Mode}/{row.Role}")
            .Order(StringComparer.Ordinal);

        Assert.Equal(_agreeingCells, agreeing);
    }
}
