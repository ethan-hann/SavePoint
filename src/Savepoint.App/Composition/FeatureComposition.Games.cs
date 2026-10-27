using Savepoint.App.Features.Common.Navigation;
using Savepoint.App.Features.Games;
using Savepoint.App.Features.Shell;
using Savepoint.App.Hosting;
using Savepoint.App.Platform;

namespace Savepoint.App.Composition;

public static partial class FeatureComposition
{
    /// <summary>The Games screen's factory.</summary>
    /// <param name="context">Shared App services.</param>
    /// <param name="editor">Builds the game editor pane.</param>
    public static IScreenFactory GamesScreen(FeatureContext context, IGameEditorFactory editor) =>
        new GamesScreenFactory(new GamesDependencies(
            context.Services.Games,
            context.Services.Profiles,
            context.Services.Jobs,
            context.Dialogs,
            context.Text,
            context.Toasts,
            context.Dispatcher,
            context.Clock,
            editor,
            context.Services.FileRevealer ?? new FileManagerRevealer(FileManager.CreateForCurrentOs())));
}
