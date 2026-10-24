using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Library;
using Savepoint.App.Tests.Features.Shell;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Features.Library;

// The whole path from the shell's "Add games from your libraries" button to games in the library, over the fake backend.
public class ShellLibraryImportTests
{
    private static async Task<ShellRig> StartAsync(params LibraryGame[] games)
    {
        var backend = Savepoint.App.Tests.Fakes.FakeBackend.Empty();
        backend.State.Library = LibraryTestData.Scan(games);
        return await ShellRig.StartAsync(backend);
    }

    private static async Task<T> NextDialogAsync<T>(ShellRig rig, int index)
        where T : class
    {
        for (var wait = 0; wait < 500 && rig.Dialogs.Shown.Count <= index; wait++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(rig.Dialogs.Shown.Count > index, "The dialog never appeared.");
        return Assert.IsType<T>(rig.Dialogs.Shown[index]);
    }

    private static IReadOnlyList<string> AddCalls(ShellRig rig) =>
        [.. rig.Backend.Calls.To<IGamesApi>(nameof(IGamesApi.AddLibraryGameAsync)).Select(call => ((LibraryGame)call.Args[0]!).Key)];

    [Fact]
    public async Task The_first_run_screen_offers_the_entry_and_it_opens_the_checklist()
    {
        var rig = await StartAsync(LibraryTestData.Game("A", id: "1"));

        var pending = ((IAsyncRelayCommand)rig.Shell.AddFromLibrariesCommand).ExecuteAsync(null);
        var picker = await NextDialogAsync<LibraryPickerDialogViewModel>(rig, 0);
        picker.CancelCommand.Execute(null);
        await pending;

        Assert.Same(rig.Shell.AddFromLibrariesCommand, rig.Shell.FirstRun.AddFromLibrariesCommand);
        Assert.Single(picker.VisibleRows);
    }

    [Fact]
    public async Task Checking_games_confirming_once_and_waiting_adds_them_and_selects_the_first()
    {
        var rig = await StartAsync(LibraryTestData.Game("Alpha", id: "1"), LibraryTestData.Game("Beta", id: "2"));

        var pending = ((IAsyncRelayCommand)rig.Shell.AddFromLibrariesCommand).ExecuteAsync(null);
        var picker = await NextDialogAsync<LibraryPickerDialogViewModel>(rig, 0);
        picker.SelectAllCommand.Execute(null);
        picker.AddCommand.Execute(null);
        var confirm = await NextDialogAsync<LibraryConfirmDialogViewModel>(rig, 1);
        Assert.Equal(["Alpha", "Beta"], confirm.Entries.Select(entry => entry.Name));
        confirm.ConfirmCommand.Execute(null);
        var run = await NextDialogAsync<LibraryAddDialogViewModel>(rig, 2);
        for (var wait = 0; wait < 500 && !run.IsFinished; wait++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(run.IsFinished);
        run.CloseCommand.Execute(null);
        await pending;

        Assert.Equal(["steam:1", "steam:2"], AddCalls(rig));
        Assert.Equal(["Alpha", "Beta"], rig.Shell.Games.Select(row => row.Name).Order());
        Assert.Equal("Alpha", rig.Shell.SelectedGame?.Name);
        Assert.Equal(3, rig.Dialogs.Shown.Count);
    }

    [Fact]
    public async Task Backing_out_of_the_checklist_adds_nothing_and_asks_nothing_more()
    {
        var rig = await StartAsync(LibraryTestData.Game("Alpha", id: "1"));

        var pending = ((IAsyncRelayCommand)rig.Shell.AddFromLibrariesCommand).ExecuteAsync(null);
        var picker = await NextDialogAsync<LibraryPickerDialogViewModel>(rig, 0);
        picker.SelectAllCommand.Execute(null);
        picker.CancelCommand.Execute(null);
        await pending;

        Assert.Empty(AddCalls(rig));
        Assert.Single(rig.Dialogs.Shown);
    }

    [Fact]
    public async Task Declining_the_confirmation_adds_nothing()
    {
        var rig = await StartAsync(LibraryTestData.Game("Alpha", id: "1"));

        var pending = ((IAsyncRelayCommand)rig.Shell.AddFromLibrariesCommand).ExecuteAsync(null);
        var picker = await NextDialogAsync<LibraryPickerDialogViewModel>(rig, 0);
        picker.SelectAllCommand.Execute(null);
        picker.AddCommand.Execute(null);
        var confirm = await NextDialogAsync<LibraryConfirmDialogViewModel>(rig, 1);
        confirm.CancelCommand.Execute(null);
        await pending;

        Assert.Empty(AddCalls(rig));
        Assert.Empty(rig.Shell.Games);
        Assert.Equal(2, rig.Dialogs.Shown.Count);
    }
}
