using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;

namespace Savepoint.App.Theme;

// Shared transition bundles that styles apply with DynamicResource; reduce-motion zeroes their durations in place.
internal sealed class MotionResources
{
    public const string DurationShortKey = "Md3MotionDurationShort";
    public const string DurationMediumKey = "Md3MotionDurationMedium";
    public const string EasingKey = "Md3EasingStandard";

    // State-layer and selection fades.
    public const string OpacityShortKey = "Md3TransitionsOpacityShort";

    // Tooltip and overlay fades.
    public const string OpacityMediumKey = "Md3TransitionsOpacityMedium";

    // The switch knob slide (ToggleSwitch.KnobTransitions).
    public const string KnobKey = "Md3TransitionsKnob";

    private static readonly TimeSpan _short = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan _medium = TimeSpan.FromMilliseconds(200);

    private readonly IResourceDictionary _resources;
    private readonly List<(TransitionBase Transition, TimeSpan Normal)> _transitions = [];

    public MotionResources(IResourceDictionary resources)
    {
        _resources = resources;
        // MD3 standard easing: cubic-bezier(0.2, 0, 0, 1).
        var easing = new SplineEasing(0.2, 0.0, 0.0, 1.0);
        resources[EasingKey] = easing;
        resources[OpacityShortKey] = Bundle(Fade(Visual.OpacityProperty, _short, easing));
        resources[OpacityMediumKey] = Bundle(Fade(Visual.OpacityProperty, _medium, easing));
        resources[KnobKey] = Bundle(Fade(Canvas.LeftProperty, _medium, easing));
        Apply(reduceMotion: false);
    }

    public void Apply(bool reduceMotion)
    {
        foreach (var (transition, normal) in _transitions)
        {
            transition.Duration = reduceMotion ? TimeSpan.Zero : normal;
        }

        _resources[DurationShortKey] = reduceMotion ? TimeSpan.Zero : _short;
        _resources[DurationMediumKey] = reduceMotion ? TimeSpan.Zero : _medium;
    }

    private static DoubleTransition Fade(AvaloniaProperty<double> property, TimeSpan duration, Easing easing) =>
        new() { Property = property, Duration = duration, Easing = easing };

    private Transitions Bundle(DoubleTransition transition)
    {
        _transitions.Add((transition, transition.Duration));
        return [transition];
    }
}
