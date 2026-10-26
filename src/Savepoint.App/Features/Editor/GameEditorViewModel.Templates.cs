using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Shell;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Features.Editor;

public sealed partial class GameEditorViewModel
{
    private const int _minSearchLength = 2;
    private const int _maxNameMatches = 8;

    // Each template's name prepared for searching, built on the first search.
    private List<(GameTemplate Template, SearchKey Key)>? _templateKeys;

    // True while the form is being filled from a definition or a template, so those names are not searched for.
    private bool _fillingForm;

    // The draft of the template last applied, and the install-only rules it left in the form: a field that still holds what that
    // template put there counts as empty, so a second pick replaces the first instead of stacking on it.
    private GameDefinition? _applied;
    private IReadOnlyList<string>? _appliedInstallRules;

    // True while the notes in the form came from a template, which are the only notes that are shown (D53.5).
    private bool _notesFromTemplate;

    private string _nameMatchesAnnouncement = string.Empty;

    /// <summary>The built-in templates whose names match what is typed in the Name field; empty while editing a game.</summary>
    public ObservableCollection<TemplateItem> NameMatches { get; } = [];

    /// <summary>True when <see cref="NameMatches"/> has entries to show.</summary>
    public bool HasNameMatches => NameMatches.Count > 0;

    /// <summary>Said to screen readers when the list of matches changes; empty when there is no list.</summary>
    public string NameMatchesAnnouncement
    {
        get => _nameMatchesAnnouncement;
        private set => SetProperty(ref _nameMatchesAnnouncement, value);
    }

    /// <summary>Raised when the Name field should take the focus back, after a match was chosen from the list.</summary>
    public event EventHandler? FocusNameRequested;

    /// <summary>True when "Pick from the game list" is offered: only while adding a game.</summary>
    public bool CanPickTemplate => IsCreating;

    // Only the player's typing searches; a name the form was filled with does not.
    private void SearchTemplates(string typed)
    {
        NameMatches.Clear();
        if (IsCreating && !_fillingForm && typed.Trim().Length >= _minSearchLength && TemplateSearch.WordsOf(typed).Length > 0)
        {
            _templateKeys ??= [.. _games.Templates.Select(template => (template, TemplateSearch.KeyOf(template.DisplayName)))];
            foreach (var template in TemplateSearch.Find(_templateKeys, typed).Take(_maxNameMatches))
            {
                var name = template.DisplayName;
                NameMatches.Add(new TemplateItem(
                    name,
                    new AsyncRelayCommand(() => ApplyTemplateAsync(template, fromNameSearch: true)),
                    _text.Message("gameEditor.name.fillFrom", ("name", name))));
            }
        }

        NotifyNameMatchesChanged();
    }

    private void NotifyNameMatchesChanged()
    {
        OnPropertyChanged(nameof(HasNameMatches));
        NameMatchesAnnouncement = NameMatches.Count == 0 ? string.Empty : _text.Message("gameEditor.name.matchesFound", ("count", NameMatches.Count));
        DismissNameMatchesCommand.NotifyCanExecuteChanged();
        ChooseTopNameMatchCommand.NotifyCanExecuteChanged();
    }

    // Closes the list without choosing; only handled while it shows, so Escape reaches whatever else wants it the rest of the time.
    [RelayCommand(CanExecute = nameof(HasNameMatches))]
    private void DismissNameMatches()
    {
        NameMatches.Clear();
        NotifyNameMatchesChanged();
    }

    [RelayCommand(CanExecute = nameof(HasNameMatches))]
    private async Task ChooseTopNameMatchAsync()
    {
        if (NameMatches.FirstOrDefault()?.Choose is IAsyncRelayCommand top)
        {
            await top.ExecuteAsync(null);
        }
    }

    // The same picker first run uses, opened on what is typed in the Name field; a template fills the form, and "Open a file" is Import.
    [RelayCommand(CanExecute = nameof(CanPickTemplate))]
    private async Task PickTemplateAsync()
    {
        switch (await _dialogs.ShowAsync(new TemplatePickerViewModel(_games.Templates, _text, Name.Trim())))
        {
            case TemplateChoice.Template chosen:
                await ApplyTemplateAsync(chosen.Chosen, fromNameSearch: false);
                break;
            case TemplateChoice.File:
                await ImportAsync();
                break;
        }
    }

