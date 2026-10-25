using Avalonia.Headless.XUnit;

namespace Savepoint.App.Tests.Features.DocScreenshots;

// Rewrites the pictures in docs/images, so it only runs when asked: SAVEPOINT_DOC_SCREENSHOTS=1 dotnet test --project tests/Savepoint.App.Tests
// -c Release --filter-class Savepoint.App.Tests.Features.DocScreenshots.DocScreenshotTests. The gate skips it, so it never changes the docs.
public class DocScreenshotTests
{
    [AvaloniaFact]
    public async Task Redraws_every_image_in_docs_images()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("SAVEPOINT_DOC_SCREENSHOTS") == "1", "Set SAVEPOINT_DOC_SCREENSHOTS=1 to rewrite the pictures in docs/images.");

        await DocScreenshotJob.RunAsync();

        foreach (var name in DocScreenshotJob.Names)
        {
            Assert.True(new FileInfo(Path.Combine(DocScreenshotJob.ImageFolder, name)).Length > 10_000, $"{name} looks empty.");
        }
    }
}
