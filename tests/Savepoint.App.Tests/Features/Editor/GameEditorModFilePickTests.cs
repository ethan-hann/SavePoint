using Savepoint.App.Features.Editor;

namespace Savepoint.App.Tests.Features.Editor;

// "Choose files…" and "Choose folders…" fill the mod files list from the system pickers, relative to the game folder.
public sealed class GameEditorModFilePickTests : IDisposable
{
    private readonly string _install = Directory.CreateTempSubdirectory("savepoint-modpick-").FullName;
    private readonly EditorRig _rig = EditorRig.Create();
    private readonly GameEditorViewModel _editor;

    public GameEditorModFilePickTests()
    {
        _editor = _rig.New();
        _editor.InstallPath = _install;
    }

    public void Dispose() => Directory.Delete(_install, recursive: true);

    private string Inside(params string[] parts) => Path.Combine([_install, .. parts]);

    // A sibling of the game folder, so it is outside it on every operating system.
    private string Outside(string name) => Path.Combine(Path.GetDirectoryName(_install)!, "elsewhere", name);

    [Fact]
    public async Task Several_files_at_the_root_of_the_game_folder_become_several_entries()
    {
        _rig.Dialogs.AnswerFiles(Inside("version.dll"), Inside("winmm.dll"), Inside("dinput8.dll"));

        await _editor.ChooseModFilesCommand.ExecuteAsync(null);

        Assert.Equal(["version.dll", "winmm.dll", "dinput8.dll"], _editor.ModFolders.Values);
    }

    [Fact]
    public async Task A_file_in_a_subfolder_keeps_its_path_from_the_game_folder_with_forward_slashes()
    {
        _rig.Dialogs.AnswerFiles(Inside("Data", "Plugins", "x.asi"));

        await _editor.ChooseModFilesCommand.ExecuteAsync(null);

        Assert.Equal(["Data/Plugins/x.asi"], _editor.ModFolders.Values);
    }

    [Fact]
    public async Task A_folder_becomes_a_folder_entry_the_way_a_typed_one_is_written()
    {
        _rig.Dialogs.AnswerFolders(Inside("BepInEx"), Inside("Data", "Mods"));

        await _editor.ChooseModFoldersCommand.ExecuteAsync(null);

        Assert.Equal(["BepInEx", "Data/Mods"], _editor.ModFolders.Values);
    }

    [Fact]
    public async Task Entries_already_in_the_list_and_repeats_within_the_pick_are_added_once()
    {
        _editor.ModFolders.SetValues(["version.dll"]);
        _rig.Dialogs.AnswerFiles(Inside("version.dll"), Inside("winmm.dll"), Inside("winmm.dll"));

        await _editor.ChooseModFilesCommand.ExecuteAsync(null);

        Assert.Equal(["version.dll", "winmm.dll"], _editor.ModFolders.Values);
    }

    [Fact]
    public async Task A_picked_item_outside_the_game_folder_is_not_added_and_the_line_says_how_many_and_why()
    {
        _rig.Dialogs.AnswerFiles(Inside("version.dll"), Outside("other.dll"), Path.GetDirectoryName(_install)! + Path.DirectorySeparatorChar + "stray.dll");

        await _editor.ChooseModFilesCommand.ExecuteAsync(null);

        Assert.Equal(["version.dll"], _editor.ModFolders.Values);
        Assert.Equal("2 items were left out because they aren't inside the game folder.", _editor.ModFilesPickNote);
        Assert.True(_editor.HasModFilesPickNote);
    }

    [Fact]
    public async Task One_item_outside_the_game_folder_is_counted_in_the_singular()
    {
        _rig.Dialogs.AnswerFolders(Outside("Mods"));

        await _editor.ChooseModFoldersCommand.ExecuteAsync(null);

        Assert.Empty(_editor.ModFolders.Values);
        Assert.Equal("1 item was left out because it isn't inside the game folder.", _editor.ModFilesPickNote);
    }

    [Fact]
    public async Task The_game_folder_itself_is_not_an_entry_and_counts_as_left_out()
    {
        _rig.Dialogs.AnswerFolders(_install);

        await _editor.ChooseModFoldersCommand.ExecuteAsync(null);

        Assert.Empty(_editor.ModFolders.Values);
        Assert.Equal("1 item was left out because it isn't inside the game folder.", _editor.ModFilesPickNote);
    }

    [Fact]
    public async Task A_pick_in_a_folder_whose_name_only_begins_with_the_game_folders_name_is_outside_it()
    {
        // The game folder's text is a prefix of this path, but the folder is a different one.
        var sibling = _install + "Bar";
        _rig.Dialogs.AnswerFiles(Inside("version.dll"), Path.Combine(sibling, "x.dll"));

        await _editor.ChooseModFilesCommand.ExecuteAsync(null);

        Assert.Equal(["version.dll"], _editor.ModFolders.Values);
        Assert.Equal("1 item was left out because it isn't inside the game folder.", _editor.ModFilesPickNote);
    }

