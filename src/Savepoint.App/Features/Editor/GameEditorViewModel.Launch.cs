using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Dialogs;

namespace Savepoint.App.Features.Editor;

public sealed partial class GameEditorViewModel
{
    private const int _maxExeSuggestions = 8;
    private const int _maxProcessSuggestions = 6;

    private IReadOnlyList<string> _executables = [];
    private CancellationTokenSource? _scan;

    // The folder the suggestions were last read for, so leaving the field unchanged does not scan a large install again.
    private string? _scannedFolder;

    /// <summary>The executables found in the install folder that match what is typed in the start file field.</summary>
    public ObservableCollection<SuggestionItem> ExeSuggestions { get; } = [];

    /// <summary>The file names found in the install folder that match what is typed in the process name field.</summary>
    public ObservableCollection<SuggestionItem> ProcessSuggestions { get; } = [];

    /// <summary>True when there are start files to suggest.</summary>
    public bool HasExeSuggestions => ExeSuggestions.Count > 0;

    /// <summary>True when there are process names to suggest.</summary>
    public bool HasProcessSuggestions => ProcessSuggestions.Count > 0;

    /// <summary>The "Run the .exe" segment's text, named for the operating system's kind of program.</summary>
    public string RunExeLabel => _text.Message("gameEditor.launch.exe");

    /// <summary>The warning about starting through Steam.</summary>
    public string SteamWarning => _text.Message("gameEditor.launch.steamWarning");

    /// <summary>True while the start method is running a file.</summary>
    public bool IsExeMethod => LaunchChoice == 0;

    /// <summary>True while the start method is a launch link.</summary>
    public bool IsSteamMethod => LaunchChoice == 1;

    /// <summary>
    /// Looks in the install folder for programs to suggest, in place of any earlier look that is still running. A folder that
    /// cannot be read just means there is nothing to suggest.
    /// </summary>
    public async Task RefreshSuggestionsAsync()
    {
        var install = InstallPath.Trim();
        if (install == _scannedFolder)
        {
            return;
        }

        _scan?.Cancel();
        _scan?.Dispose();
        var scan = _scan = new CancellationTokenSource();

        if (install.Length == 0)
        {
            _executables = [];
            _scannedFolder = install;
            UpdateSuggestions();
            return;
        }

        var found = await _games.ListExecutablesAsync(install, scan.Token);
        if (scan.IsCancellationRequested)
        {
            return;
        }

        _executables = found.Ok ? found.Value : [];
        _scannedFolder = found.Ok ? install : null;
        UpdateSuggestions();
    }

    partial void OnLaunchChoiceChanged(int value)
    {
        LaunchError = null;
        OnPropertyChanged(nameof(IsExeMethod));
        OnPropertyChanged(nameof(IsSteamMethod));
    }

    partial void OnExePathChanged(string value)
    {
        ExePathError = null;
        UpdateSuggestions();
    }

    partial void OnProcessNameChanged(string value) => UpdateSuggestions();

    private void UpdateSuggestions()
    {
        Replace(ExeSuggestions, Matching(_executables, ExePath, _maxExeSuggestions), text => ExePath = text);
        var names = _executables.Select(FileNameOf).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Replace(ProcessSuggestions, Matching(names, ProcessName, _maxProcessSuggestions), text => ProcessName = text);
        OnPropertyChanged(nameof(HasExeSuggestions));
        OnPropertyChanged(nameof(HasProcessSuggestions));
    }

    // What is already in the field is not offered back. A field that is empty or holds one of the candidates has nothing
    // half typed, so the others are all offered as alternatives; otherwise the candidates are narrowed to what has been typed.
    private static IEnumerable<string> Matching(IReadOnlyList<string> candidates, string typed, int limit)
    {
        var text = typed.Trim();
        var isCandidate = candidates.Any(candidate => string.Equals(candidate, text, StringComparison.OrdinalIgnoreCase));
        var narrow = text.Length > 0 && !isCandidate;
        return candidates
            .Where(candidate => !string.Equals(candidate, text, StringComparison.OrdinalIgnoreCase)
                && (!narrow || candidate.Contains(text, StringComparison.OrdinalIgnoreCase)))
            .Take(limit);
    }

    private static void Replace(ObservableCollection<SuggestionItem> target, IEnumerable<string> texts, Action<string> use)
    {
        target.Clear();
        foreach (var text in texts)
        {
            target.Add(new SuggestionItem(text, new RelayCommand(() => use(text))));
        }
    }

    private static string FileNameOf(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];

    [RelayCommand]
    private async Task BrowseExeAsync()
    {
        var install = InstallPath.Trim();
        if (install.Length == 0)
        {
            ExePathError = _text.Message("gameEditor.launch.exeNeedsInstall");
            return;
        }

        var folder = await StartFolderAsync(install) ?? install;
        var title = _text.Message("gameEditor.launch.exePickTitle");
        if (await _dialogs.PickOpenFileAsync(new OpenFilePickRequest(title, StartPath: folder)) is not { } picked)
        {
            return;
        }

        if (RelativeTo(folder, picked) is { } relative)
        {
            ExePath = relative;
        }
        else
        {
            ExePathError = _text.Message("gameEditor.launch.exeOutside", ("folder", folder));
        }
    }

    // The path of a file inside a folder, relative to it; null for a file outside it (D13.11).
    private static string? RelativeTo(string folder, string file)
    {
        var relative = Path.GetRelativePath(folder, file);
        return Path.IsPathRooted(relative) || relative == "." || relative.Split('/', '\\')[0] == ".." ? null : relative;
    }
}
