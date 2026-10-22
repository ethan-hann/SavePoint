using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Theme.Gallery;

// Writes PNGs to render-output/theme/ at the repo root (gitignored) for side-by-side review against the mockups.
public class StyleGalleryRenderTests
{
    public static string OutputFolder =>
        Path.Combine(ThemeTestSupport.RepoRoot(), "render-output", "theme");

    [AvaloniaTheory]
    [InlineData(ThemePreference.Dark, AccentColorId.Blue, "gallery-dark-blue")]
    [InlineData(ThemePreference.Light, AccentColorId.Blue, "gallery-light-blue")]
    [InlineData(ThemePreference.Dark, AccentColorId.Crimson, "gallery-dark-crimson")]
    public void Gallery_renders_to_a_png(ThemePreference preference, AccentColorId accent, string fileName)
    {
        var path = RenderGallery(preference, accent, fileName, focusRing: true);

        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 20_000, "The render looks empty.");
    }

    // Hover, press and field focus, with reduce motion on so the state layers are at their final opacity.
    [AvaloniaTheory]
    [InlineData(ThemePreference.Dark, "gallery-states-dark-blue")]
    [InlineData(ThemePreference.Light, "gallery-states-light-blue")]
    public void Gallery_states_render_to_a_png(ThemePreference preference, string fileName)
    {
        var path = RenderGallery(preference, AccentColorId.Blue, fileName, focusRing: false, gallery =>
        {
            ((App)Application.Current!).Theme.ReduceMotion = true;
            SetPseudo(gallery.OutlinedWithIcon, ":pointerover");
            SetPseudo(gallery.CloseButton, ":pointerover");
            SetPseudo(gallery.OverflowButton, ":pressed");
            SetPseudo(gallery.SwitchOn, ":pointerover");
            gallery.PlainField.Focus(NavigationMethod.Tab);
        });

        Assert.True(File.Exists(path));
    }

    internal static string RenderGallery(ThemePreference preference, AccentColorId accent, string fileName, bool focusRing, Action<StyleGallery>? configure = null)
    {
        var gallery = new StyleGallery();
        using var host = new ThemeHost(gallery, preference, accent, 1040, 1200);
        configure?.Invoke(gallery);
        if (focusRing)
        {
            gallery.FocusTarget.Focus(NavigationMethod.Tab);
        }

        Dispatcher.UIThread.RunJobs();
        return ThemeTestSupport.SaveFrame(host.Window, Path.Combine(OutputFolder, fileName + ".png"));
    }

    private static void SetPseudo(StyledElement element, string name)
    {
        ((IPseudoClasses)element.Classes).Set(name, true);
        Dispatcher.UIThread.RunJobs();
    }
}
