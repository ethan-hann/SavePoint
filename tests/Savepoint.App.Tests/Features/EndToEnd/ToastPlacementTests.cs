using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

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
        app.Backend.FailNext<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync), SampleScenario.FileInUseError);
        _ = app.Games.Profiles.Single(row => row.Id == SampleScenario.ModdedId).PlayCommand.ExecuteAsync(null);
        app.Toasts.Show(new ToastMessage("Could not save Skyrim Special Edition.", ToastKind.Error, "There is not enough free disk space to save this profile right now."));
        app.Toasts.Show(new ToastMessage("Only part of Satisfactory was saved.", ToastKind.Warning, "A file is in use."));
        app.Settle();
        app.RenderIfRequested("toasts", $"error-card-with-toasts-{width}x{height}");

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

    public static TheoryData<string, double, double> LayoutsAtAuditedSizes()
    {
        var data = new TheoryData<string, double, double>();
        foreach (var layout in new[] { ShellLayout.Sidebar, ShellLayout.Library })
        {
            data.Add(layout.ToString(), 1040, 660);
            data.Add(layout.ToString(), 900, 660);
            foreach (var (_, width, height) in LayoutAudit.ScaledScreens)
            {
                data.Add(layout.ToString(), width, height);
            }

            data.Add(layout.ToString(), 640, 420);
        }

        return data;
    }

    [AvaloniaTheory]
    [MemberData(nameof(LayoutsAtAuditedSizes))]
    public async Task A_toast_floats_over_the_shell_and_leaves_its_layout_alone(string layoutName, double width, double height)
    {
        var layout = Enum.Parse<ShellLayout>(layoutName);
        var backend = new FakeBackend(ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings with { Layout = layout }));
        using var app = await ComposedApp.StartWithViewAsync(backend, layout: layout, width: width, height: height);
        app.Settle();
        var names = new[] { "ShellRoot", "Rail", "Page", "ContentRegion" };
        var before = names.Select(name => BoundsIn(app, app.Find<Control>(name))).ToList();

        app.Toasts.Show(new ToastMessage("Vanilla is loaded. Grounded was not started.", ToastKind.Success));
        app.Settle();

        Assert.True(app.Find<ItemsControl>("ToastList").GetVisualDescendants().OfType<Border>().Any(border => border.Classes.Contains("panel")), "No toast is showing.");
        var after = names.Select(name => BoundsIn(app, app.Find<Control>(name))).ToList();
        for (var index = 0; index < names.Length; index++)
        {
            Assert.True(before[index] == after[index], $"{names[index]} moved from {before[index]} to {after[index]} when a toast showed ({layoutName}, {width}x{height}).");
        }

        var toast = BoundsIn(app, app.Find<Grid>("Strip"));
        Assert.True(new Rect(app.Window.ClientSize).Contains(toast), $"The toasts at {toast} are not inside the {app.Window.ClientSize} window.");
        Assert.True(BoundsIn(app, app.Find<ContentControl>("Page")).Contains(toast), "The toasts float over the page, not beside it.");

        app.Toasts.DismissAll();
        app.Settle();
        Assert.True(before.SequenceEqual(names.Select(name => BoundsIn(app, app.Find<Control>(name)))), "The shell moved when the toast went.");
    }

    // The first-run tour's card is placed in the page area, which the toasts float over: the card must stay clear of the stack at every step.
    [AvaloniaTheory]
    [InlineData(1040.0, 660.0)]
    [InlineData(640.0, 420.0)]
    public async Task The_tour_card_stays_clear_of_the_toasts(double width, double height)
    {
        var backend = new FakeBackend(ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings with { TourSeen = false }));
        using var app = await ComposedApp.StartWithViewAsync(backend, width: width, height: height);
        app.Toasts.Show(new ToastMessage("Could not save Skyrim Special Edition.", ToastKind.Error, "There is not enough free disk space to save this profile right now."));
        app.Toasts.Show(new ToastMessage("Only part of Satisfactory was saved.", ToastKind.Warning, "A file is in use."));
        app.Settle();

        var steps = 0;
        while (app.Shell.Tour.IsActive && steps++ < 10)
        {
            var card = app.All<Border>().Single(border => border.Name == "TourCard");
            Assert.True(card.IsEffectivelyVisible, $"The tour card is not showing at step {steps}.");
            var strip = BoundsIn(app, app.Find<Grid>("Strip"));
            Assert.False(BoundsIn(app, card).Intersects(strip), $"The tour card at {BoundsIn(app, card)} is under the toasts at {strip} at step {steps} ({width}x{height}).");
            app.Shell.Tour.NextCommand.Execute(null);
            app.Settle();
        }

        Assert.True(steps > 1, "The tour did not run.");
    }

    private static Rect BoundsIn(ComposedApp app, Visual visual) =>
        new(visual.TranslatePoint(default, app.Window) ?? default, visual.Bounds.Size);
}
