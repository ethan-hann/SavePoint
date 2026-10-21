using Savepoint.App.Platform;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Platform;

public class NotificationPolicyTests
{
    private static readonly WindowActivity _inFront = new(IsActive: true, IsMinimized: false);
    private static readonly WindowActivity _behindOtherWindows = new(IsActive: false, IsMinimized: false);
    private static readonly WindowActivity _minimized = new(IsActive: false, IsMinimized: true);
    // Some platforms keep reporting a minimized window as the active one.
    private static readonly WindowActivity _minimizedButStillActive = new(IsActive: true, IsMinimized: true);

    public static TheoryData<JobKind> NotifiableKinds => new() { JobKind.Capture, JobKind.Compact, JobKind.RelocateStorage, JobKind.ClearData };

    [Theory]
    [MemberData(nameof(NotifiableKinds))]
    public void A_long_job_notifies_when_another_window_has_the_focus(JobKind kind)
    {
        Assert.True(NotificationPolicy.ShouldNotify(kind, _behindOtherWindows));
    }

    [Theory]
    [MemberData(nameof(NotifiableKinds))]
    public void A_long_job_notifies_when_the_window_is_minimized(JobKind kind)
    {
        Assert.True(NotificationPolicy.ShouldNotify(kind, _minimized));
    }

    [Theory]
    [MemberData(nameof(NotifiableKinds))]
    public void A_long_job_notifies_when_a_minimized_window_still_claims_to_be_active(JobKind kind)
    {
        Assert.True(NotificationPolicy.ShouldNotify(kind, _minimizedButStillActive));
    }

    [Theory]
    [MemberData(nameof(NotifiableKinds))]
    public void A_long_job_stays_quiet_while_the_user_is_looking_at_the_window(JobKind kind)
    {
        Assert.False(NotificationPolicy.ShouldNotify(kind, _inFront));
    }

    [Fact]
    public void A_launch_never_notifies_whatever_the_window_is_doing()
    {
        Assert.False(NotificationPolicy.ShouldNotify(JobKind.Launch, _inFront));
        Assert.False(NotificationPolicy.ShouldNotify(JobKind.Launch, _behindOtherWindows));
        Assert.False(NotificationPolicy.ShouldNotify(JobKind.Launch, _minimized));
        Assert.False(NotificationPolicy.ShouldNotify(JobKind.Launch, _minimizedButStillActive));
    }

    [Fact]
    public void Exactly_the_four_long_kinds_are_notifiable()
    {
        var notifiable = Enum.GetValues<JobKind>().Where(NotificationPolicy.IsNotifiable);

        Assert.Equal([JobKind.Capture, JobKind.Compact, JobKind.RelocateStorage, JobKind.ClearData], notifiable);
    }

    [Fact]
    public void The_decision_can_follow_a_live_window_source()
    {
        var source = new FakeActivitySource(_inFront);

        Assert.False(NotificationPolicy.ShouldNotify(JobKind.Capture, source));

        source.Current = _minimized;

        Assert.True(NotificationPolicy.ShouldNotify(JobKind.Capture, source));
    }

    private sealed class FakeActivitySource : IWindowActivitySource
    {
        public FakeActivitySource(WindowActivity current)
        {
            Current = current;
        }

        public WindowActivity Current { get; set; }

#pragma warning disable CS0067 // Part of the interface; this fake never raises it.
        public event EventHandler? Changed;
#pragma warning restore CS0067
    }
}
