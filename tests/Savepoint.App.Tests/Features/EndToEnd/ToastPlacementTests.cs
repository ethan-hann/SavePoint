using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Features.EndToEnd;

// A toast must never sit on top of something a person needs to press, whatever the window size.
public class ToastPlacementTests
{
    [AvaloniaTheory]
    [InlineData(900.0, 660.0)]
    [InlineData(1040.0, 660.0)]
    public async Task Toasts_never_cover_the_actions_of_an_error_card(double width, double height)
    {
        using var app = await ComposedApp.StartWithViewAsync(width: width, height: height);
        app.Backend.FailNext<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync), MockupScenario.FileInUseError);
        _ = app.Games.Profiles.Single(row => row.Id == MockupScenario.ModdedId).PlayCommand.ExecuteAsync(null);
        app.Toasts.Show(new ToastMessage("Could not save Skyrim Special Edition.", ToastKind.Error, "There is not enough free disk space to save this profile right now."));
        app.Toasts.Show(new ToastMessage("Only part of Satisfactory was saved.", ToastKind.Warning, "A file is in use."));
        app.Settle();
        app.Render("toasts", $"error-card-with-toasts-{width}x{height}");

        var actions = app.All<ErrorCardView>().Single().GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).ToList();
        var toasts = app.Find<ItemsControl>("ToastList").GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("panel")).ToList();

        Assert.NotEmpty(actions);
        Assert.Equal(2, toasts.Count);
        foreach (var action in actions)
        {
            foreach (var toast in toasts)
            {
                Assert.False(
                    BoundsIn(app, action).Intersects(BoundsIn(app, toast)),
                    $"A toast at {BoundsIn(app, toast)} covers the card button '{action.Content}' at {BoundsIn(app, action)} in a {width}x{height} window.");
            }
        }
    }

    [AvaloniaTheory]
    [InlineData(900.0, 660.0)]
    [InlineData(1040.0, 660.0)]
    public async Task The_toast_stack_sits_outside_the_page_it_reports_on(double width, double height)
    {
        using var app = await ComposedApp.StartWithViewAsync(width: width, height: height);
        app.Toasts.Show(new ToastMessage("Could not save Skyrim Special Edition.", ToastKind.Error));
        app.Settle();

        var page = BoundsIn(app, app.Find<ContentControl>("Page"));
        var stack = BoundsIn(app, app.Find<ItemsControl>("ToastList"));

        Assert.False(page.Intersects(stack), $"The toast stack {stack} overlaps the page {page}.");
    }

    private static Rect BoundsIn(ComposedApp app, Visual visual) =>
        new(visual.TranslatePoint(default, app.Window) ?? default, visual.Bounds.Size);
}
