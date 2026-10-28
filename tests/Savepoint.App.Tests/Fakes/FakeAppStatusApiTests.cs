namespace Savepoint.App.Tests.Fakes;

// The startup choices for storage that cannot be reached are compared with the real Api by AppStatusContractTests. The one rule the
// story cannot play, since the real default folder cannot be made to hold history there, is kept here.
public class FakeAppStatusApiTests
{
    private const string _defaultRoot = @"C:\Users\you\AppData\Local\SavePoint\Storage";

    [Fact]
    public async Task Starting_empty_is_refused_while_the_default_folder_already_holds_history()
    {
        var scenario = ScenarioBuilder.Sample().WithDefaultStorageRoot(_defaultRoot).WithStorageHistoryIn(_defaultRoot);
        var backend = new FakeBackend(scenario).WithStorageGone();

        var result = await backend.AppStatus.StartWithEmptyStorageAsync();

        Assert.Equal("error.storage.defaultFolderHasHistory", result.Error?.MessageId);
        Assert.Equal(_defaultRoot, result.Error!.Args["path"]);
    }
}
