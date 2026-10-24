using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Accessibility;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.Library;

/// <summary>What kind of line the result list shows.</summary>
public enum LibraryResultKind
{
    /// <summary>A game was added but the player has something left to do.</summary>
    Notice,

    /// <summary>A game was not added.</summary>
    NotAdded,

    /// <summary>A note about the run itself, such as games not tried after a stop.</summary>
    Info,
}

/// <summary>One line of the result list.</summary>
/// <param name="Kind">What the line is about.</param>
/// <param name="Text">The line, in plain words.</param>
public sealed record LibraryResultLine(LibraryResultKind Kind, string Text);

/// <summary>How a run of adding library games ended.</summary>
/// <param name="Results">One result for each game tried, in order.</param>
/// <param name="NotTried">How many games were left alone because the player stopped.</param>
public sealed record LibraryAddSummary(IReadOnlyList<LibraryAddResult> Results, int NotTried)
{
    /// <summary>The results of the games that were added.</summary>
    public IReadOnlyList<LibraryAddResult> Added => [.. Results.Where(result => result.Status == LibraryAddStatus.Added)];
}

/// <summary>
/// Adds the confirmed library games one after another while showing how far it is, and then lists what needs the player's attention.
/// It cannot be dismissed while it runs; "Stop after this game" ends the run between two games.
/// </summary>
public sealed partial class LibraryAddDialogViewModel : DialogViewModel<LibraryAddSummary>
{
    private readonly IGamesApi _games;
    private readonly IReadOnlyList<LibraryGame> _toAdd;
    private readonly UiFormatter _text;
    private readonly ProgressMilestones _milestones = new();
    private readonly List<LibraryAddResult> _results = [];
    private LibraryAddSummary _summary = new([], 0);
    private bool _started;
    private bool _stopRequested;

    [ObservableProperty]
    private string _heading;

    [ObservableProperty]
    private string _progressText;

    [ObservableProperty]
    private double _percentValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(CanDismiss))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    private bool _isFinished;

    [ObservableProperty]
    private bool _isStopping;

    [ObservableProperty]
    private string _announcement = string.Empty;

    [ObservableProperty]
    private string _addedSummary = string.Empty;

    [ObservableProperty]
    private string _alreadyAddedSummary = string.Empty;

    [ObservableProperty]
    private string _notAddedSummary = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<LibraryResultLine> _resultLines = [];

    /// <param name="games">The games Api that adds each game.</param>
    /// <param name="toAdd">The games to add, in order.</param>
    /// <param name="text">Formats the copy.</param>
    public LibraryAddDialogViewModel(IGamesApi games, IReadOnlyList<LibraryGame> toAdd, UiFormatter text)
        : base(text.Message("library.add.title.working"))
    {
        _games = games;
        _toAdd = toAdd;
        _text = text;
        _heading = Title;
        _progressText = text.Message("library.add.starting");
        Description = text.Message("library.add.description");
        StopLabel = text.Message("library.add.stop");
        StoppingText = text.Message("library.add.stopping");
        CloseLabel = text.Message("library.add.close");
    }

    /// <summary>"Stop after this game".</summary>
    public string StopLabel { get; }

    /// <summary>"Stopping after this game…", shown once the player asked to stop.</summary>
    public string StoppingText { get; }

    /// <summary>"Done".</summary>
    public string CloseLabel { get; }

    /// <summary>True while games are being added.</summary>
    public bool IsRunning => !IsFinished;

    /// <inheritdoc />
    public override bool CanDismiss => IsFinished;

    /// <inheritdoc />
    public override string Description { get; }

    /// <inheritdoc />
    public override double DialogWidth => 640;

    /// <inheritdoc />
    public override bool PinsFooter => true;

    /// <inheritdoc />
    protected override LibraryAddSummary DismissResult => _summary;

    /// <summary>Adds the games. Call once, as the dialog is shown; a second call does nothing.</summary>
    public async Task RunAsync()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        var tried = 0;
        foreach (var game in _toAdd)
        {
            if (_stopRequested)
            {
                break;
            }

            ProgressText = _text.Message("library.add.progress", ("done", tried + 1), ("total", _toAdd.Count), ("name", game.Name));
            var added = await _games.AddLibraryGameAsync(game).ConfigureAwait(true);
            _results.Add(added.Ok ? added.Value : new LibraryAddResult(game.Key, game.Name, LibraryAddStatus.Refused, Error: added.Error));
            tried++;
            PercentValue = 100.0 * tried / _toAdd.Count;
            if (_milestones.Crossed((int)PercentValue) is not null)
            {
                Announcement = _text.Message("library.add.progressAnnounce", ("done", tried), ("total", _toAdd.Count));
            }
        }

        Finish(_toAdd.Count - tried);
    }

    private void Finish(int notTried)
    {
        _summary = new LibraryAddSummary([.. _results], notTried);
        var added = _results.Count(r => r.Status == LibraryAddStatus.Added);
        var already = _results.Count(r => r.Status == LibraryAddStatus.AlreadyAdded);
        var notAdded = _results.Count(r => r.Status is LibraryAddStatus.Refused or LibraryAddStatus.HasOlderHistory);
        AddedSummary = _text.Message("library.add.summary.added", ("count", added));
        AlreadyAddedSummary = already > 0 ? _text.Message("library.add.summary.alreadyAdded", ("count", already)) : string.Empty;
        NotAddedSummary = notAdded > 0 ? _text.Message("library.add.summary.notAdded", ("count", notAdded)) : string.Empty;

        var lines = new List<LibraryResultLine>();
        foreach (var result in _results)
        {
            switch (result.Status)
            {
                case LibraryAddStatus.Refused:
                    lines.Add(new LibraryResultLine(
                        LibraryResultKind.NotAdded,
                        _text.Message("library.add.line.refused", ("name", result.Name), ("reason", result.Error is { } error ? _text.Message(error) : string.Empty))));
                    break;
                case LibraryAddStatus.HasOlderHistory:
                    lines.Add(new LibraryResultLine(LibraryResultKind.NotAdded, _text.Message("library.add.line.olderSnapshots", ("name", result.Name))));
                    break;
                case LibraryAddStatus.Added when result.SavesFolderLeftOut is { } path:
                    lines.Add(new LibraryResultLine(LibraryResultKind.Notice, _text.Message("library.add.line.savesLeftOut", ("name", result.Name), ("path", path))));
                    break;
            }
        }

        if (notTried > 0)
        {
            lines.Add(new LibraryResultLine(LibraryResultKind.Info, _text.Message("library.add.line.notTried", ("count", notTried))));
        }

        ResultLines = lines;
        Heading = _text.Message("library.add.title.done");
        ProgressText = Heading;
        IsFinished = true;
    }

    [RelayCommand]
    private void Stop()
    {
        _stopRequested = true;
        IsStopping = true;
    }

    [RelayCommand(CanExecute = nameof(IsFinished))]
    private void Close()
    {
        if (IsFinished)
        {
            Close(_summary);
        }
    }
}
