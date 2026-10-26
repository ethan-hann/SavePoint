using Savepoint.App.Features.Editor;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Features.Editor;

// The note follows the Steam app id and install folder as they are typed: the store is asked again after a pause, and an answer to an
// earlier question never replaces the answer to a later one.
public class GameEditorCloudSavesFollowTests
{
    private const string _steamOnStart = "Steam Cloud is on for this game.";
    private const string _unknownStart = "If this game's store syncs cloud saves";

    private static FakeGamesApi Games(EditorRig rig) => Assert.IsType<FakeGamesApi>(rig.Backend.Games);

    private static IReadOnlyList<CloudSaveQuery> Queries(EditorRig rig) =>
        [.. rig.Calls(nameof(IGamesApi.GetCloudSavesAsync)).Select(call => call.Args[0]).OfType<CloudSaveQuery>()];

    private static Task<string> NoteStartingAsync(GameEditorViewModel editor, string start) =>
        EditorRig.Eventually(() => editor.CloudSavesNote.StartsWith(start, StringComparison.Ordinal) ? editor.CloudSavesNote : null);

    private static async Task<GameEditorViewModel> OpenAsync(EditorRig rig)
    {
        var editor = rig.Edit();
        await editor.LoadAsync();
        editor.CloudCheckDelay = (_, _) => Task.CompletedTask;
        return editor;
    }

    [Fact]
    public async Task Changing_the_install_folder_asks_the_store_about_the_new_folder()
    {
        var rig = EditorRig.Create();
        Games(rig).CloudSavesForForm = (_, _) => Task.FromResult(new CloudSaveInfo(CloudSaveState.On, CloudStore.Steam));
        var editor = await OpenAsync(rig);

        editor.InstallPath = @"D:\Games\Elsewhere";
        await NoteStartingAsync(editor, _steamOnStart);

        var query = Assert.Single(Queries(rig));
        Assert.Equal(@"D:\Games\Elsewhere", query.InstallPath);
    }

    [Fact]
    public async Task Changing_the_steam_app_id_asks_the_store_about_that_app_with_the_folder_as_it_is()
    {
        var rig = EditorRig.Create();
        Games(rig).CloudSavesForForm = (_, _) => Task.FromResult(new CloudSaveInfo(CloudSaveState.On, CloudStore.Steam));
        var editor = await OpenAsync(rig);
        var folder = editor.InstallPath;

        editor.SteamAppIdText = "730";
        await NoteStartingAsync(editor, _steamOnStart);

        var query = Assert.Single(Queries(rig));
        Assert.Equal(730, query.SteamAppId);
        Assert.Equal(folder, query.InstallPath);
    }

    [Fact]
    public async Task An_app_id_that_is_not_a_number_is_sent_as_none()
    {
        var rig = EditorRig.Create();
        var editor = await OpenAsync(rig);

        editor.SteamAppIdText = "abc";
        await EditorRig.Eventually(() => Queries(rig).Count > 0 ? Queries(rig) : null);

        Assert.Null(Queries(rig)[0].SteamAppId);
    }

    [Fact]
    public async Task Typing_in_bursts_asks_the_store_once_for_the_last_value()
    {
        var rig = EditorRig.Create();
        Games(rig).CloudSavesForForm = (_, _) => Task.FromResult(new CloudSaveInfo(CloudSaveState.On, CloudStore.Steam));
        var editor = await OpenAsync(rig);
        var delays = new List<TaskCompletionSource>();
        editor.CloudCheckDelay = (_, token) =>
        {
            var gate = new TaskCompletionSource();
            delays.Add(gate);
            return gate.Task.WaitAsync(token);
        };

        editor.InstallPath = @"D:\Games\A";
        editor.InstallPath = @"D:\Games\AB";
        editor.InstallPath = @"D:\Games\ABC";
        Assert.Empty(Queries(rig));
        Assert.Equal(3, delays.Count);
        delays[^1].SetResult();
        await NoteStartingAsync(editor, _steamOnStart);

        Assert.Equal(@"D:\Games\ABC", Assert.Single(Queries(rig)).InstallPath);
    }

    [Fact]
    public async Task A_late_answer_to_an_earlier_question_does_not_replace_a_newer_answer()
    {
        var rig = EditorRig.Create();
        var release = new TaskCompletionSource();
        Games(rig).CloudSavesForForm = async (query, _) =>
        {
            if (query.InstallPath == @"D:\Games\First")
            {
                await release.Task;
                return new CloudSaveInfo(CloudSaveState.On, CloudStore.Steam);
            }

            return new CloudSaveInfo(CloudSaveState.Unknown, CloudStore.None);
        };
        var editor = await OpenAsync(rig);

        editor.InstallPath = @"D:\Games\First";
        await EditorRig.Eventually(() => Queries(rig).Count == 1 ? Queries(rig) : null);
        editor.InstallPath = @"D:\Games\Second";
        await NoteStartingAsync(editor, _unknownStart);
        release.SetResult();
        await Task.Delay(150, TestContext.Current.CancellationToken);

        Assert.StartsWith(_unknownStart, editor.CloudSavesNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_first_load_answering_late_does_not_replace_the_answer_to_an_edit()
    {
        var rig = EditorRig.Create();
        var release = new TaskCompletionSource();
        Games(rig).CloudSaves[SampleScenario.SkyrimId] = new CloudSaveInfo(CloudSaveState.On, CloudStore.Steam);
        Games(rig).CloudSavesGate = () => release.Task;
        var editor = rig.Edit();
        editor.CloudCheckDelay = (_, _) => Task.CompletedTask;
        var loading = editor.LoadAsync();

        editor.InstallPath = @"D:\Games\Elsewhere";
        await NoteStartingAsync(editor, _unknownStart);
        release.SetResult();
        await loading;

        Assert.StartsWith(_unknownStart, editor.CloudSavesNote, StringComparison.Ordinal);
    }
}
