using static Savepoint.App.Tests.Fakes.FakeAssert;

namespace Savepoint.App.Tests.Fakes;

// The two startup choices for storage that cannot be reached refuse in the fake as they do in the real Api, so a screen test sees the
// refusals without scripting them.
public class FakeAppStatusApiTests
{
    private const string _history = @"G:\SavePoint";
    private const string _defaultRoot = @"C:\Users\you\AppData\Local\SavePoint\Storage";

    private static FakeBackend Backend(params string[] foldersWithHistory) =>
        new(ScenarioBuilder.Mockup().WithDefaultStorageRoot(_defaultRoot).WithStorageHistoryIn(foldersWithHistory));

    [Fact]
    public async Task A_folder_that_holds_no_history_of_the_games_is_refused_and_nothing_changes()
    {
        var backend = Backend(_history);
        var before = Ok(await backend.AppStatus.CheckAsync(TestContext.Current.CancellationToken)).Storage;

        var result = await backend.AppStatus.UseStorageFolderAsync(@"G:\Elsewhere", TestContext.Current.CancellationToken);

        Assert.Equal("error.storage.noHistoryHere", result.Error?.MessageId);
        Assert.Equal(@"G:\Elsewhere", result.Error!.Args["path"]);
        Assert.Equal(before, Ok(await backend.AppStatus.CheckAsync(TestContext.Current.CancellationToken)).Storage);
    }

    [Fact]
    public async Task A_folder_that_holds_the_history_is_taken_and_the_storage_is_reachable_there()
    {
        var backend = Backend(_history);

        var result = await backend.AppStatus.UseStorageFolderAsync(_history, TestContext.Current.CancellationToken);

        Assert.Equal(_history, Ok(result));
        var storage = Ok(await backend.AppStatus.CheckAsync(TestContext.Current.CancellationToken)).Storage!;
        Assert.True(storage.Reachable);
        Assert.Equal(_history, storage.Root);
    }

    [Fact]
    public async Task Starting_empty_is_refused_while_the_default_folder_already_holds_history()
    {
        var backend = Backend(_defaultRoot);

        var result = await backend.AppStatus.StartWithEmptyStorageAsync(TestContext.Current.CancellationToken);

        Assert.Equal("error.storage.defaultFolderHasHistory", result.Error?.MessageId);
        Assert.Equal(_defaultRoot, result.Error!.Args["path"]);
    }

    [Fact]
    public async Task Starting_empty_uses_the_default_folder_when_it_holds_nothing()
    {
        var backend = Backend(_history);

        var result = await backend.AppStatus.StartWithEmptyStorageAsync(TestContext.Current.CancellationToken);

        Assert.Equal(_defaultRoot, Ok(result));
        Assert.Equal(_defaultRoot, Ok(await backend.AppStatus.CheckAsync(TestContext.Current.CancellationToken)).Storage!.Root);
    }
}