    // Starts from the same draft first run opens the editor with, but fills only what is still empty, so nothing the player typed is
    // lost. A name typed into the search box was only a search, so picking from that box replaces it.
    private async Task ApplyTemplateAsync(GameTemplate template, bool fromNameSearch)
    {
        var draft = template.ToDraft();
        var last = _applied;
        _fillingForm = true;
        try
        {
            if (fromNameSearch || IsEmpty(Name, last?.Name))
            {
                Name = draft.Name;
            }

            FillStartingPoint(draft, last);
            FillRules(template, draft, last);
            FillIdentifiers(draft, last);
            if (IsEmptyList(ModFolders.Values, last?.ModPathHints))
            {
                ModFolders.SetValues(draft.ModPathHints);
            }

            if (string.IsNullOrWhiteSpace(_source.Notes) || _source.Notes == last?.Notes)
            {
                _source = _source with { Notes = draft.Notes };
                _notesFromTemplate = true;
            }

            DefaultRulesNote = string.Empty;
            _applied = draft;
        }
        finally
        {
            _fillingForm = false;
        }

        NameMatches.Clear();
        NotifyNameMatchesChanged();
        NotifyTemplateNotes();
        _toasts.Show(new ToastMessage(_text.Message("gameEditor.template.applied", ("name", draft.Name)), ToastKind.Success));
        if (fromNameSearch)
        {
            FocusNameRequested?.Invoke(this, EventArgs.Empty);
        }

        await RefreshSuggestionsAsync();
    }

    // A field counts as empty when it is blank or still holds what the last template put there.
    private static bool IsEmpty(string current, string? applied) =>
        current.Trim().Length == 0 || (applied is not null && current.Trim() == applied.Trim());

    private static bool IsEmptyList(IReadOnlyList<string> current, IReadOnlyList<string>? applied) =>
        current.Count == 0 || (applied is not null && current.SequenceEqual(applied));

    private void FillStartingPoint(GameDefinition draft, GameDefinition? last)
    {
        if (IsEmpty(InstallPath, last?.Install.Path))
        {
            InstallPath = draft.Install.Path;
        }

        if (IsEmpty(SavesPath, last?.External?.Path))
        {
            SavesPath = draft.External?.Path ?? string.Empty;
        }

        var exeEmpty = IsEmpty(ExePath, last?.Launch.ExePath);
        var uriEmpty = IsEmpty(Uri, last?.Launch.Uri);

        // The start method is the template's only while the player has chosen neither a file nor a link and has not picked a method
        // of their own; a start file or link they typed keeps its method even when the template starts the other way.
        var choiceIsFree = exeEmpty && uriEmpty && (LaunchChoice == 0 || (last is not null && LaunchChoice == ChoiceOf(last)));
        if (exeEmpty)
        {
            ExePath = draft.Launch.ExePath ?? string.Empty;
        }

        if (uriEmpty)
        {
            Uri = draft.Launch.Uri ?? string.Empty;
        }

        if (choiceIsFree)
        {
            LaunchChoice = ChoiceOf(draft);
        }

        if (IsEmpty(ProcessName, last?.Launch.ProcessNameForExitDetection))
        {
            ProcessName = draft.Launch.ProcessNameForExitDetection ?? string.Empty;
        }

        if (IsEmptyList(LaunchArgs.Values, last?.Launch.Args))
        {
            LaunchArgs.SetValues(draft.Launch.Args);
        }
    }

    private static int ChoiceOf(GameDefinition game) => game.Launch.Method == LaunchMethod.Uri ? 1 : 0;

    // The Steam app id picks artwork and nothing else; a launch link the player typed for another game's id is the better word on which game this is.
    private void FillIdentifiers(GameDefinition draft, GameDefinition? last)
    {
        var lastId = last?.SteamAppId?.ToString(CultureInfo.InvariantCulture);
        if (!IsEmpty(SteamAppIdText, lastId))
        {
            return;
        }

        if (LaunchLink().Match(Uri.Trim()) is { Success: true } link
            && int.TryParse(link.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var linked)
            && linked != draft.SteamAppId)
        {
            return;
        }

        SteamAppIdText = draft.SteamAppId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    [GeneratedRegex(@"^steam://(?:run|rungameid)/(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LaunchLink();

    private void FillRules(GameTemplate template, GameDefinition draft, GameDefinition? last)
    {
        if (IsEmptyList(IgnoreRules.Values, last?.IgnorePaths))
        {
            IgnoreRules.SetValues(draft.IgnorePaths);
            _suppliedRules = [.. _suppliedRules, .. draft.IgnorePaths.Except(_suppliedRules)];
        }

        // A form that was never touched here holds the default rules. They are rebuilt from the game-wide list the form ends up with, so
        // a default the game-wide list covers is not repeated and one it does not cover is not lost. A list the player changed keeps what
        // they removed, and only gains the template's own install-only rules.
        var untouched = InstallIgnoreRules.Values.SequenceEqual(RunTimeOutput.DefaultRules)
            || (_appliedInstallRules is not null && InstallIgnoreRules.Values.SequenceEqual(_appliedInstallRules));
        var rules = untouched
            ? RunTimeOutput.Merge(template.Definition.InstallIgnorePaths, RunTimeOutput.DefaultRules.Where(rule => !IgnoreRules.Values.Contains(rule)))
            : RunTimeOutput.Merge(InstallIgnoreRules.Values, template.Definition.InstallIgnorePaths);
        InstallIgnoreRules.SetValues(rules);
        _appliedInstallRules = rules;
    }
}
