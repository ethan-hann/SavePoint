using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Dialogs;

namespace Savepoint.App.Features.Editor;

public sealed partial class GameEditorViewModel
{
    // How many items of the last pick were not inside the game folder.
    private int _modFilesLeftOut;

    /// <summary>True when the install folder is named, which is what the mod file pickers start from and measure against.</summary>
    public bool CanChooseModFiles => InstallPath.Trim().Length > 0;

    /// <summary>The line under the mod files list: why the pickers are unavailable, or how many picked items were left out; empty for none.</summary>
    public string ModFilesPickNote => !CanChooseModFiles
        ? _text.Message("gameEditor.modFiles.needInstall")
        : _modFilesLeftOut > 0
            ? _text.Message("gameEditor.modFiles.leftOut", ("count", _modFilesLeftOut))
            : string.Empty;

    /// <summary>True when <see cref="ModFilesPickNote"/> has text.</summary>
    public bool HasModFilesPickNote => ModFilesPickNote.Length > 0;

    [RelayCommand(CanExecute = nameof(CanChooseModFiles))]
    private Task ChooseModFilesAsync() => PickModEntriesAsync(folders: false);

    [RelayCommand(CanExecute = nameof(CanChooseModFiles))]
    private Task ChooseModFoldersAsync() => PickModEntriesAsync(folders: true);

    // Each pick starts from a clean line. What was picked becomes entries written the way a typed one is, relative to the game folder.
    private async Task PickModEntriesAsync(bool folders)
    {
        // A command's ExecuteAsync does not look at its CanExecute, so the guard is here too.
        SetModFilesLeftOut(0);
        if (!CanChooseModFiles || await StartFolderAsync(InstallPath) is not { } installFolder)
        {
            return;
        }

        var picked = folders
            ? await _dialogs.PickFoldersAsync(new FolderPickRequest(_text.Message("gameEditor.modFiles.pickFoldersTitle"), installFolder))
            : await _dialogs.PickFilesAsync(new OpenFilePickRequest(_text.Message("gameEditor.modFiles.pickFilesTitle"), StartPath: installFolder));

        var entries = new List<string>();
        var leftOut = 0;
        foreach (var path in picked)
        {
            if (EntryInside(installFolder, path) is { } entry)
            {
                entries.Add(entry);
            }
            else
            {
                leftOut++;
            }
        }

        ModFolders.AddValues(entries);
        SetModFilesLeftOut(leftOut);
    }

    // The picked path from the game folder with forward slashes, or null when it is the folder itself or not inside it.
    private static string? EntryInside(string installFolder, string picked)
    {
        var relative = Path.GetRelativePath(installFolder, picked);
        var parts = relative.Split(Path.DirectorySeparatorChar);
        if (relative == "." || Path.IsPathRooted(relative) || parts[0] == "..")
        {
            return null;
        }

        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private void SetModFilesLeftOut(int count)
    {
        _modFilesLeftOut = count;
        NotifyModFilesPick();
    }

    // The pickers need the game folder, and the line and the buttons' help text give the reason while it is missing.
    private void NotifyModFilesPick()
    {
        OnPropertyChanged(nameof(CanChooseModFiles));
        OnPropertyChanged(nameof(ModFilesPickNote));
        OnPropertyChanged(nameof(HasModFilesPickNote));
        ChooseModFilesCommand.NotifyCanExecuteChanged();
        ChooseModFoldersCommand.NotifyCanExecuteChanged();
        var reason = CanChooseModFiles ? null : _text.Message("gameEditor.modFiles.needInstall");
        foreach (var action in ModFolders.Actions)
        {
            action.HelpText = reason;
        }
    }
}
