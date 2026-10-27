using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Editor;
using Savepoint.App.Localization;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Editor;

public class ReconnectDialogModsFolderTests
{
    private static readonly UiFormatter _text = new(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: "windows", timeZone: TimeZoneInfo.Utc);

    private static ReconnectDialogViewModel Dialog(params RepoRole[] roles) =>
        new(new OrphanedHistory("old-game", roles, [new RecoverableProfile("profile/modded", "Modded")]), _text);

    [Fact]
    public void The_dialog_names_each_folder_the_old_snapshots_cover_in_the_order_they_are_shown()
    {
        var dialog = Dialog(RepoRole.Install, RepoRole.External, RepoRole.Mods);

        Assert.Equal("Found in the game folder, the mods folder, the saves folder.", dialog.FoundIn);
    }

    [Fact]
    public void Snapshots_found_only_in_the_mods_folder_say_so()
    {
        Assert.Equal("Found in the mods folder.", Dialog(RepoRole.Mods).FoundIn);
    }
}
