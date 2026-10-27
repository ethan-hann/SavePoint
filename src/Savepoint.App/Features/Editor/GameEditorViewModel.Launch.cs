using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.Core.Platform;

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
    private string? _scannedName;

    // The program name the player let SavePoint fill in; the confirmation about it stays until the field holds something else.
    private string _confirmedProcessName = string.Empty;

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
        OnPropertyChanged(nameof(CanOpenProcessNameField));
    }

    // The field is under More settings, so filling it from here says so; the confirmation goes when the player changes the field.
    [RelayCommand]
    private void UseSuggestedProcessName()
    {
        var name = SuggestedProcessName;
        ProcessName = name;
        _confirmedProcessName = name;
        ProcessNameConfirmation = _text.Message("gameEditor.processName.used", ("name", name));
    }

    /// <summary>What the program name is for and where to enter it, shown when no program could be suggested.</summary>
    public string ProcessNameMoreSettingsText => _text.Message("gameEditor.processName.moreSettings");

    /// <summary>True when a link-started game has no program name and none was found to suggest, so the pointer to More settings shows.</summary>
    public bool CanOpenProcessNameField => ShowProcessNameWhy && !HasProcessNameSuggestion;

    /// <summary>Raised when the program name field, newly opened under More settings, should take the focus.</summary>
    public event EventHandler? ProcessNameFieldFocusRequested;

    [RelayCommand]
    private void OpenProcessNameField()
    {
        IsMoreSettingsOpen = true;
        ProcessNameFieldFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Says which program name was filled in for the player and where to change it; empty when the player has not used the suggestion.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProcessNameConfirmation))]
    private string _processNameConfirmation = string.Empty;

    /// <summary>True when <see cref="ProcessNameConfirmation"/> has text.</summary>
    public bool HasProcessNameConfirmation => ProcessNameConfirmation.Length > 0;

    /// <summary>The "Run the .exe" segment's text, named for the operating system's kind of program.</summary>
    public string RunExeLabel => _text.Message("gameEditor.launch.exe");

    /// <summary>The warning about starting through Steam.</summary>
    public string SteamWarning => _text.Message("gameEditor.launch.steamWarning");

    /// <summary>
    /// True when the start method runs a file on Linux and the install folder is inside a Steam library: Steam's own start scripts
    /// there usually need Steam to run them, so <see cref="SteamStartNote"/> points at the link method instead.
    /// </summary>
    public bool ShowSteamStartNote => _text.OsKind == OsKind.Linux && IsExeMethod && IsInSteamLibrary(InstallPath);

    /// <summary>The note shown when <see cref="ShowSteamStartNote"/> is true; empty otherwise.</summary>
    public string SteamStartNote => ShowSteamStartNote ? _text.Message("gameEditor.launch.steamStartNote") : string.Empty;

    // Steam keeps every library's games under steamapps/common, however the library folder is named.
    private static bool IsInSteamLibrary(string installPath)
    {
        var parts = installPath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i + 1 < parts.Length; i++)
        {
            if (parts[i].Equals("steamapps", StringComparison.OrdinalIgnoreCase) && parts[i + 1].Equals("common", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void NotifySteamStartNote()
    {
        OnPropertyChanged(nameof(ShowSteamStartNote));
        OnPropertyChanged(nameof(SteamStartNote));
    }

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
        // The game's name is a hint in the ranking, so a rename asks again; the service ranks by file sizes the editor does not hold.
        var install = InstallPath.Trim();
        var gameName = Name.Trim();
        if (install == _scannedFolder && gameName == _scannedName)
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
            _scannedName = gameName;
            UpdateSuggestions();
            return;
        }

        var found = await _games.ListExecutablesAsync(install, gameName, scan.Token);
        if (scan.IsCancellationRequested)
        {
            return;
        }

        _executables = found.Ok ? found.Value : [];
        _scannedFolder = found.Ok ? install : null;
        _scannedName = gameName;
        UpdateSuggestions();
        await RefreshProcessNameSuggestionAsync();
    }

    partial void OnLaunchChoiceChanged(int value)
    {
        LaunchError = null;
        OnPropertyChanged(nameof(IsExeMethod));
        OnPropertyChanged(nameof(IsSteamMethod));
        NotifySteamStartNote();
        NotifyProcessNameOffer();
        _ = RefreshProcessNameSuggestionAsync();
        ScheduleCloudCheck();
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
        if (ProcessNameConfirmation.Length > 0 && value != _confirmedProcessName)
        {
            ProcessNameConfirmation = string.Empty;
        }
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
