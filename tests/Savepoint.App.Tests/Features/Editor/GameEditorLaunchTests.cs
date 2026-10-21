using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Editor;

public class GameEditorLaunchTests
{
    // Paths for the browse tests follow the operating system the test runs on, because turning a pick into a relative path is the system's own.
    private static readonly string _root = OperatingSystem.IsWindows() ? @"C:\Games\SkyrimSE" : "/games/SkyrimSE";

    private static string Under(params string[] parts) => Path.Combine([_root, .. parts]);

    private static string Elsewhere => OperatingSystem.IsWindows() ? @"D:\Other\Game.exe" : "/other/Game.exe";

    private static EditorRig RigWithExecutables(params string[] executables) =>
        EditorRig.Create(new FakeBackend(ScenarioBuilder.Mockup().WithExecutables(MockupScenario.SkyrimInstall, executables)));

    private static EditorRig BrowseRig() =>
        EditorRig.Create(new FakeBackend(ScenarioBuilder.Empty().WithGame(MockupScenario.Skyrim with { Install = new InstallLocation(_root) })));

    [Theory]
    [InlineData("windows", "Run the .exe", "Running the .exe avoids that.")]
    [InlineData("macos", "Run the app", "Running the app avoids that.")]
    [InlineData("linux", "Run the program", "Running the program avoids that.")]
    public void The_start_choice_names_the_kind_of_program_the_operating_system_runs(string os, string label, string warningEnd)
    {
        var editor = EditorRig.Create(os: os).New();

        Assert.Equal(label, editor.RunExeLabel);
        Assert.StartsWith("Starting through Steam can let Steam update the game again right after SavePoint puts your version back.", editor.SteamWarning);
        Assert.EndsWith(warningEnd, editor.SteamWarning);
    }

    [Fact]
    public void The_method_flags_follow_the_choice()
    {
        var editor = EditorRig.Create().New();
        Assert.True(editor.IsExeMethod);
        Assert.False(editor.IsSteamMethod);

        editor.LaunchChoice = 1;

        Assert.False(editor.IsExeMethod);
        Assert.True(editor.IsSteamMethod);
    }

    [Fact]
    public async Task Programs_found_in_the_install_folder_are_suggested_except_the_one_already_chosen()
    {
        var editor = RigWithExecutables("SkyrimSELauncher.exe", "SkyrimSE.exe", @"bin\tools\Helper.exe").Edit();

        await editor.LoadAsync();

        Assert.Equal(["SkyrimSE.exe", @"bin\tools\Helper.exe"], editor.ExeSuggestions.Select(item => item.Text));
    }

    [Fact]
    public async Task What_is_typed_narrows_the_suggestions_ignoring_case()
    {
        var editor = RigWithExecutables("SkyrimSELauncher.exe", "SkyrimSE.exe", @"bin\tools\Helper.exe").Edit();
        await editor.LoadAsync();

        editor.ExePath = "HELPER";

        Assert.Equal([@"bin\tools\Helper.exe"], editor.ExeSuggestions.Select(item => item.Text));
    }

    [Fact]
    public async Task Using_a_suggestion_fills_the_field_and_removes_it_from_the_list()
    {
        var editor = RigWithExecutables("SkyrimSELauncher.exe", "SkyrimSE.exe").Edit();
        await editor.LoadAsync();

        editor.ExeSuggestions[0].Use.Execute(null);

        Assert.Equal("SkyrimSE.exe", editor.ExePath);
        Assert.Equal(["SkyrimSELauncher.exe"], editor.ExeSuggestions.Select(item => item.Text));
    }

    [Fact]
    public async Task Only_a_handful_of_suggestions_are_shown()
    {
        var editor = RigWithExecutables([.. Enumerable.Range(1, 12).Select(number => $"tool{number:00}.exe")]).New();
        editor.InstallPath = MockupScenario.SkyrimInstall;

        await editor.RefreshSuggestionsAsync();

        Assert.Equal(8, editor.ExeSuggestions.Count);
        Assert.Equal(6, editor.ProcessSuggestions.Count);
    }

    [Fact]
    public async Task The_process_name_is_suggested_from_the_file_names_without_repeats()
    {
        var editor = RigWithExecutables("SkyrimSELauncher.exe", "SkyrimSE.exe", @"bin\tools\Helper.exe", @"other\Helper.exe").Edit();

        await editor.LoadAsync();

        Assert.Equal(["SkyrimSELauncher.exe", "Helper.exe"], editor.ProcessSuggestions.Select(item => item.Text));

        editor.ProcessSuggestions[0].Use.Execute(null);

        Assert.Equal("SkyrimSELauncher.exe", editor.ProcessName);
        Assert.Equal(["SkyrimSE.exe", "Helper.exe"], editor.ProcessSuggestions.Select(item => item.Text));
    }

