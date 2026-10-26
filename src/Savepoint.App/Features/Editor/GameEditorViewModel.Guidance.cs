using CommunityToolkit.Mvvm.ComponentModel;
using Savepoint.App.Features.Help;

namespace Savepoint.App.Features.Editor;

public sealed partial class GameEditorViewModel
{
    /// <summary>"Can I use this with online games?", beside how the game starts.</summary>
    public ContextualHelpViewModel AntiCheatHelp { get; private set; } = null!;

    /// <summary>"How do these rules work?", under the lists of files to leave out.</summary>
    public ContextualHelpViewModel RulesHelp { get; private set; } = null!;

    /// <summary>"When do I need this?" for the program name SavePoint waits for.</summary>
    public ContextualHelpViewModel ProcessHelp { get; private set; } = null!;

    /// <summary>"When do I need this?" for the folders that hold mods.</summary>
    public ContextualHelpViewModel ModFoldersHelp { get; private set; } = null!;

    /// <summary>"When do I need this?" for the Steam app id.</summary>
    public ContextualHelpViewModel SteamAppIdHelp { get; private set; } = null!;

    /// <summary>"Does my store sync saves?", beside the saves folder; shown only while the store's answer is unknown.</summary>
    public ContextualHelpViewModel CloudSavesHelp { get; private set; } = null!;

    /// <summary>True while it is not known whether the store syncs this game's saves, so the question button shows.</summary>
    public bool HasCloudSavesUnknown { get; private set; }

    /// <summary>
    /// True while the "More settings" section shows its fields. It starts open for an existing game that uses any of them, and opens
    /// by itself when one of them has a problem to show.
    /// </summary>
    [ObservableProperty]
    private bool _isMoreSettingsOpen;

    /// <summary>
    /// The notes the game was started from (a template or a definition file) while a new game is being added; empty otherwise.
    /// A saved game's notes are the player's own and are never shown (D13.22).
    /// </summary>
    public string TemplateNotes => IsCreating && _source.Notes is { } notes ? notes.Trim() : string.Empty;

    /// <summary>True when <see cref="TemplateNotes"/> has text.</summary>
    public bool HasTemplateNotes => TemplateNotes.Length > 0;

    partial void OnSteamAppIdErrorChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            IsMoreSettingsOpen = true;
        }
    }

    private void NotifyTemplateNotes()
    {
        OnPropertyChanged(nameof(TemplateNotes));
        OnPropertyChanged(nameof(HasTemplateNotes));
    }

    // Whether the form holds anything that lives in "More settings".
    private bool HasMoreSettingsSet() =>
        IgnoreRules.Values.Count > 0
        || InstallIgnoreRules.Values.Count > 0
        || LaunchArgs.Values.Count > 0
        || ModFolders.Values.Count > 0
        || ProcessName.Trim().Length > 0
        || SteamAppIdText.Trim().Length > 0;

    private void InitializeGuidance()
    {
        IsMoreSettingsOpen = IsEditing && HasMoreSettingsSet();
        AntiCheatHelp = new ContextualHelpViewModel(_text.Message("gameEditor.antiCheat.question"), _text.Message("gameEditor.antiCheat"));
        RulesHelp = new ContextualHelpViewModel(_text.Message("gameEditor.rules.question"), _text.Message("gameEditor.rules.answer"));
        ProcessHelp = new ContextualHelpViewModel(_text.Message("gameEditor.whenNeeded.question"), _text.Message("gameEditor.processName.helper"));
        ModFoldersHelp = new ContextualHelpViewModel(_text.Message("gameEditor.whenNeeded.question"), _text.Message("gameEditor.modPathHints.helper"));
        SteamAppIdHelp = new ContextualHelpViewModel(_text.Message("gameEditor.whenNeeded.question"), _text.Message("gameEditor.artwork.appId.helper"));
        CloudSavesHelp = new ContextualHelpViewModel(_text.Message("gameEditor.cloudSaves.unknown.question"), _text.Message("gameEditor.cloudSaves.unknown"));
    }
}
