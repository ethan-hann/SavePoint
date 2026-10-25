using System.Text.RegularExpressions;
using Savepoint.App.Tests.Theme;

namespace Savepoint.App.Tests.Features.DocScreenshots;

// A picture in the guides that the screenshot job cannot redraw goes stale on the next UI change, so each one needs a generator.
public partial class DocScreenshotCoverageTests
{
    private static readonly string[] _guides = ["README.md", Path.Combine("docs", "user-guide.md")];

    [GeneratedRegex(@"\]\((?:docs/)?images/(?<name>[^)\s]+\.png)\)")]
    private static partial Regex ImageLink();

    private static SortedSet<string> ReferencedImages()
    {
        var root = ThemeTestSupport.RepoRoot();
        return [.. _guides.SelectMany(guide => ImageLink().Matches(File.ReadAllText(Path.Combine(root, guide))).Select(match => match.Groups["name"].Value))];
    }

    [Fact]
    public void The_guides_reference_images()
    {
        Assert.NotEmpty(ReferencedImages());
    }

    [Fact]
    public void Every_image_the_guides_show_exists_in_docs_images()
    {
        var folder = Path.Combine(ThemeTestSupport.RepoRoot(), "docs", "images");

        var missing = ReferencedImages().Where(name => !File.Exists(Path.Combine(folder, name))).ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public void The_screenshot_job_draws_every_image_the_guides_show()
    {
        var ungenerated = ReferencedImages().Except(DocScreenshotJob.Names).ToArray();

        Assert.Empty(ungenerated);
    }

    [Fact]
    public void The_screenshot_job_draws_only_images_the_guides_show()
    {
        var unused = DocScreenshotJob.Names.Except(ReferencedImages()).ToArray();

        Assert.Empty(unused);
    }
}
