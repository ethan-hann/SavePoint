using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Editor;

// The note about a store's cloud saves depends on what the store reports for the game; it is asked in the background.
public class GameEditorCloudSavesTests
{
    private static FakeGamesApi Games(EditorRig rig) => Assert.IsType<FakeGamesApi>(rig.Backend.Games);

    [Fact]
    public async Task When_the_store_keeps_the_saves_the_note_says_what_happens_and_what_to_choose()
    {
        var rig = EditorRig.Create();
        Games(rig).CloudSaves[SampleScenario.SkyrimId] = CloudSaveState.On;

        var editor = rig.Edit();
        await editor.LoadAsync();

        Assert.True(editor.HasCloudSavesNote);
        Assert.Equal(
            "The store keeps the saves SavePoint puts back and uploads them when you quit. If you also play on another PC, Steam may ask which saves to keep: choose \"Local Save\" to keep these, because \"Cloud Save\" replaces them.",
            editor.CloudSavesNote);
    }

    [Fact]
    public async Task When_the_store_does_not_keep_the_saves_there_is_no_note()
    {
        var rig = EditorRig.Create();
        Games(rig).CloudSaves[SampleScenario.SkyrimId] = CloudSaveState.Off;

        var editor = rig.Edit();
        await editor.LoadAsync();

        Assert.False(editor.HasCloudSavesNote);
        Assert.Equal(string.Empty, editor.CloudSavesNote);
    }

    [Fact]
    public async Task When_the_state_is_unknown_the_note_gives_softer_advice()
    {
        var rig = EditorRig.Create();
        Games(rig).CloudSaves[SampleScenario.SkyrimId] = CloudSaveState.Unknown;

        var editor = rig.Edit();
        await editor.LoadAsync();

        Assert.StartsWith("SavePoint can't tell if this game's store keeps cloud saves.", editor.CloudSavesNote);
        Assert.DoesNotContain("Local Save", editor.CloudSavesNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_check_gives_the_unknown_note()
    {
        var rig = EditorRig.Create();
        rig.Backend.FailNext<IGamesApi>(nameof(IGamesApi.GetCloudSavesAsync), ErrorInfo.From(new GameNotFoundException(SampleScenario.SkyrimId)));

        var editor = rig.Edit();
        await editor.LoadAsync();

        Assert.StartsWith("SavePoint can't tell", editor.CloudSavesNote);
    }

    [Fact]
    public void A_game_that_is_not_saved_yet_gets_the_unknown_note_without_asking()
    {
        var rig = EditorRig.Create();

        var editor = rig.New();

        Assert.StartsWith("SavePoint can't tell", editor.CloudSavesNote);
        Assert.Empty(rig.Calls(nameof(IGamesApi.GetCloudSavesAsync)));
    }

    [Fact]
    public async Task The_note_is_hidden_until_the_answer_arrives_and_the_load_does_not_wait_for_it_on_the_caller()
    {
        var rig = EditorRig.Create();
        var release = new TaskCompletionSource();
        Games(rig).CloudSaves[SampleScenario.SkyrimId] = CloudSaveState.On;
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
