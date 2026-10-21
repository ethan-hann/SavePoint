using Avalonia.Animation;
using Avalonia.Headless.XUnit;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Theme;

public class ThemeServiceMotionTests
{
    private static List<TransitionBase> TransitionsIn(ThemeHarness harness) =>
        harness.Resources.Values.OfType<Transitions>().SelectMany(bundle => bundle).OfType<TransitionBase>().ToList();

    [AvaloniaFact]
    public void Transitions_are_published_at_the_spec_durations_by_default()
    {
        var harness = new ThemeHarness();

        var transitions = TransitionsIn(harness);

        Assert.NotEmpty(transitions);
        Assert.All(transitions, t => Assert.InRange(t.Duration.TotalMilliseconds, 150, 200));
        Assert.Equal(TimeSpan.FromMilliseconds(150), harness.Value("Md3MotionDurationShort"));
        Assert.Equal(TimeSpan.FromMilliseconds(200), harness.Value("Md3MotionDurationMedium"));
    }

    [AvaloniaFact]
    public void Reduce_motion_zeroes_every_transition_duration_and_the_duration_tokens()
    {
        var harness = new ThemeHarness();
        harness.Service.Apply(ThemePreference.Dark, AccentColorId.Blue);

        harness.Service.ReduceMotion = true;

        var transitions = TransitionsIn(harness);
        Assert.NotEmpty(transitions);
        Assert.All(transitions, t => Assert.Equal(TimeSpan.Zero, t.Duration));
        Assert.Equal(TimeSpan.Zero, harness.Value("Md3MotionDurationShort"));
        Assert.Equal(TimeSpan.Zero, harness.Value("Md3MotionDurationMedium"));
    }

    [AvaloniaFact]
    public void Turning_reduce_motion_off_restores_the_durations()
    {
        var harness = new ThemeHarness();
        harness.Service.ReduceMotion = true;

        harness.Service.ReduceMotion = false;

        Assert.NotEmpty(TransitionsIn(harness));
        Assert.All(TransitionsIn(harness), t => Assert.InRange(t.Duration.TotalMilliseconds, 150, 200));
        Assert.Equal(TimeSpan.FromMilliseconds(150), harness.Value("Md3MotionDurationShort"));
    }

    [AvaloniaFact]
    public void Reduce_motion_survives_a_theme_switch()
    {
        var harness = new ThemeHarness();
        harness.Service.ReduceMotion = true;

        harness.Service.Apply(ThemePreference.Light, AccentColorId.Amber);

        Assert.NotEmpty(TransitionsIn(harness));
        Assert.All(TransitionsIn(harness), t => Assert.Equal(TimeSpan.Zero, t.Duration));
    }

    [AvaloniaFact]
    public void Standard_easing_is_the_material_curve()
    {
        var harness = new ThemeHarness();

        var easing = Assert.IsType<Avalonia.Animation.Easings.SplineEasing>(harness.Value("Md3EasingStandard"));

        Assert.Equal(0.2, easing.X1);
        Assert.Equal(0.0, easing.Y1);
        Assert.Equal(0.0, easing.X2);
        Assert.Equal(1.0, easing.Y2);
    }
}
