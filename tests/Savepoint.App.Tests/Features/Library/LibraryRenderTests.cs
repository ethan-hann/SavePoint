using Avalonia.Headless.XUnit;
using Savepoint.App.Tests.Features.EndToEnd;

namespace Savepoint.App.Tests.Features.Library;

// The three dialogs of "Add games from your libraries" as they are drawn, in render-output/library/. Nothing here asserts on a
// picture, so the renders are only drawn on request: SAVEPOINT_RENDER=1.
public class LibraryRenderTests
{
    private static readonly string[] _states = ["library-picker", "library-picker-many", "library-confirm", "library-add-done", "first-run"];

    [AvaloniaFact]
    public async Task Writes_every_Library_render_on_request()
    {
        RenderOutput.SkipUnlessRequested();

        foreach (var state in _states)
        {
            using var app = await AppStates.OpenAsync(state);

            app.Render("library", state);
        }
    }
}
