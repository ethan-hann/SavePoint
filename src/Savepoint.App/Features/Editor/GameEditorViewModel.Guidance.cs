using CommunityToolkit.Mvvm.ComponentModel;
using Savepoint.App.Features.Help;
using Savepoint.Core.Catalog;

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

    /// <summary>"Where do my mods live?": why a game's mods folder is optional and when to set it.</summary>
    public ContextualHelpViewModel ModsHelp { get; private set; } = null!;

    /// <summary>True while it is not known whether the store syncs this game's saves, so the question button shows.</summary>
    public bool HasCloudSavesUnknown { get; private set; }

    /// <summary>
    /// True while the "More settings" section shows its fields. It starts open for an existing game that uses any of them, and opens
    /// by itself when one of them has a problem to show.
    /// </summary>
    [ObservableProperty]
    private bool _isMoreSettingsOpen;

    /// <summary>
    /// What a template's notes say, in the catalog's words, while a new game is being added from a template; empty otherwise. Notes
    /// from a definition file and a saved game's own notes are never shown (D13.22), and text the catalog has no words for is not shown.
    /// </summary>
    public string TemplateNotes =>
        IsCreating && _notesFromTemplate
            ? string.Join(' ', TemplateNoteCatalog.KeysIn(_source.Notes).Select(key => _text.Message(key)))
            : string.Empty;

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

    // Whether the form holds anything that lives in "More settings". The default install rules are there for every game, so they only
    // count once they have been changed.
    private bool HasMoreSettingsSet() =>
        IgnoreRules.Values.Count > 0
        || (InstallIgnoreRules.Values.Count > 0 && !InstallIgnoreRules.Values.SequenceEqual(RunTimeOutput.DefaultRules))
        || LaunchArgs.Values.Count > 0
        || ModFolders.Values.Count > 0
        || ProcessName.Trim().Length > 0
        || SteamAppIdText.Trim().Length > 0;

    private void InitializeGuidance()
    {
        IsMoreSettingsOpen = IsEditing && HasMoreSettingsSet();
        AntiCheatHelp = new ContextualHelpViewModel(_text.Message("gameEditor.antiCheat.question"), _text.Message("gameEditor.antiCheat"));
        var more = _text.Message("help.moreInPage");
        RulesHelp = new ContextualHelpViewModel(_text.Message("gameEditor.rules.question"), _text.Message("gameEditor.rules.answer"), more, () => OpenHelpPage(RulesHelp, HowItWorksSection.FilesToLeaveOut));
        ProcessHelp = new ContextualHelpViewModel(_text.Message("gameEditor.whenNeeded.question"), _text.Message("gameEditor.processName.helper"), more, () => OpenHelpPage(ProcessHelp, HowItWorksSection.HowTheGameStarts), automationName: _text.Message("gameEditor.processName.whenNeededName"));
        ModFoldersHelp = new ContextualHelpViewModel(_text.Message("gameEditor.whenNeeded.question"), _text.Message("gameEditor.modPathHints.helper"), automationName: _text.Message("gameEditor.modPathHints.whenNeededName"));
        SteamAppIdHelp = new ContextualHelpViewModel(_text.Message("gameEditor.whenNeeded.question"), _text.Message("gameEditor.artwork.appId.helper"), automationName: _text.Message("gameEditor.artwork.appId.whenNeededName"));
        ModsHelp = new ContextualHelpViewModel(_text.Message("gameEditor.mods.question"), _text.Message("gameEditor.mods.answer"));
        CloudSavesHelp = new ContextualHelpViewModel(_text.Message("gameEditor.cloudSaves.unknown.question"), _text.Message("gameEditor.cloudSaves.unknown"), more, () => OpenHelpPage(CloudSavesHelp, HowItWorksSection.CloudSaves));
    }

    private ContextualHelpViewModel? _helpToReturnTo;

    /// <summary>
    /// Where the form was scrolled to, in pixels. The view keeps it while it is on screen, so the form comes back to the same place
    /// after the "How SavePoint works" page covers it.
    /// </summary>
    // A pixel offset is view state, but the editor view is rebuilt when the player comes back from a help page, so only the view model outlives it.
    internal double FormScrollOffset { get; set; }

    /// <summary>
    /// The explanation whose link opened the help page, if the editor is coming back from that page; it is given once. The view puts
    /// focus on that explanation's question instead of on the name field.
    /// </summary>
    internal ContextualHelpViewModel? TakeHelpToReturnTo()
    {
        var help = _helpToReturnTo;
        _helpToReturnTo = null;
        return help;
    }

    private void OpenHelpPage(ContextualHelpViewModel from, HowItWorksSection section)
    {
        _helpToReturnTo = from;
        _shell.ShowHelp(section);
    }
}
