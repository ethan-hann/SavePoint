using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Launch;
using Savepoint.App.Localization;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Launch;

public class UnsavedChangesBlockSavingTests
{
    private static readonly UiFormatter _text = new(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: "windows", timeZone: TimeZoneInfo.Utc);

    private static UnsavedChangesBlockViewModel Create() =>
        new(RepoRole.Install, "Title", "Body", false, _ => Task.CompletedTask, _ => Task.CompletedTask, "path", 0, 0);

    [Fact]
    public void The_start_of_finishing_is_announced_once_and_the_text_says_so()
    {
        var block = Create();
        var spoken = new List<string>();
        block.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UnsavedChangesBlockViewModel.Announcement))
            {
                spoken.Add(block.Announcement);
            }
        };
        block.BeginSaving("Saving");
        spoken.Clear();

        block.UpdateSaving(new JobProgress(3, 23, 69_300_000_000, 70_000_000_000), _text);
        spoken.Clear();
        block.UpdateSaving(new JobProgress(3, 23, 69_300_000_000, 70_000_000_000, null, Finishing: true), _text);
        block.UpdateSaving(new JobProgress(4, 23, 69_300_000_000, 70_000_000_000, null, Finishing: true), _text);

        Assert.Equal(["Making sure everything is stored safely. This can take a few minutes for a big game."], spoken);
        Assert.Equal(spoken[0], block.ProgressText);
    }
}
