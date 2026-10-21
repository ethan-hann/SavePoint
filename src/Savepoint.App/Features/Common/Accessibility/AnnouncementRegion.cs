using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace Savepoint.App.Features.Common.Accessibility;

/// <summary>
/// A visually hidden live region: whatever is put into <see cref="Message"/> is exposed to assistive technology as a
/// polite announcement, without taking space or focus. Place one near the part of the screen whose milestones
/// should be spoken (a job's progress, a toast) and bind <see cref="Message"/> to a view model string. To make
/// visible text a live region instead, use <see cref="Mark"/>.
/// </summary>
public sealed class AnnouncementRegion : TextBlock
{
    /// <summary>The text announced when it changes.</summary>
    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<AnnouncementRegion, string?>(nameof(Message));

    static AnnouncementRegion()
    {
        MessageProperty.Changed.AddClassHandler<AnnouncementRegion>((region, e) => region.Apply(e.NewValue as string));
    }

    /// <summary>Creates a polite, hidden region.</summary>
    public AnnouncementRegion()
    {
        Mark(this);
        Opacity = 0;
        Width = 1;
        Height = 1;
        IsHitTestVisible = false;
        Focusable = false;
    }

    /// <summary>The text announced when it changes; null or empty announces nothing.</summary>
    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(TextBlock);

    /// <summary>
    /// Makes a control a live region: assistive technology announces changes to its text or name. Polite waits for
    /// the user to finish what they are doing; assertive interrupts and is for things the user must know now.
    /// </summary>
    public static void Mark(Control control, AutomationLiveSetting setting = AutomationLiveSetting.Polite) =>
        AutomationProperties.SetLiveSetting(control, setting);

    /// <summary>Announces a message, including one identical to the previous message.</summary>
    public void Announce(string message)
    {
        Message = string.Empty;
        Message = message;
    }

    private void Apply(string? message)
    {
        Text = message;
        AutomationProperties.SetName(this, message);
    }
}
