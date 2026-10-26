using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Shell;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Features.Editor;

public sealed partial class GameEditorViewModel
{
    private const int _minSearchLength = 2;
    private const int _maxNameMatches = 8;

    // Each template's name split into words, built on the first search.
    private List<(GameTemplate Template, string[] Words)>? _templateWords;

    // True while the form is being filled from a definition or a template, so those names are not searched for.
    private bool _fillingForm;

    /// <summary>The built-in templates whose names match what is typed in the Name field; empty while editing a game.</summary>
    public ObservableCollection<TemplateItem> NameMatches { get; } = [];

    /// <summary>True when <see cref="NameMatches"/> has entries to show.</summary>
    public bool HasNameMatches => NameMatches.Count > 0;

    /// <summary>True when "Pick from the game list" is offered: only while adding a game.</summary>
    public bool CanPickTemplate => IsCreating;

    // Only the player's typing searches; a name the form was filled with does not.
    private void SearchTemplates(string typed)
    {
        NameMatches.Clear();
        if (IsCreating && !_fillingForm && typed.Trim().Length >= _minSearchLength)
        {
            var words = TemplateSearch.WordsOf(typed);
            _templateWords ??= [.. _games.Templates.Select(template => (template, TemplateSearch.WordsOf(template.DisplayName)))];
            foreach (var (template, _) in _templateWords.Where(entry => TemplateSearch.Matches(words, entry.Words)).Take(_maxNameMatches))
            {
                NameMatches.Add(new TemplateItem(template.DisplayName, new AsyncRelayCommand(() => ApplyTemplateAsync(template, replaceName: true))));
            }
        }

        OnPropertyChanged(nameof(HasNameMatches));
    }

    [RelayCommand]
    private void DismissNameMatches()
    {
        NameMatches.Clear();
        OnPropertyChanged(nameof(HasNameMatches));
    }

    // The same picker first run uses; a template fills the form, and "Open a file" is Import.
    [RelayCommand(CanExecute = nameof(CanPickTemplate))]
    private async Task PickTemplateAsync()
    {
        switch (await _dialogs.ShowAsync(new TemplatePickerViewModel(_games.Templates, _text)))
        {
            case TemplateChoice.Template chosen:
                await ApplyTemplateAsync(chosen.Chosen, replaceName: false);
                break;
            case TemplateChoice.File:
                await ImportAsync();
                break;
        }
    }

    // Starts from the same draft first run opens the editor with, but fills only what is still empty, so nothing the player typed is
    // lost. A name typed into the search box was only a search, so picking from that box replaces it.
    private async Task ApplyTemplateAsync(GameTemplate template, bool replaceName)
    {
        var draft = template.ToDraft();
        _fillingForm = true;
        try
        {
            if (replaceName || Name.Trim().Length == 0)
            {
                Name = draft.Name;
            }

            FillStartingPoint(draft);
            FillRules(draft);
            if (SteamAppIdText.Trim().Length == 0)
            {
                SteamAppIdText = draft.SteamAppId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            }

            if (ModFolders.Values.Count == 0)
            {
                ModFolders.SetValues(draft.ModPathHints);
            }

            if (string.IsNullOrWhiteSpace(_source.Notes))
            {
                _source = _source with { Notes = draft.Notes };
            }

            DefaultRulesNote = string.Empty;
        }
        finally
        {
            _fillingForm = false;
        }

        NameMatches.Clear();
        OnPropertyChanged(nameof(HasNameMatches));
        _toasts.Show(new ToastMessage(_text.Message("gameEditor.template.applied", ("name", draft.Name)), ToastKind.Success));
        await RefreshSuggestionsAsync();
    }

    private void FillStartingPoint(GameDefinition draft)
    {
        if (InstallPath.Trim().Length == 0)
        {
            InstallPath = draft.Install.Path;
        }

        if (SavesPath.Trim().Length == 0)
        {
            SavesPath = draft.External?.Path ?? string.Empty;
        }

        // The start method comes with its file or link, so it only follows the template when the player has chosen neither.
        if (ExePath.Trim().Length == 0 && Uri.Trim().Length == 0)
        {
            LaunchChoice = draft.Launch.Method == LaunchMethod.Uri ? 1 : 0;
            ExePath = draft.Launch.ExePath ?? string.Empty;
            Uri = draft.Launch.Uri ?? string.Empty;
        }

        if (ProcessName.Trim().Length == 0)
        {
            ProcessName = draft.Launch.ProcessNameForExitDetection ?? string.Empty;
        }

        if (LaunchArgs.Values.Count == 0)
        {
            LaunchArgs.SetValues(draft.Launch.Args);
        }
    }

    private void FillRules(GameDefinition draft)
    {
        if (IgnoreRules.Values.Count == 0)
        {
            IgnoreRules.SetValues(draft.IgnorePaths);
            _suppliedRules = [.. _suppliedRules, .. draft.IgnorePaths.Except(_suppliedRules)];
        }

        // A new form starts with the default rules here; the template's draft already holds them, minus any it covers game-wide.
        InstallIgnoreRules.SetValues(
            InstallIgnoreRules.Values.SequenceEqual(RunTimeOutput.DefaultRules)
                ? draft.InstallIgnorePaths
                : RunTimeOutput.Merge(InstallIgnoreRules.Values, draft.InstallIgnorePaths));
    }
}
