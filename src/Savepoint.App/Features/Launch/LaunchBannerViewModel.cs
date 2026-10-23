using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Theme;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.Launch;

/// <summary>One line of the "what SavePoint checked" list: an icon, a sentence and the folder it is about.</summary>
/// <param name="Icon">A Material Symbols glyph.</param>
/// <param name="Text">What was checked and found.</param>
/// <param name="Path">The folder involved, or null.</param>
public sealed record LaunchCheckLine(string Icon, string Text, string? Path);

/// <summary>
/// The "Everything was already in place" banner, pinned at the bottom of the Games pane while the game starts
/// without a dialog. It is a polite live region, and Details lists what was checked.
/// </summary>
public sealed partial class LaunchBannerViewModel
{
    private readonly UiFormatter _text;
    private readonly IDialogs _dialogs;

    /// <param name="preview">The preview that showed nothing needed to change.</param>
    /// <param name="gameName">The game that is starting.</param>
    /// <param name="profileName">The profile it starts from.</param>
    /// <param name="text">Formats the banner and its checks.</param>
    /// <param name="dialogs">Shows the checks list.</param>
    public LaunchBannerViewModel(LaunchPreview preview, string gameName, string profileName, UiFormatter text, IDialogs dialogs)
    {
        _text = text;
        _dialogs = dialogs;
        Title = text.Message("launch.banner.headline", ("gameName", gameName));

        var saveName = preview.External is null ? null : preview.AvailableSaves.FirstOrDefault(save => preview.SelectedSaveId is { } id && save.Id == id)?.Name;
        Detail = saveName is null
            ? profileName
            : text.Message("launch.banner.detailWithSave", ("profileName", profileName), ("saveName", saveName));
        Checks = ChecksFor(preview, saveName);
    }

    /// <summary>"Everything was already in place — starting Skyrim now".</summary>
    public string Title { get; }

    /// <summary>The profile, and the save that was loaded when there is one.</summary>
    public string Detail { get; }

    /// <summary>What was checked, for the Details dialog.</summary>
    public IReadOnlyList<LaunchCheckLine> Checks { get; }

    [RelayCommand]
    private Task ShowDetailsAsync() => _dialogs.ShowAsync(new LaunchChecksDialogViewModel(Checks, _text));

    private List<LaunchCheckLine> ChecksFor(LaunchPreview preview, string? saveName)
    {
        var install = LaunchText.IsUnknown(preview.TargetInstall)
            ? _text.Message("launch.checks.installPlain")
            : _text.Message("launch.checks.install", ("state", LaunchText.InstallState(_text, preview.TargetInstall)));
        var checks = new List<LaunchCheckLine> { new(Icons.CheckCircle, install, preview.Install.Path) };

        if (preview.External is { } external)
        {
            var saves = saveName is null
                ? _text.Message("launch.checks.savesPlain")
                : _text.Message("launch.checks.saves", ("saveName", saveName));
            checks.Add(new LaunchCheckLine(Icons.CheckCircle, saves, external.Path));
        }

        var clean = _text.Message(preview.External is null ? "launch.checks.cleanInstall" : "launch.checks.clean");
        checks.Add(new LaunchCheckLine(Icons.CheckCircle, clean, null));
        return checks;
    }
}