    [Fact]
    public async Task Changing_the_install_folder_looks_again_and_an_empty_one_suggests_nothing()
    {
        var rig = RigWithExecutables("SkyrimSE.exe");
        var editor = rig.Edit();
        await editor.LoadAsync();
        rig.Backend.Calls.Clear();

        editor.InstallPath = @"D:\Games\Other";
        await editor.RefreshSuggestionsAsync();

        Assert.Equal(@"D:\Games\Other", Assert.Single(rig.Backend.Calls.To<IGamesApi>("ListExecutablesAsync")).Args[0]);

        editor.InstallPath = "  ";
        await editor.RefreshSuggestionsAsync();

        Assert.Empty(editor.ExeSuggestions);
        Assert.Single(rig.Backend.Calls.To<IGamesApi>("ListExecutablesAsync"));
    }

    [Fact]
    public async Task A_folder_that_was_already_looked_at_is_not_scanned_again_until_it_changes()
    {
        var rig = RigWithExecutables("SkyrimSE.exe");
        var editor = rig.Edit();
        await editor.LoadAsync();

        await editor.RefreshSuggestionsAsync();
        await editor.RefreshSuggestionsAsync();

        Assert.Single(rig.Backend.Calls.To<IGamesApi>("ListExecutablesAsync"));

        editor.InstallPath = @"D:\Games\Other";
        await editor.RefreshSuggestionsAsync();
        await editor.RefreshSuggestionsAsync();

        Assert.Equal(2, rig.Backend.Calls.To<IGamesApi>("ListExecutablesAsync").Count);
    }

    [Fact]
    public async Task A_scan_that_fails_suggests_nothing_and_shows_no_error()
    {
        var rig = RigWithExecutables("SkyrimSE.exe");
        rig.Backend.FailNext<IGamesApi>("ListExecutablesAsync", new ErrorInfo("error.unknown", new Dictionary<string, object?>(), string.Empty));
        var editor = rig.Edit();

        await editor.LoadAsync();

        Assert.Empty(editor.ExeSuggestions);
        Assert.Empty(rig.Toasts.Toasts);
        Assert.Null(editor.FormError);
    }

    [Fact]
    public async Task A_picked_file_inside_the_install_folder_becomes_a_relative_path()
    {
        var rig = BrowseRig();
        var editor = rig.Edit();
        rig.Dialogs.AnswerOpenFile(Under("bin", "Win64", "Game.exe"));

        await editor.BrowseExeCommand.ExecuteAsync(null);

        Assert.Equal(Path.Combine("bin", "Win64", "Game.exe"), editor.ExePath);
        Assert.Null(editor.ExePathError);
        Assert.Equal(_root, Assert.Single(rig.Dialogs.OpenFilePicks).StartPath);
    }

    [Theory]
    [InlineData("elsewhere")]
    [InlineData("sibling")]
    [InlineData("parent")]
    public async Task A_picked_file_outside_the_install_folder_is_refused_with_copy_and_the_field_is_kept(string kind)
    {
        var rig = BrowseRig();
        var editor = rig.Edit();
        var picked = kind switch
        {
            "elsewhere" => Elsewhere,
            "sibling" => Path.Combine(Path.GetDirectoryName(_root)!, "SkyrimSEOld", "Game.exe"),
            _ => Path.Combine(Path.GetDirectoryName(_root)!, "Game.exe"),
        };
        rig.Dialogs.AnswerOpenFile(picked);

        await editor.BrowseExeCommand.ExecuteAsync(null);

        Assert.Equal("SkyrimSELauncher.exe", editor.ExePath);
        Assert.NotNull(editor.ExePathError);
        Assert.StartsWith("That file isn't inside the game's install folder.", editor.ExePathError);
        Assert.Contains(_root, editor.ExePathError);
    }

    [Fact]
    public async Task Cancelling_the_file_picker_changes_nothing()
    {
        var rig = BrowseRig();
        var editor = rig.Edit();

        await editor.BrowseExeCommand.ExecuteAsync(null);

        Assert.Equal("SkyrimSELauncher.exe", editor.ExePath);
        Assert.Null(editor.ExePathError);
    }

    [Fact]
    public async Task Browsing_for_the_start_file_needs_the_install_folder_first()
    {
        var rig = EditorRig.Create();
        var editor = rig.New();

        await editor.BrowseExeCommand.ExecuteAsync(null);

        Assert.Equal("Choose the install folder first, so the start file can be shown relative to it.", editor.ExePathError);
        Assert.Empty(rig.Dialogs.OpenFilePicks);
    }
}
