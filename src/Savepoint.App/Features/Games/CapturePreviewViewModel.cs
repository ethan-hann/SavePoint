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
        Rows = [.. preview.Folders.Select(Row)];
        Note = preview.NewBytesEstimate > 0
            ? _text.Message("profiles.wizard.preview.newBytesNote", ("newSize", _text.FileSize(preview.NewBytesEstimate)))
            : string.Empty;
        Error = string.Empty;
        IsLoading = false;
    }

    internal void Fail()
    {
        Rows = [];
        Note = string.Empty;
        Error = _text.Message("profiles.wizard.preview.failed");
        IsLoading = false;
    }

    private CaptureRowViewModel Row(FolderCapturePreview folder) => folder.Role == RepoRole.Install
        ? new CaptureRowViewModel(Icons.Folder, _text.Message("profiles.wizard.preview.installTitle"), InstallSubtitle(folder))
        : new CaptureRowViewModel(
            Icons.Save,
            _text.Message("profiles.wizard.preview.externalTitle"),
            _text.Message("profiles.wizard.preview.externalSubtitle", ("path", folder.Path), ("fileCount", folder.FileCount)));

    private string InstallSubtitle(FolderCapturePreview folder) => folder.ModFileCount > 0
        ? _text.Message("profiles.wizard.preview.installSubtitle", ("path", folder.Path), ("modFileCount", folder.ModFileCount), ("size", _text.FileSize(folder.Bytes)))
        : _text.Message("profiles.wizard.preview.installSubtitleNoMods", ("path", folder.Path), ("size", _text.FileSize(folder.Bytes)));
}
