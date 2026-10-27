namespace Savepoint.App.Features.Common.Dialogs;

/// <summary>
/// Everything a view model may ask of the window: modal dialogs, confirmations, and the operating system's file and
/// folder pickers. Texts in the requests are already localized by the caller. The fake in the test project and the
/// Avalonia implementation (<see cref="AvaloniaDialogs"/>) are the two implementations.
/// </summary>
public interface IDialogs
{
    /// <summary>Shows a dialog above the window and answers with its result once it closes.</summary>
    Task<TResult> ShowAsync<TResult>(DialogViewModel<TResult> dialog);

    /// <summary>Asks the user to confirm something; true only when they chose the confirm button (and typed the required word, when one is asked for).</summary>
    Task<bool> ConfirmAsync(ConfirmRequest request);

    /// <summary>Opens the operating system's folder picker; answers with the local path, or null when the user cancelled.</summary>
    Task<string?> PickFolderAsync(FolderPickRequest request);

    /// <summary>Opens the operating system's open-file picker; answers with the local path, or null when the user cancelled.</summary>
    Task<string?> PickOpenFileAsync(OpenFilePickRequest request);

    /// <summary>Opens the operating system's folder picker with several folders selectable; answers with their local paths, or an empty list when the user cancelled.</summary>
    Task<IReadOnlyList<string>> PickFoldersAsync(FolderPickRequest request);

    /// <summary>Opens the operating system's open-file picker with several files selectable; answers with their local paths, or an empty list when the user cancelled.</summary>
    Task<IReadOnlyList<string>> PickFilesAsync(OpenFilePickRequest request);

    /// <summary>Opens the operating system's save-file picker; answers with the local path to write, or null when the user cancelled.</summary>
    Task<string?> PickSaveFileAsync(SaveFilePickRequest request);
}
