using Savepoint.App.Features.Library;
using Savepoint.App.Tests.Fakes;

namespace Savepoint.App.Tests.Features.Library;

public class LibraryImportFlowTests
{
    [Fact]
    public async Task Backing_out_while_the_scan_is_still_running_ends_the_flow_at_once()
    {
        var backend = FakeBackend.Empty();
        var never = new TaskCompletionSource();
        // A drive that does not answer: the scan ignores its token and does not come back.
        backend.Games.ScanGate = _ => never.Task;
        var dialogs = new FakeDialogs();
        var flow = new LibraryImportFlow(backend.Games, dialogs, LibraryTestData.Text);

        var running = flow.RunAsync();
        var picker = Assert.IsType<LibraryPickerDialogViewModel>(Assert.Single(dialogs.Shown));
        picker.CancelCommand.Execute(null);
        var summary = await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Null(summary);
        Assert.True(picker.IsLoading);
    }
}
