using Savepoint.App.Features.Common.Navigation;
using Savepoint.App.Features.Settings;
using Savepoint.App.Features.Shell;

namespace Savepoint.App.Composition;

public static partial class FeatureComposition
{
    /// <summary>The Settings screen's factory.</summary>
    /// <param name="context">Shared App services.</param>
    /// <param name="editor">Builds the game editor pane; Settings never opens it.</param>
    /// <param name="environment">What Settings asks of the machine; the running app's when null.</param>
    public static IScreenFactory? SettingsScreen(FeatureContext context, IGameEditorFactory? editor, SettingsEnvironment? environment = null)
    {
        _ = editor;
        return new SettingsScreenFactory(context, environment ?? SettingsEnvironment.ForThisApp());
    }
}
