using SkiaSharp;

namespace Savepoint.Tools.IconGen;

// Writes the icon files into src/Savepoint.App/Assets/Icon. Run it by hand when the design changes:
//   dotnet run --project tools/IconGen -c Release
// IconRenderer draws the mark; the frames at 32 px and below use the filled bookmark glyph.
internal static class Program
{
    private static readonly int[] _icoSizes = [16, 24, 32, 48, 64, 128, 256];
    private static readonly int[] _pngSizes = [256, 512, 1024];

    private static int Main(string[] args)
    {
        var root = FindRepoRoot();
        if (root is null)
        {
            Console.Error.WriteLine("IconGen: run it from inside the SavePoint checkout.");
            return 2;
        }

        var assets = Path.Combine(root, "src", "Savepoint.App", "Assets");
        var output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(assets, "Icon");
        Directory.CreateDirectory(output);

        using var outline = SKTypeface.FromFile(Path.Combine(assets, "Fonts", "MaterialSymbolsRounded.ttf"))
            ?? throw new InvalidOperationException("The icon font could not be loaded.");
        using var filled = SKTypeface.FromFile(Path.Combine(assets, "Fonts", "MaterialSymbolsRoundedFilled.ttf"))
            ?? throw new InvalidOperationException("The filled icon font could not be loaded.");

        File.WriteAllBytes(Path.Combine(output, "savepoint.ico"), IcoFile.Build(_icoSizes.Select(size => (size, IconRenderer.Render(size, outline, filled))).ToArray()));
        foreach (var size in _pngSizes)
        {
            File.WriteAllBytes(Path.Combine(output, $"savepoint-{size}.png"), IconRenderer.Render(size, outline, filled));
        }

        Console.WriteLine($"Wrote the icon files to {output}");
        return 0;
    }

    private static string? FindRepoRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Savepoint.slnx")))
                {
                    return directory.FullName;
                }
            }
        }

        return null;
    }
}
