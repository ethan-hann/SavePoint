using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Features.Launch;

/// <summary>
/// The "unsaved changes" callout for one folder in the launch dialog: how many files changed, "Save them first" when
/// the changes belong to a profile, and "Discard them" (which asks again). It stays until the changes are resolved.
/// </summary>
public sealed partial class UnsavedChangesBlockViewModel : ObservableObject
{
    private readonly Func<UnsavedChangesBlockViewModel, Task> _saveFirst;
    private readonly Func<UnsavedChangesBlockViewModel, Task> _discard;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveFirstCommand), nameof(DiscardCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    /// <param name="role">The folder the changes are in.</param>
    /// <param name="title">The callout's headline.</param>
    /// <param name="body">How many files changed, and what happens to them if nothing is done.</param>
    /// <param name="canSaveFirst">Whether the changes belong to a profile, which is what "Save them first" needs.</param>
    /// <param name="saveFirst">Saves the changes into their profile.</param>
    /// <param name="discard">Discards the changes after confirming.</param>
    public UnsavedChangesBlockViewModel(
        RepoRole role,
        string title,
        string body,
        bool canSaveFirst,
        Func<UnsavedChangesBlockViewModel, Task> saveFirst,
        Func<UnsavedChangesBlockViewModel, Task> discard)
    {
        Role = role;
        Title = title;
        Body = body;
        CanSaveFirst = canSaveFirst;
        _saveFirst = saveFirst;
        _discard = discard;
    }

    /// <summary>The folder the changes are in.</summary>
    public RepoRole Role { get; }

    /// <summary>The callout's headline.</summary>
    public string Title { get; }

    /// <summary>How many files changed, and what happens to them if nothing is done.</summary>
    public string Body { get; }

    /// <summary>True when "Save them first" is offered: the changes belong to a known profile.</summary>
    public bool CanSaveFirst { get; }

    /// <summary>True when the last attempt failed and <see cref="Error"/> says why.</summary>
    public bool HasError => Error is not null;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task SaveFirstAsync() => _saveFirst(this);

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task DiscardAsync() => _discard(this);

    private bool CanAct() => !IsBusy;
}
