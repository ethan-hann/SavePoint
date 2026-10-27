using CommunityToolkit.Mvvm.ComponentModel;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Features.Games;

/// <summary>One folder in the "What goes in the first snapshot" panel.</summary>
/// <param name="Icon">A Material Symbols glyph: a folder for the game, a disk for the saves.</param>
/// <param name="Title">What the folder holds ("Game version and mods").</param>
/// <param name="Subtitle">Its path and how much is in it, shown in monospace.</param>
public sealed record CaptureRowViewModel(string Icon, string Title, string Subtitle);

/// <summary>
/// The "What goes in the first snapshot" panel of the New profile wizard: each tracked folder with its size, and what saving
/// will really add to storage after SavePoint reuses the files it already has.
/// </summary>
public sealed partial class CapturePreviewViewModel : ObservableObject
{
    private readonly UiFormatter _text;
    private CapturePreview? _preview;
    private IReadOnlySet<RepoRole> _empty = new HashSet<RepoRole>();

    /// <summary>True until the preview has been read or has failed.</summary>
    [ObservableProperty]
    private bool _isLoading = true;

    /// <summary>The folders that will be captured.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRows))]
    private IReadOnlyList<CaptureRowViewModel> _rows = [];

    /// <summary>What saving will really add to storage after reuse; empty when that is nothing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    private string _note = string.Empty;

    /// <summary>Why the preview could not be read; empty when it could.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _error = string.Empty;

    internal CapturePreviewViewModel(UiFormatter text)
    {
        _text = text;
    }

    /// <summary>True when there are folders to list.</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>True when there is a line about storage to show.</summary>
    public bool HasNote => Note.Length > 0;

    /// <summary>True when the preview could not be read.</summary>
    public bool HasError => Error.Length > 0;

    internal void Show(CapturePreview preview)
    {
        _preview = preview;
        Rows = [.. preview.Folders.Select(Row)];
        Note = preview.NewBytesEstimate > 0
            ? _text.Message("profiles.wizard.preview.newBytesNote", ("newSize", _text.FileSize(preview.NewBytesEstimate)))
            : string.Empty;
        Error = string.Empty;
        IsLoading = false;
    }

    /// <summary>True once the preview was read: a box's reason is only worth giving when the answer is known.</summary>
    internal bool IsRead => _preview is not null;

    /// <summary>True when the folder's current files are already in a snapshot, so the new profile may start it empty. False when unknown.</summary>
    /// <param name="role">The saves or mods folder.</param>
    internal bool IsHeld(RepoRole role) => _preview?.Folders.Any(folder => folder.Role == role && folder.HeldInSnapshot) == true;

    internal void Fail()
    {
        _preview = null;
        Rows = [];
        Note = string.Empty;
        Error = _text.Message("profiles.wizard.preview.failed");
        IsLoading = false;
    }

    /// <summary>Marks the folders the new profile starts with nothing in, so their rows say so instead of counting files.</summary>
    /// <param name="folders">The saves and mods folders that start empty.</param>
    internal void StartEmpty(IReadOnlySet<RepoRole> folders)
    {
        _empty = folders;
        if (_preview is { } preview)
        {
            Rows = [.. preview.Folders.Select(Row)];
        }
    }

    private CaptureRowViewModel Row(FolderCapturePreview folder) => folder.Role switch
    {
        RepoRole.Install => new CaptureRowViewModel(Icons.Folder, _text.Message("profiles.wizard.preview.installTitle"), InstallSubtitle(folder)),
        RepoRole.Mods when _empty.Contains(RepoRole.Mods) => new CaptureRowViewModel(
            Icons.Folder,
            _text.Message("profiles.wizard.preview.modsTitle"),
            _text.Message("profiles.wizard.preview.modsEmptySubtitle", ("path", folder.Path))),
        RepoRole.Mods => new CaptureRowViewModel(
            Icons.Folder,
            _text.Message("profiles.wizard.preview.modsTitle"),
            _text.Message("profiles.wizard.preview.modsSubtitle", ("path", folder.Path), ("fileCount", folder.FileCount), ("size", _text.FileSize(folder.Bytes)))),
        _ when _empty.Contains(RepoRole.External) => new CaptureRowViewModel(
            Icons.Save,
            _text.Message("profiles.wizard.preview.externalEmptyTitle"),
            _text.Message("profiles.wizard.preview.externalEmptySubtitle", ("path", folder.Path))),
        _ => new CaptureRowViewModel(
            Icons.Save,
            _text.Message("profiles.wizard.preview.externalTitle"),
            _text.Message("profiles.wizard.preview.externalSubtitle", ("path", folder.Path), ("fileCount", folder.FileCount))),
    };

    private string InstallSubtitle(FolderCapturePreview folder) => folder.ModFileCount > 0
        ? _text.Message("profiles.wizard.preview.installSubtitle", ("path", folder.Path), ("modFileCount", folder.ModFileCount), ("size", _text.FileSize(folder.Bytes)))
        : _text.Message("profiles.wizard.preview.installSubtitleNoMods", ("path", folder.Path), ("size", _text.FileSize(folder.Bytes)));
}
