using Savepoint.App.Features.Common.Dialogs;

namespace Savepoint.App.Tests.Fakes;

// Records every request and answers from queues a test fills; with a queue empty, a pick is cancelled and a confirmation fails the test
// unless DefaultConfirmAnswer says what to answer.
public sealed class FakeDialogs : IDialogs
{
    private readonly Queue<bool> _confirmAnswers = [];
    private readonly Queue<string?> _folderAnswers = [];
    private readonly Queue<string?> _openFileAnswers = [];
    private readonly Queue<string?> _saveFileAnswers = [];
    private readonly Queue<string[]> _foldersAnswers = [];
    private readonly Queue<string[]> _filesAnswers = [];

    public FakeDialogs()
    {
        FailOnUnansweredConfirmationAttribute.Track(this);
    }

    public List<ConfirmRequest> Confirmations { get; } = [];

    // The confirmations that had no answer, so the test fails when it ends even if the screen's command task, which carries the failure,
    // was never awaited. A test that provokes one on purpose clears the list.
    public List<ConfirmRequest> Unanswered { get; } = [];

    public List<FolderPickRequest> FolderPicks { get; } = [];

    public List<OpenFilePickRequest> OpenFilePicks { get; } = [];

    // The several-folder and several-file picks, kept apart from the single ones above.
    public List<FolderPickRequest> FoldersPicks { get; } = [];

    public List<OpenFilePickRequest> FilesPicks { get; } = [];

    public List<SaveFilePickRequest> SaveFilePicks { get; } = [];

    // Dialogs shown through ShowAsync, oldest first; a test answers one by calling the commands on its view model.
    public List<IDialogViewModel> Shown { get; } = [];

    // What a confirmation nobody queued an answer for gets. Null, the default, fails the test: a destructive question that is accepted
    // without anyone having said yes would pass whether or not the screen asked it.
    public bool? DefaultConfirmAnswer { get; set; }

    // Runs while a confirmation is open, before it is answered, for a test that needs something to happen during the question.
    public Func<ConfirmRequest, Task>? WhileConfirming { get; set; }

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

    // The next several-folder pick answers with these folders; with none queued, it is cancelled.
    public FakeDialogs AnswerFolders(params string[] paths)
    {
        _foldersAnswers.Enqueue(paths);
        return this;
    }

    // The next several-file pick answers with these files; with none queued, it is cancelled.
    public FakeDialogs AnswerFiles(params string[] paths)
    {
        _filesAnswers.Enqueue(paths);
        return this;
    }

    public FakeDialogs AnswerSaveFile(string? path)
    {
        _saveFileAnswers.Enqueue(path);
        return this;
    }

    // Runs as each ShowAsync dialog opens, for a test that answers it by pressing one of its buttons.
    public Action<IDialogViewModel>? OnShown { get; set; }

    public Task<TResult> ShowAsync<TResult>(DialogViewModel<TResult> dialog)
    {
        Shown.Add(dialog);
        OnShown?.Invoke(dialog);
        return dialog.Result;
    }

    public async Task<bool> ConfirmAsync(ConfirmRequest request)
    {
        Confirmations.Add(request);
        if (WhileConfirming is { } during)
        {
            await during(request);
        }

        if (_confirmAnswers.TryDequeue(out var answer))
        {
            return answer;
        }

        if (DefaultConfirmAnswer is null)
        {
            Unanswered.Add(request);
        }

        return DefaultConfirmAnswer ?? throw new Xunit.Sdk.XunitException($"The screen asked \"{request.Title}\" and the test queued no answer. Say yes or no with AnswerConfirm, or set DefaultConfirmAnswer.");
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

    public Task<IReadOnlyList<string>> PickFoldersAsync(FolderPickRequest request)
    {
        FoldersPicks.Add(request);
        return Task.FromResult<IReadOnlyList<string>>(_foldersAnswers.TryDequeue(out var answer) ? answer : []);
    }

    public Task<IReadOnlyList<string>> PickFilesAsync(OpenFilePickRequest request)
    {
        FilesPicks.Add(request);
        return Task.FromResult<IReadOnlyList<string>>(_filesAnswers.TryDequeue(out var answer) ? answer : []);
    }

    public Task<string?> PickSaveFileAsync(SaveFilePickRequest request)
    {
        SaveFilePicks.Add(request);
        return Task.FromResult(_saveFileAnswers.TryDequeue(out var answer) ? answer : null);
    }
}
