using Savepoint.App.Features.Library;

namespace Savepoint.App.Tests.Features.Library;

public class LibraryConfirmDialogViewModelTests
{
    private static LibraryConfirmDialogViewModel Dialog(params Savepoint.Core.Api.LibraryGame[] games) => new(games, LibraryTestData.Text);

    [Fact]
    public void It_names_every_game_with_its_install_folder_and_its_saves_folder()
    {
        var skyrim = LibraryTestData.Game("Skyrim", saves: @"C:\Users\you\Documents\My Games\Skyrim\Saves");
        var fallout = LibraryTestData.Game("Fallout 4");

        var dialog = Dialog(skyrim, fallout);

        Assert.Equal(["Skyrim", "Fallout 4"], dialog.Entries.Select(entry => entry.Name));
        Assert.Equal([@"C:\Games\Skyrim", @"C:\Games\Fallout 4"], dialog.Entries.Select(entry => entry.InstallPath));
        Assert.Equal([@"Game folder: C:\Games\Skyrim", @"Game folder: C:\Games\Fallout 4"], dialog.Entries.Select(entry => entry.InstallLine));
        Assert.Equal(@"Saves folder: C:\Users\you\Documents\My Games\Skyrim\Saves", dialog.Entries[0].SavesLine);
        Assert.Equal("No saves folder yet", dialog.Entries[1].SavesLine);
    }

    [Fact]
    public void A_saves_folder_that_does_not_exist_yet_is_not_named_because_it_is_left_out()
    {
        var dialog = Dialog(LibraryTestData.Game("Skyrim", saves: @"C:\Docs\Saves", savesExist: false));

        Assert.Equal("No saves folder yet", dialog.Entries.Single().SavesLine);
    }

    [Fact]
    public void The_title_counts_the_games_and_reads_in_the_singular_for_one()
    {
        Assert.Equal("Add this game?", Dialog(LibraryTestData.Game("A")).Title);
        Assert.Equal("Add these 3 games?", Dialog(LibraryTestData.Game("A"), LibraryTestData.Game("B"), LibraryTestData.Game("C")).Title);
    }

    [Fact]
    public void The_message_says_what_SavePoint_will_do_to_the_folders_in_plain_words()
    {
        var dialog = Dialog(LibraryTestData.Game("A"));

        // The same words as the confirmation for one game, so the two never promise different things.
        Assert.Contains("takes snapshots of everything in these folders", dialog.Message);
        Assert.Contains("small file in each folder", dialog.Message);
        Assert.Contains("puts files back and removes files", dialog.Message);
        Assert.Contains("No snapshot is taken now", dialog.Message);
        Assert.DoesNotContain("for now", dialog.Message);
        Assert.Equal(dialog.Message, dialog.Description);
        Assert.True(dialog.PinsFooter);
        Assert.Equal("Confirm and add", dialog.ConfirmLabel);
    }

    [Fact]
    public async Task Confirming_answers_true_and_cancelling_or_dismissing_answers_false()
    {
        var confirmed = Dialog(LibraryTestData.Game("A"));
        var cancelled = Dialog(LibraryTestData.Game("A"));
        var dismissed = Dialog(LibraryTestData.Game("A"));

        confirmed.ConfirmCommand.Execute(null);
        cancelled.CancelCommand.Execute(null);
        dismissed.RequestDismiss();

        Assert.True(await confirmed.Result);
        Assert.False(await cancelled.Result);
        Assert.False(await dismissed.Result);
    }
}
