using Savepoint.App.Features.Library;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Features.Library;

// A library game whose template names a mods folder outside the game folder: the picker row, the confirmation and the result say so,
// and a folder that does not exist yet is left out the way a missing saves folder is.
public class LibraryModsFolderTests
{
    private const string _mods = @"C:\Users\you\AppData\Roaming\7DaysToDie\Mods";

    private static (LibraryAddDialogViewModel Dialog, FakeBackend Backend) Make(params LibraryGame[] games)
    {
        var backend = FakeBackend.Empty();
        backend.State.Library = LibraryTestData.Scan(games);
        return (new LibraryAddDialogViewModel(backend.Games, games, LibraryTestData.Text), backend);
    }

    [Fact]
    public void A_picker_row_names_a_mods_folder_that_exists()
    {
        var row = new LibraryRowViewModel(LibraryTestData.Game("7 Days", mods: _mods), LibraryTestData.Text);

        Assert.Equal($"Mods folder: {_mods}", row.ModsLine);
    }

    [Fact]
    public void A_picker_row_says_a_mods_folder_that_is_not_there_yet_is_left_out_for_now()
    {
        var row = new LibraryRowViewModel(LibraryTestData.Game("7 Days", mods: _mods, modsExist: false), LibraryTestData.Text);

        Assert.Equal($"Mods folder: {_mods}. It doesn't exist yet, so it's left out for now.", row.ModsLine);
    }

    [Fact]
    public void A_picker_row_for_a_game_whose_template_names_no_mods_folder_has_no_mods_line()
    {
        var row = new LibraryRowViewModel(LibraryTestData.Game("Skyrim", saves: @"C:\Saves"), LibraryTestData.Text);

        Assert.Equal(string.Empty, row.ModsLine);
    }

    [Fact]
    public void The_confirmation_names_a_mods_folder_that_will_be_managed_and_not_one_that_is_left_out()
    {
        var there = LibraryTestData.Game("7 Days", mods: _mods);
        var missing = LibraryTestData.Game("Other", mods: _mods, modsExist: false);
        var plain = LibraryTestData.Game("Plain");

        var dialog = new LibraryConfirmDialogViewModel([there, missing, plain], LibraryTestData.Text);

        Assert.Equal([$"Mods folder: {_mods}", string.Empty, string.Empty], dialog.Entries.Select(entry => entry.ModsLine));
    }

    [Fact]
    public async Task A_game_with_an_existing_mods_folder_is_added_with_it()
    {
        var (dialog, backend) = Make(LibraryTestData.Game("7 Days", id: "1", mods: _mods));

        await dialog.RunAsync();

        var stored = Assert.Single((await backend.Games.ListAsync(TestContext.Current.CancellationToken)).Value!);
        Assert.Equal(_mods, stored.Mods?.Path);
        Assert.DoesNotContain(dialog.ResultLines, line => line.Text.Contains("mods folder", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_game_whose_mods_folder_is_not_there_yet_is_added_without_it_and_the_result_says_so()
    {
        var (dialog, backend) = Make(LibraryTestData.Game("7 Days", id: "1", mods: _mods, modsExist: false));

        await dialog.RunAsync();

        var stored = Assert.Single((await backend.Games.ListAsync(TestContext.Current.CancellationToken)).Value!);
        Assert.Null(stored.Mods);
        var line = Assert.Single(dialog.ResultLines, l => l.Kind == LibraryResultKind.Notice);
        Assert.Equal($"7 Days: added without a mods folder, because {_mods} doesn't exist yet. Add it in the game's settings once your mods are installed.", line.Text);
    }
}
