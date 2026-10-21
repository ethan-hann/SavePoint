using Avalonia.Platform.Storage;

namespace Savepoint.App.Features.Common.Dialogs;

/// <summary>Turns picker requests into the option objects of Avalonia's storage provider.</summary>
public static class StoragePickerOptions
{
    /// <summary>Options for a single-folder pick that opens at <paramref name="start"/> when it is not null.</summary>
    public static FolderPickerOpenOptions Folder(FolderPickRequest request, IStorageFolder? start) => new()
    {
        Title = request.Title,
        AllowMultiple = false,
        SuggestedStartLocation = start,
    };

    /// <summary>Options for a single-file open pick; no filter list means any file.</summary>
    public static FilePickerOpenOptions OpenFile(OpenFilePickRequest request, IStorageFolder? start) => new()
    {
        Title = request.Title,
        AllowMultiple = false,
        SuggestedStartLocation = start,
        FileTypeFilter = Types(request.Filters),
    };

    /// <summary>Options for a save pick; the first filter's first pattern gives the default extension.</summary>
    public static FilePickerSaveOptions SaveFile(SaveFilePickRequest request, IStorageFolder? start) => new()
    {
        Title = request.Title,
        SuggestedFileName = request.SuggestedName,
        SuggestedStartLocation = start,
        FileTypeChoices = Types(request.Filters),
        DefaultExtension = DefaultExtension(request.Filters),
        ShowOverwritePrompt = true,
    };

    private static List<FilePickerFileType>? Types(IReadOnlyList<FileTypeFilter>? filters) =>
        filters is { Count: > 0 }
            ? [.. filters.Select(filter => new FilePickerFileType(filter.Name) { Patterns = [.. filter.Patterns] })]
            : null;

    // "*.json" gives "json"; a pattern that is not a plain extension gives none.
    private static string? DefaultExtension(IReadOnlyList<FileTypeFilter>? filters)
    {
        var pattern = filters?.FirstOrDefault()?.Patterns.FirstOrDefault();
        return pattern is not null && pattern.StartsWith("*.", StringComparison.Ordinal) && pattern.IndexOfAny(['*', '?'], 2) < 0
            ? pattern[2..]
            : null;
    }
}
