using Savepoint.App.Features.Settings;
using Savepoint.Testing;

namespace Savepoint.App.Tests.Features.Settings;

public class NoticesBesideTheAppTests
{
    private static string RepoRoot => TestEnvironment.RepoRoot ?? throw new InvalidOperationException("The tests did not run inside a checkout.");

    [Fact]
    public void Settings_finds_the_notices_file_in_the_apps_output_folder()
    {
        var found = SettingsEnvironment.ForThisApp().FindNotices();

        Assert.NotNull(found);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md"), found);
        Assert.Equal(File.ReadAllText(Path.Combine(RepoRoot, "THIRD-PARTY-NOTICES.md")), File.ReadAllText(found));
    }

    [Fact]
    public void Every_license_text_is_in_the_output_folder_beside_the_notices()
    {
        var source = Path.Combine(RepoRoot, "licenses");
        var expected = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(source, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        var shipped = Path.Combine(AppContext.BaseDirectory, "licenses");
        var actual = Directory.Exists(shipped)
            ? Directory.EnumerateFiles(shipped, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(shipped, path))
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [];

        Assert.NotEmpty(expected);
        Assert.Equal(expected, actual);
    }
}
