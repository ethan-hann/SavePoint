using Avalonia.Platform.Storage;
using Savepoint.App.Features.Common.Formatting;

namespace Savepoint.App.Features.Common.Dialogs;

/// <summary>
/// The real <see cref="IDialogs"/>: modal dialogs go onto the window's <see cref="DialogHostViewModel"/>, and the
/// pickers use the storage provider of the window the app is showing.
/// </summary>
public sealed class AvaloniaDialogs : IDialogs
{
    private readonly DialogHostViewModel _host;
    private readonly UiFormatter _text;
    private readonly Func<IStorageProvider?> _storage;

    /// <param name="host">The stack the window's dialog overlay shows.</param>
    /// <param name="text">Supplies the default cancel label.</param>
    /// <param name="storage">The storage provider of the window to attach pickers to; null while no window exists, which makes every pick answer null.</param>
    public AvaloniaDialogs(DialogHostViewModel host, UiFormatter text, Func<IStorageProvider?> storage)
    {
        _host = host;
        _text = text;
        _storage = storage;
    }

    /// <inheritdoc />
    public Task<TResult> ShowAsync<TResult>(DialogViewModel<TResult> dialog) => _host.ShowAsync(dialog);

    /// <inheritdoc />
    public Task<bool> ConfirmAsync(ConfirmRequest request) =>
        _host.ShowAsync(new ConfirmDialogViewModel(request, _text.Message("common.cancel")));

    /// <inheritdoc />
    public async Task<string?> PickFolderAsync(FolderPickRequest request)
    {
        if (_storage() is not { CanPickFolder: true } provider)
        {
            return null;
        }

        var start = await StartFolderAsync(provider, request.StartPath);
        var picked = await provider.OpenFolderPickerAsync(StoragePickerOptions.Folder(request, start));
        return picked.Count == 0 ? null : picked[0].TryGetLocalPath();
    }

    /// <inheritdoc />
    public async Task<string?> PickOpenFileAsync(OpenFilePickRequest request)
    {
        if (_storage() is not { CanOpen: true } provider)
        {
            return null;
        }

        var start = await StartFolderAsync(provider, request.StartPath);
        var picked = await provider.OpenFilePickerAsync(StoragePickerOptions.OpenFile(request, start));
        return picked.Count == 0 ? null : picked[0].TryGetLocalPath();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> PickFoldersAsync(FolderPickRequest request)
    {
        if (_storage() is not { CanPickFolder: true } provider)
        {
            return [];
        }

        var start = await StartFolderAsync(provider, request.StartPath);
        var picked = await provider.OpenFolderPickerAsync(StoragePickerOptions.Folder(request, start, allowMultiple: true));
        return LocalPaths(picked);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> PickFilesAsync(OpenFilePickRequest request)
    {
        if (_storage() is not { CanOpen: true } provider)
        {
            return [];
        }

        var start = await StartFolderAsync(provider, request.StartPath);
        var picked = await provider.OpenFilePickerAsync(StoragePickerOptions.OpenFile(request, start, allowMultiple: true));
        return LocalPaths(picked);
    }

    /// <inheritdoc />
    public async Task<string?> PickSaveFileAsync(SaveFilePickRequest request)
    {
        if (_storage() is not { CanSave: true } provider)
        {
            return null;
        }

        var start = await StartFolderAsync(provider, request.StartPath);
        var picked = await provider.SaveFilePickerAsync(StoragePickerOptions.SaveFile(request, start));
        return picked?.TryGetLocalPath();
    }

    // An item with no local path (a cloud location, say) cannot be a path in the game folder, so it is dropped.
    private static List<string> LocalPaths(IEnumerable<IStorageItem> items) => [.. items.Select(item => item.TryGetLocalPath()).OfType<string>()];

    private static async Task<IStorageFolder?> StartFolderAsync(IStorageProvider provider, string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : await provider.TryGetFolderFromPathAsync(path);
}
