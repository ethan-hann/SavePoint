using Savepoint.App.Features.Common.Dialogs;

namespace Savepoint.App.Tests.Fakes;

// Records every request and answers from queues a test fills; with a queue empty, a confirmation is accepted and a pick is cancelled.
public sealed class FakeDialogs : IDialogs
{
    private readonly Queue<bool> _confirmAnswers = [];
    private readonly Queue<string?> _folderAnswers = [];
    private readonly Queue<string?> _openFileAnswers = [];
    private readonly Queue<string?> _saveFileAnswers = [];

    public List<ConfirmRequest> Confirmations { get; } = [];

    public List<FolderPickRequest> FolderPicks { get; } = [];

    public List<OpenFilePickRequest> OpenFilePicks { get; } = [];

    public List<SaveFilePickRequest> SaveFilePicks { get; } = [];

    // Dialogs shown through ShowAsync, oldest first; a test answers one by calling the commands on its view model.
    public List<IDialogViewModel> Shown { get; } = [];

    public bool DefaultConfirmAnswer { get; set; } = true;

    public FakeDialogs AnswerConfirm(params bool[] answers)
    {
        foreach (var answer in answers)
        {
            _confirmAnswers.Enqueue(answer);
        }

        return this;
    }

    public FakeDialogs AnswerFolder(string? path)
    {
        _folderAnswers.Enqueue(path);
        return this;
    }

    public FakeDialogs AnswerOpenFile(string? path)
    {
        _openFileAnswers.Enqueue(path);
        return this;
    }

    public FakeDialogs AnswerSaveFile(string? path)
    {
        _saveFileAnswers.Enqueue(path);
        return this;
    }

    public Task<TResult> ShowAsync<TResult>(DialogViewModel<TResult> dialog)
    {
        Shown.Add(dialog);
        return dialog.Result;
    }

    public Task<bool> ConfirmAsync(ConfirmRequest request)
    {
        Confirmations.Add(request);
        return Task.FromResult(_confirmAnswers.TryDequeue(out var answer) ? answer : DefaultConfirmAnswer);
    }

    public Task<string?> PickFolderAsync(FolderPickRequest request)
    {
        FolderPicks.Add(request);
        return Task.FromResult(_folderAnswers.TryDequeue(out var answer) ? answer : null);
    }

    public Task<string?> PickOpenFileAsync(OpenFilePickRequest request)
    {
        OpenFilePicks.Add(request);
        return Task.FromResult(_openFileAnswers.TryDequeue(out var answer) ? answer : null);
    }

    public Task<string?> PickSaveFileAsync(SaveFilePickRequest request)
    {
        SaveFilePicks.Add(request);
        return Task.FromResult(_saveFileAnswers.TryDequeue(out var answer) ? answer : null);
    }
}
