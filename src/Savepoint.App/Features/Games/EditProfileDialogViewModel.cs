using CommunityToolkit.Mvvm.ComponentModel;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Features.Games;

/// <summary>
/// Edits a profile's name, notes and version. The version field is an override: left blank, the profile shows the
/// version SavePoint detected in the game's executable.
/// </summary>
public sealed partial class EditProfileDialogViewModel : ProfileFormDialogViewModel
{
    private readonly Func<ProfileChanges, Task<OperationResult<Profile>>> _save;

    [ObservableProperty]
    private string _notes;

    [ObservableProperty]
    private string _versionOverride;

    /// <param name="name">The profile's current name.</param>
    /// <param name="notes">Its notes, or null.</param>
    /// <param name="versionOverride">The version the user typed earlier, or null; never the detected one.</param>
    /// <param name="detectedVersion">The version SavePoint detected, or null when it could not.</param>
    /// <param name="save">Saves the changes through the Api.</param>
    /// <param name="text">Formats the dialog's copy.</param>
    public EditProfileDialogViewModel(
        string name,
        string? notes,
        string? versionOverride,
        string? detectedVersion,
        Func<ProfileChanges, Task<OperationResult<Profile>>> save,
        UiFormatter text)
        : base(text.Message("games.editProfile.title", ("name", name)), name, text)
    {
        _save = save;
        _notes = notes ?? string.Empty;
        _versionOverride = versionOverride ?? string.Empty;
        VersionPlaceholder = string.IsNullOrWhiteSpace(detectedVersion) ? null : detectedVersion.Trim();
        VersionHelper = VersionPlaceholder is { } detected
            ? text.Message("games.editProfile.versionHelperDetected", ("detectedVersion", detected))
            : text.Message("games.editProfile.versionHelperUndetected");
    }

    /// <summary>What to say under the version field: how a blank field behaves.</summary>
    public string VersionHelper { get; }

    /// <summary>The detected version, shown faintly in an empty version field; null when there is none.</summary>
    public string? VersionPlaceholder { get; }

    /// <inheritdoc />
    protected override async Task<ErrorInfo?> SubmitAsync()
    {
        var changes = new ProfileChanges(Name.Trim(), Blank(Notes), Blank(VersionOverride));
        var saved = await _save(changes);
        return saved.Ok ? null : saved.Error;
    }

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
