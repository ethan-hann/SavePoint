using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Theme.Gallery;

// Writes PNGs to render-output/theme/ at the repo root (gitignored) to compare with the previous render of the style gallery after a theme change.
public class StyleGalleryRenderTests
{
    public static string OutputFolder =>
        Path.Combine(ThemeTestSupport.RepoRoot(), "render-output", "theme");

    [AvaloniaFact]
    public void Writes_the_style_gallery_renders_on_request()
    {
        RenderOutput.SkipUnlessRequested();

        RenderGallery(ThemePreference.Dark, AccentColorId.Blue, "gallery-dark-blue", focusRing: true);
        RenderGallery(ThemePreference.Light, AccentColorId.Blue, "gallery-light-blue", focusRing: true);
        RenderGallery(ThemePreference.Dark, AccentColorId.Crimson, "gallery-dark-crimson", focusRing: true);
    }

    // Hover, press and field focus, with reduce motion on so the state layers are at their final opacity.
    [AvaloniaFact]
    public void Writes_the_style_gallery_state_renders_on_request()
    {
        RenderOutput.SkipUnlessRequested();

        foreach (var (preference, fileName) in new[] { (ThemePreference.Dark, "gallery-states-dark-blue"), (ThemePreference.Light, "gallery-states-light-blue") })
        {
            RenderGallery(preference, AccentColorId.Blue, fileName, focusRing: false, gallery =>
            {
                ((App)Application.Current!).Theme.ReduceMotion = true;
                SetPseudo(gallery.OutlinedWithIcon, ":pointerover");
                SetPseudo(gallery.CloseButton, ":pointerover");
                SetPseudo(gallery.OverflowButton, ":pressed");
                SetPseudo(gallery.SwitchOn, ":pointerover");
                gallery.PlainField.Focus(NavigationMethod.Tab);
            });
        }
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
