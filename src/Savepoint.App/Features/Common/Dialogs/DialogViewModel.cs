using CommunityToolkit.Mvvm.ComponentModel;

namespace Savepoint.App.Features.Common.Dialogs;

/// <summary>
/// Base for a modal dialog's view model. The dialog is shown with <see cref="IDialogs.ShowAsync{TResult}"/> and ends
/// when the view model calls <see cref="Close"/>; the task from <see cref="Result"/> then completes with that value.
/// </summary>
/// <typeparam name="TResult">What the dialog answers with: a bool for a confirmation, the entered value for a prompt.</typeparam>
public abstract class DialogViewModel<TResult> : ObservableObject, IDialogViewModel
{
    private readonly TaskCompletionSource<TResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <param name="title">The dialog's accessible name, already localized.</param>
    protected DialogViewModel(string title)
    {
        Title = title;
    }

    /// <inheritdoc />
    public event EventHandler? Closed;

    /// <inheritdoc />
    public string Title { get; }

    /// <inheritdoc />
    public virtual bool CanDismiss => true;

    /// <inheritdoc />
    public virtual double DialogWidth => 440;

    /// <inheritdoc />
    public bool IsClosed { get; private set; }

    /// <summary>Completes with the value passed to <see cref="Close"/> (or the dismiss result) when the dialog ends.</summary>
    public Task<TResult> Result => _completion.Task;

    /// <summary>What Escape and the scrim answer with when <see cref="CanDismiss"/> is true.</summary>
    protected abstract TResult DismissResult { get; }

    /// <inheritdoc />
    public void RequestDismiss()
    {
        if (CanDismiss)
        {
            Close(DismissResult);
        }
    }

    /// <summary>Ends the dialog with an answer. Closing twice keeps the first answer.</summary>
    protected void Close(TResult result)
    {
        if (IsClosed)
        {
            return;
        }

        IsClosed = true;
        _completion.TrySetResult(result);
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
