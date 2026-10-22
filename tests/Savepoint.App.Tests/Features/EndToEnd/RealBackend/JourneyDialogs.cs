using Savepoint.App.Features.Common.Dialogs;

namespace Savepoint.App.Tests.Features.EndToEnd.RealBackend;

// The dialogs of a journey: dialogs and confirmations go onto the window's real dialog host, where the test answers them through
// their own commands, and only the operating system's pickers, which a headless run cannot open, answer from a script.
internal sealed class JourneyDialogs : IDialogs
{
    private readonly Queue<string?> _folders = [];
    private IDialogs? _host;

    public List<FolderPickRequest> FolderPicks { get; } = [];

    // The dialogs are built before the window's dialog host exists, so the host is attached once the app has been composed.
    public void Attach(IDialogs host) => _host = host;

    public JourneyDialogs AnswerFolder(string? path)
    {
        _folders.Enqueue(path);
        return this;
    }

    public Task<TResult> ShowAsync<TResult>(DialogViewModel<TResult> dialog) => Host.ShowAsync(dialog);

    public Task<bool> ConfirmAsync(ConfirmRequest request) => Host.ConfirmAsync(request);

    public Task<string?> PickFolderAsync(FolderPickRequest request)
    {
        FolderPicks.Add(request);
        return Task.FromResult(_folders.TryDequeue(out var answer) ? answer : null);
    }

    public Task<string?> PickOpenFileAsync(OpenFilePickRequest request) => Task.FromResult<string?>(null);

    public Task<string?> PickSaveFileAsync(SaveFilePickRequest request) => Task.FromResult<string?>(null);

    private IDialogs Host => _host ?? throw new InvalidOperationException("The app has not been composed yet.");
}
