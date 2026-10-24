using Savepoint.App.Features.Common.Accessibility;

namespace Savepoint.App.Tests.Features.Common;

public class ProgressMilestonesTests
{
    [Fact]
    public void The_start_of_finishing_is_worth_saying_once_until_the_job_starts_over()
    {
        var milestones = new ProgressMilestones();

        Assert.False(milestones.StartedFinishing(false));
        Assert.True(milestones.StartedFinishing(true));
        Assert.False(milestones.StartedFinishing(true));

        milestones.Reset();
        Assert.True(milestones.StartedFinishing(true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(24)]
    public void Nothing_is_worth_saying_before_the_first_quarter(int percent)
    {
        Assert.Null(new ProgressMilestones().Crossed(percent));
    }

    [Theory]
    [InlineData(25, 25)]
    [InlineData(49, 25)]
    [InlineData(50, 50)]
    [InlineData(74, 50)]
    [InlineData(75, 75)]
    [InlineData(100, 75)]
    public void Reaching_a_quarter_says_that_quarter(int percent, int expected)
    {
        Assert.Equal(expected, new ProgressMilestones().Crossed(percent));
    }

    [Fact]
    public void A_quarter_is_said_once_however_many_reports_stay_inside_it()
    {
        var milestones = new ProgressMilestones();

        Assert.Equal(25, milestones.Crossed(26));
        Assert.Null(milestones.Crossed(30));
        Assert.Null(milestones.Crossed(49));
        Assert.Equal(50, milestones.Crossed(50));
    }

    [Fact]
    public void A_jump_over_several_quarters_says_only_the_highest()
    {
        var milestones = new ProgressMilestones();

        Assert.Equal(75, milestones.Crossed(80));
        Assert.Null(milestones.Crossed(99));
    }

    [Fact]
    public void Progress_that_slips_back_says_nothing_new()
    {
        var milestones = new ProgressMilestones();
        milestones.Crossed(60);

        Assert.Null(milestones.Crossed(30));
        Assert.Null(milestones.Crossed(55));
    }

    [Fact]
    public void After_a_reset_the_quarters_are_said_again()
    {
        var milestones = new ProgressMilestones();
        milestones.Crossed(80);

        milestones.Reset();

        Assert.Equal(25, milestones.Crossed(30));
    }
}
