using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Formatting;

namespace Savepoint.App.Features.Common.Search;

/// <summary>
/// The state of one search box: what was typed, the query read from it, a Clear command and a screen-reader announcement of how
/// many entries are left. The owner narrows its list when <see cref="Changed"/> is raised, at once on every keystroke, and
/// hands the box the count to say, which the box holds back until typing pauses.
/// </summary>
public sealed partial class SearchBoxViewModel : ObservableObject, IDisposable
{
    /// <summary>How long typing has to pause before the count is announced, so a screen reader is not read every step of a word.</summary>
    public static readonly TimeSpan AnnouncePause = TimeSpan.FromMilliseconds(600);

    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _clock;
    private ITimer? _pause;
    private int _generation;

    /// <summary>What was typed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasText))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    private string _text = string.Empty;

    /// <summary>The count a live region says; set once typing has paused.</summary>
    [ObservableProperty]
    private string _announcement = string.Empty;

    /// <param name="text">Supplies the Clear button's name.</param>
    /// <param name="dispatcher">Brings the announcement timer back to the UI thread.</param>
    /// <param name="clock">Times the pause before an announcement.</param>
    /// <param name="label">The box's accessible name and placeholder, such as "Search games".</param>
    /// <param name="shortcut">The key that moves to the box, as the player's system names it (such as "Ctrl+F"); null when no key does.</param>
    public SearchBoxViewModel(UiFormatter text, IUiDispatcher dispatcher, TimeProvider clock, string label, string? shortcut = null)
    {
        _dispatcher = dispatcher;
        _clock = clock;
        Label = label;
        ClearName = text.Message("search.clear");
        Shortcut = shortcut;
        Tip = shortcut is null ? null : text.Message("search.tip", ("label", label), ("shortcut", shortcut));
    }

    /// <summary>The key that moves to the box (such as "Ctrl+F"), for assistive technology; null when no key does.</summary>
    public string? Shortcut { get; }

    /// <summary>The box's tooltip: its name and its key, such as "Search profiles (Ctrl+F)"; null when no key moves to it.</summary>
    public string? Tip { get; }

    /// <summary>Raised after <see cref="Text"/> or <see cref="Query"/> changed.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised when <see cref="ClearCommand"/> ran, so the view can put the cursor back in the box: the button that ran it (a "Clear
    /// search" under a "no matches" message) goes away with the text, and focus would be lost.
    /// </summary>
    public event EventHandler? Cleared;

    /// <summary>The box's accessible name and placeholder.</summary>
    public string Label { get; }

    /// <summary>The Clear button's accessible name.</summary>
    public string ClearName { get; }

    /// <summary>What was typed, read into words; matches everything while the box is empty.</summary>
    public SearchQuery Query { get; private set; } = SearchQuery.Empty;

    /// <summary>True while something is typed, which is when the Clear button shows.</summary>
    public bool HasText => Text.Length > 0;

    /// <summary>Says a message once typing has paused for <see cref="AnnouncePause"/>; a newer message replaces one still waiting.</summary>
    /// <param name="message">What to say, such as "3 of 120 games".</param>
    public void AnnounceAfterPause(string message)
    {
        _pause?.Dispose();
        var generation = ++_generation;
        _pause = _clock.CreateTimer(_ => _dispatcher.Post(() => Say(generation, message)), null, AnnouncePause, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Asks the view to put the cursor back in the box, for an owner whose own clear action ran from a button that goes away with the
    /// search (the box's <see cref="ClearCommand"/> does this itself).
    /// </summary>
    public void ReturnFocus() => Cleared?.Invoke(this, EventArgs.Empty);

    /// <summary>Stops a pending announcement.</summary>
    public void Dispose()
    {
        _generation++;
        _pause?.Dispose();
        _pause = null;
    }

    /// <summary>Empties the box.</summary>
    [RelayCommand(CanExecute = nameof(HasText))]
    private void Clear()
    {
        Text = string.Empty;
        Cleared?.Invoke(this, EventArgs.Empty);
    }

    partial void OnTextChanged(string value)
    {
        Query = SearchQuery.Parse(value);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Emptied first so a count identical to the last one is still announced.
    private void Say(int generation, string message)
    {
        if (generation != _generation)
        {
            return;
        }

        Announcement = string.Empty;
        Announcement = message;
    }
}