    [Fact]
    public async Task A_pick_on_another_drive_is_outside_the_game_folder()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows has drive letters");
        var installDrive = char.ToUpperInvariant(_install[0]);
        var otherDrive = installDrive == 'Z' ? 'Y' : 'Z';
        _rig.Dialogs.AnswerFiles(Inside("version.dll"), $@"{otherDrive}:\Data\x.dll");

        await _editor.ChooseModFilesCommand.ExecuteAsync(null);

        Assert.Equal(["version.dll"], _editor.ModFolders.Values);
        Assert.Equal("1 item was left out because it isn't inside the game folder.", _editor.ModFilesPickNote);
    }

    [Fact]
    public async Task A_pick_whose_game_folder_part_differs_only_in_case_is_inside_it_on_Windows_and_the_entry_keeps_the_picked_casing()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows treats paths that differ in case as the same folder");
        var shouted = _install.ToUpperInvariant();
        Assert.NotEqual(_install, shouted);
        _rig.Dialogs.AnswerFiles(Path.Combine(shouted, "Data", "Version.dll"));

        await _editor.ChooseModFilesCommand.ExecuteAsync(null);

        Assert.Equal(["Data/Version.dll"], _editor.ModFolders.Values);
        Assert.Equal(string.Empty, _editor.ModFilesPickNote);
    }

    [Fact]
    public async Task A_cancelled_pick_adds_nothing_and_clears_the_line_from_the_pick_before()
    {
        _editor.ModFolders.SetValues(["Data"]);
        _rig.Dialogs.AnswerFiles(Outside("other.dll"));
        await _editor.ChooseModFilesCommand.ExecuteAsync(null);
        Assert.NotEmpty(_editor.ModFilesPickNote);

        await _editor.ChooseModFilesCommand.ExecuteAsync(null);

        Assert.Equal(["Data"], _editor.ModFolders.Values);
        Assert.Equal(string.Empty, _editor.ModFilesPickNote);
        Assert.False(_editor.HasModFilesPickNote);
    }

    [Fact]
    public async Task Both_pickers_open_in_the_game_folder_with_their_own_titles()
    {
        await _editor.ChooseModFilesCommand.ExecuteAsync(null);
        await _editor.ChooseModFoldersCommand.ExecuteAsync(null);

        var files = Assert.Single(_rig.Dialogs.FilesPicks);
        var folders = Assert.Single(_rig.Dialogs.FoldersPicks);
        Assert.Equal(_install, files.StartPath);
        Assert.Equal(_install, folders.StartPath);
        Assert.Equal("Choose the mod files", files.Title);
        Assert.Equal("Choose the mod folders", folders.Title);
    }

    [Fact]
    public async Task A_game_folder_typed_with_a_trailing_separator_still_gives_paths_from_it()
    {
        _editor.InstallPath = _install + Path.DirectorySeparatorChar;
        _rig.Dialogs.AnswerFiles(Inside("version.dll"));

        await _editor.ChooseModFilesCommand.ExecuteAsync(null);

        Assert.Equal(["version.dll"], _editor.ModFolders.Values);
    }

    [Fact]
    public async Task Both_pickers_are_unavailable_until_the_game_folder_is_named_and_the_line_says_why()
    {
        var editor = _rig.New();

        Assert.False(editor.CanChooseModFiles);
        Assert.False(editor.ChooseModFilesCommand.CanExecute(null));
        Assert.False(editor.ChooseModFoldersCommand.CanExecute(null));
        Assert.Equal("Choose the game's install folder first, then you can pick its mod files here.", editor.ModFilesPickNote);
        Assert.True(editor.HasModFilesPickNote);
        await editor.ChooseModFilesCommand.ExecuteAsync(null);
        Assert.Empty(_rig.Dialogs.FilesPicks);

        var changes = 0;
        editor.ChooseModFilesCommand.CanExecuteChanged += (_, _) => changes++;
        editor.InstallPath = _install;

        Assert.True(editor.CanChooseModFiles);
        Assert.True(editor.ChooseModFilesCommand.CanExecute(null));
        Assert.True(editor.ChooseModFoldersCommand.CanExecute(null));
        Assert.Equal(string.Empty, editor.ModFilesPickNote);
        Assert.True(changes > 0);
    }

    [Fact]
    public void The_extra_buttons_are_named_for_what_they_pick()
    {
        var actions = _editor.ModFolders.Actions;

        Assert.Equal(["Choose files…", "Choose folders…"], actions.Select(action => action.Label));
        Assert.Equal(["Choose files…, for the mod files in the game folder", "Choose folders…, for the mod files in the game folder"], actions.Select(action => action.AutomationName));
    }

    [Fact]
    public void The_extra_buttons_carry_the_reason_while_the_game_folder_is_not_named()
    {
        var editor = _rig.New();

        Assert.All(editor.ModFolders.Actions, action => Assert.Equal("Choose the game's install folder first, then you can pick its mod files here.", action.HelpText));
        editor.InstallPath = _install;
        Assert.All(editor.ModFolders.Actions, action => Assert.Null(action.HelpText));
    }
}
