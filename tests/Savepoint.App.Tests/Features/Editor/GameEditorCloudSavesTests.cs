using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Features.Editor;

// The note about a store's cloud saves depends on what the store reports for the game; it is asked in the background.
public class GameEditorCloudSavesTests
{
    private const string _steamOn = "Steam Cloud is on for this game. Steam keeps saves that SavePoint changes, but brings back saves that SavePoint removed, so going back to before a save may not stick. If Steam asks which saves to keep, choose \"Local Save\".";
    private const string _gogOn = "Cloud saves are on for this game in GOG Galaxy. Galaxy keeps saves that SavePoint changes, but brings back saves that SavePoint removed, so going back to before a save may not stick. To avoid that, turn off cloud saves for this game in Galaxy.";

    private static FakeGamesApi Games(EditorRig rig) => Assert.IsType<FakeGamesApi>(rig.Backend.Games);

    private static async Task<string> NoteAsync(CloudSaveInfo info)
    {
        var rig = EditorRig.Create();
        Games(rig).CloudSaves[SampleScenario.SkyrimId] = info;

        var editor = rig.Edit();
        await editor.LoadAsync();

        Assert.Equal(editor.CloudSavesNote.Length > 0, editor.HasCloudSavesNote);
        return editor.CloudSavesNote;
    }

    [Fact]
    public async Task When_steam_keeps_the_saves_the_note_names_steam_and_what_to_choose()
    {
        Assert.Equal(_steamOn, await NoteAsync(new CloudSaveInfo(CloudSaveState.On, CloudStore.Steam)));
    }

    [Fact]
    public async Task When_gog_galaxy_keeps_the_saves_the_note_names_galaxy_and_how_to_turn_it_off()
    {
        Assert.Equal(_gogOn, await NoteAsync(new CloudSaveInfo(CloudSaveState.On, CloudStore.Gog)));
    }

    [Theory]
    [InlineData(CloudStore.Steam)]
    [InlineData(CloudStore.Gog)]
    public async Task When_the_store_does_not_keep_the_saves_there_is_no_note(CloudStore store)
    {
        Assert.Equal(string.Empty, await NoteAsync(new CloudSaveInfo(CloudSaveState.Off, store)));
    }

    [Theory]
    [InlineData(CloudStore.None)]
    [InlineData(CloudStore.Steam)]
    [InlineData(CloudStore.Gog)]
    public async Task When_the_state_is_unknown_there_is_no_warning(CloudStore store)
    {
        Assert.Equal(string.Empty, await NoteAsync(new CloudSaveInfo(CloudSaveState.Unknown, store)));
    }

    [Fact]
    public async Task The_note_stays_hidden_while_the_check_is_pending_and_the_load_finishes_when_it_answers()
    {
        var rig = EditorRig.Create();
        var release = new TaskCompletionSource();
        Games(rig).CloudSaves[SampleScenario.SkyrimId] = new CloudSaveInfo(CloudSaveState.On, CloudStore.Steam);
        Games(rig).CloudSavesGate = () => release.Task;

        var editor = rig.Edit();
        var loading = editor.LoadAsync();

        Assert.False(editor.HasCloudSavesNote);
        Assert.False(loading.IsCompleted);

        release.SetResult();
        await loading;

        Assert.True(editor.HasCloudSavesNote);
    }
}
