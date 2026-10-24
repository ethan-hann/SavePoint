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

    // The folder the process name suggestion was last read for, and a counter so a slow answer for an old folder is dropped.
    private string? _suggestedFor;
    private int _suggestRun;

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

    /// <summary>The program name worth offering for a game started through a link; empty when there is none to offer.</summary>
    public string SuggestedProcessName { get; private set; } = string.Empty;

    /// <summary>True when a link-started game has no process name yet and a likely one was found in the install folder.</summary>
    public bool HasProcessNameSuggestion => ShowProcessNameWhy && SuggestedProcessName.Length > 0;

    /// <summary>True when the explanation of why the process name matters is shown: a link-started game with the field empty.</summary>
    public bool ShowProcessNameWhy => IsSteamMethod && string.IsNullOrWhiteSpace(ProcessName);

    /// <summary>The label of the button that fills in the suggested process name.</summary>
    public string UseSuggestedProcessNameLabel => _text.Message("gameEditor.processName.use", ("name", SuggestedProcessName));

    /// <summary>
    /// Looks in the install folder for the game's own program, to offer as the process name of a link-started game. Does nothing
    /// unless the start method is a link, the field is empty, and a folder is chosen; a folder already asked about is not asked
    /// again, and a folder that cannot be read just means there is nothing to offer.
    /// </summary>
    public async Task RefreshProcessNameSuggestionAsync()
    {
        var install = InstallPath.Trim();
        if (!ShowProcessNameWhy || install.Length == 0 || install == _suggestedFor)
        {
            return;
        }

        var run = ++_suggestRun;
        var found = await _games.SuggestProcessNameAsync(install, Name.Trim(), (_scan ??= new CancellationTokenSource()).Token);
        if (run != _suggestRun)
        {
            return;
        }

        _suggestedFor = found.Ok ? install : null;
        SuggestedProcessName = found.Ok ? found.Value : string.Empty;
        NotifyProcessNameOffer();
    }

    // An offer belongs to the folder it was read from, so it goes as soon as the folder changes and any answer still on its way is dropped.
    private void ClearProcessNameOffer()
    {
        _suggestRun++;
        _suggestedFor = null;
        SuggestedProcessName = string.Empty;
        NotifyProcessNameOffer();
    }

    private void NotifyProcessNameOffer()
    {
        OnPropertyChanged(nameof(SuggestedProcessName));
        OnPropertyChanged(nameof(HasProcessNameSuggestion));
        OnPropertyChanged(nameof(ShowProcessNameWhy));
        OnPropertyChanged(nameof(UseSuggestedProcessNameLabel));
    }

    [RelayCommand]
    private void UseSuggestedProcessName() => ProcessName = SuggestedProcessName;

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
            await RefreshProcessNameSuggestionAsync();
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
        await RefreshProcessNameSuggestionAsync();
    }

    partial void OnLaunchChoiceChanged(int value)
    {
        LaunchError = null;
        OnPropertyChanged(nameof(IsExeMethod));
        OnPropertyChanged(nameof(IsSteamMethod));
        NotifyProcessNameOffer();
        _ = RefreshProcessNameSuggestionAsync();
    }

    partial void OnExePathChanged(string value)
    {
        ExePathError = null;
        UpdateSuggestions();
    }

    partial void OnProcessNameChanged(string value)
    {
        UpdateSuggestions();
        NotifyProcessNameOffer();
    }

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
