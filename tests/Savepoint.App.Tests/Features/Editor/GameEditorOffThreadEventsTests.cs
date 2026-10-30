using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Common;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Common;
using Savepoint.Core.Api;
using static Savepoint.App.Tests.Fakes.SampleScenario;

namespace Savepoint.App.Tests.Features.Editor;

// The artwork lookup reports from its own thread. The editor has to follow it on the UI thread while it is open.
public class GameEditorOffThreadEventsTests
{
    [AvaloniaFact]
    public async Task Steam_artwork_that_arrives_off_the_UI_thread_shows_in_the_open_editor_and_changes_nothing_off_that_thread()
    {
        var backend = FakeBackend.Sample().WithDelivery(EventDelivery.Background);
        var rig = EditorRig.Create(backend, dispatcher: new AvaloniaUiDispatcher());
        var shared = rig.Shell.Artwork.For(SkyrimId);
        shared.NotifyOnScreen();
        var editor = rig.Edit();
        var writes = new UiThreadWrites().Watch(editor).Watch(editor.ArtworkPreview).Watch(shared);
        Assert.True(await UiWait.UntilAsync(() => backend.Calls.To<IArtworkApi>(nameof(IArtworkApi.GetAsync)).Count > 0));

        backend.Artwork.ScriptSteamArrives(SkyrimId, "/art/wide.jpg", "/art/square.jpg");
        await backend.WhenEventsDeliveredAsync();

        Assert.True(await UiWait.UntilAsync(() => editor.ArtworkSourceText == "From Steam"));
        Assert.Equal("/art/wide.jpg", editor.ArtworkPreview.WidePath);
        Assert.Empty(writes.Offenders);
    }
}
