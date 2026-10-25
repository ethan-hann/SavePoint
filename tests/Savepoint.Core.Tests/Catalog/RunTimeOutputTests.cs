using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Catalog;

public class RunTimeOutputTests
{
    [Fact]
    public void New_games_leave_out_logs_and_crash_dumps()
    {
        Assert.Equal(["**/*.log", "**/*.dmp", "**/*.mdmp"], RunTimeOutput.DefaultRules);
    }

    [Theory]
    [InlineData("crloader.log")]
    [InlineData("crmods/RadarPlus/RadarPlus.log")]
    [InlineData(@"BepInEx\LogOutput.log")]
    [InlineData("crash.dmp")]
    [InlineData("bin/hammer_8075_crash.mdmp")]
    public void Logs_and_crash_dumps_are_run_time_output(string path)
    {
        Assert.True(RunTimeOutput.IsRunTimeOutput(path, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("data/base.pak")]
    [InlineData("Game.log.bak")]
    [InlineData("0.paz.bak")]
    [InlineData("shaderCache/shader.bin")]
    [InlineData("Engine/Programs/CrashReportClient/CrashReportClient.exe")]
    [InlineData("demoheader.tmp")]
    [InlineData("log")]
    [InlineData("logs/readme.txt")]
    public void Anything_else_is_part_of_the_game_or_a_mod(string path)
    {
        Assert.False(RunTimeOutput.IsRunTimeOutput(path, StringComparison.Ordinal));
    }

    [Fact]
    public void Letter_case_counts_only_where_the_folders_history_ignores_it()
    {
        Assert.False(RunTimeOutput.IsRunTimeOutput("CRASH.DMP", StringComparison.Ordinal));
        Assert.True(RunTimeOutput.IsRunTimeOutput("CRASH.DMP", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_set_of_only_logs_is_covered_by_the_log_rule_alone()
    {
        var rules = RunTimeOutput.RulesCovering(["crloader.log", "crmods/a/a.log"], StringComparison.Ordinal);

        Assert.Equal(["**/*.log"], rules);
    }

    [Fact]
    public void A_set_of_logs_and_dumps_is_covered_by_each_rule_that_matched_in_default_order()
    {
        var rules = RunTimeOutput.RulesCovering(["crash/a.mdmp", "x.log"], StringComparison.Ordinal);

        Assert.Equal(["**/*.log", "**/*.mdmp"], rules);
    }

    [Fact]
    public void One_file_that_is_not_run_time_output_means_nothing_is_covered()
    {
        Assert.Null(RunTimeOutput.RulesCovering(["x.log", "mods/a.txt"], StringComparison.Ordinal));
    }

    [Fact]
    public void No_files_are_not_covered()
    {
        Assert.Null(RunTimeOutput.RulesCovering([], StringComparison.Ordinal));
    }

    [Fact]
    public void Merging_keeps_the_existing_rules_first_and_skips_the_ones_already_there()
    {
        var merged = RunTimeOutput.Merge(["Saves/*.tmp", "**/*.log"], ["**/*.log", "**/*.dmp"]);

        Assert.Equal(["Saves/*.tmp", "**/*.log", "**/*.dmp"], merged);
    }
}
