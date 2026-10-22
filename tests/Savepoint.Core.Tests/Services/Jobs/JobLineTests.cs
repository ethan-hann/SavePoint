using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Services.Jobs;

public class JobLineTests
{
    [Fact]
    public void Two_path_lines_are_equal_when_they_name_the_same_file()
    {
        Assert.Equal(JobLine.ForPath("Data/a.esp"), JobLine.ForPath("Data/a.esp"));
        Assert.NotEqual(JobLine.ForPath("Data/a.esp"), JobLine.ForPath("Data/b.esp"));
    }

    [Fact]
    public void Two_steps_are_equal_when_the_id_and_the_argument_values_are_equal_in_any_order()
    {
        var one = JobLine.ForStep(JobSteps.CaptureStart, ("profile", "Vanilla"), ("snapshot", "Session"));
        var other = JobLine.ForStep(JobSteps.CaptureStart, ("snapshot", "Session"), ("profile", "Vanilla"));

        Assert.Equal(one, other);
        Assert.Equal(one.GetHashCode(), other.GetHashCode());
        Assert.NotEqual(one, JobLine.ForStep(JobSteps.CaptureStart, ("profile", "Modded"), ("snapshot", "Session")));
        Assert.NotEqual(one, JobLine.ForStep(JobSteps.CaptureFolder, ("profile", "Vanilla"), ("snapshot", "Session")));
    }

    [Fact]
    public void A_path_line_never_equals_a_step()
    {
        Assert.NotEqual(JobLine.ForPath("Data/a.esp"), JobLine.ForStep("Data/a.esp"));
    }

    [Fact]
    public void An_enum_argument_becomes_its_camelCase_name_so_a_select_can_match_it()
    {
        var step = Assert.IsType<JobLine.Step>(JobLine.ForStep(JobSteps.CaptureFolder, ("folder", RepoRole.External), ("path", "C:/saves")));

        Assert.Equal("external", step.Args["folder"]);
    }

    [Fact]
    public void A_step_keeps_the_arguments_it_was_made_with_whatever_the_caller_does_to_its_dictionary_later()
    {
        var source = new Dictionary<string, object?> { ["game"] = "Skyrim" };
        var step = new JobLine.Step(JobSteps.CompactGame, source);

        source["game"] = "Fallout";

        Assert.Equal("Skyrim", step.Args["game"]);
    }

    [Fact]
    public void A_line_prints_what_it_says_for_logs_and_test_output()
    {
        Assert.Equal("Data/a.esp", JobLine.ForPath("Data/a.esp").ToString());
        var text = JobLine.ForStep(JobSteps.CompactFreed, ("game", "Skyrim"), ("bytes", 2048L)).ToString();

        Assert.Contains(JobSteps.CompactFreed, text, StringComparison.Ordinal);
        Assert.Contains("game=Skyrim", text, StringComparison.Ordinal);
        Assert.Contains("bytes=2048", text, StringComparison.Ordinal);
    }
}
