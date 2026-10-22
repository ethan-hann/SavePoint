using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.Core.Errors;

namespace Savepoint.App.Features.Games;

/// <summary>
/// Base of the dialogs that edit or copy a profile: a name field, a Save button that does the work through the Api,
/// and an inline message when the Api refuses. The dialog stays open on a failure so nothing the user typed is lost,
/// and it answers true once the change went through.
/// </summary>
public abstract partial class ProfileFormDialogViewModel : DialogViewModel<bool>
{
    private readonly UiFormatter _text;

    /// <summary>The name field.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name;

    /// <summary>Why the Api refused the change, shown under the fields; null when nothing is wrong.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    /// <summary>True while the Api call runs, which locks the dialog.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanDismiss))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isSaving;

    /// <param name="title">The dialog's title.</param>
    /// <param name="name">The name field's first value.</param>
    /// <param name="text">Formats the Api's error messages.</param>
    protected ProfileFormDialogViewModel(string title, string name, UiFormatter text)
        : base(title)
    {
        _name = name;
        _text = text;
    }

    /// <summary>True when the name has something other than spaces in it and no save is running.</summary>
    public bool CanSave => !IsSaving && !string.IsNullOrWhiteSpace(Name);

    /// <summary>True when <see cref="Error"/> has a message to show.</summary>
    public bool HasError => !string.IsNullOrEmpty(Error);

    /// <inheritdoc />
    public override bool CanDismiss => !IsSaving;

    /// <inheritdoc />
    public abstract override string Description { get; }

    /// <inheritdoc />
    public override double DialogWidth => 480;

    /// <inheritdoc />
    protected override bool DismissResult => false;

    /// <summary>Does the work through the Api; the error it answers with is shown inline, and null means it went through.</summary>
    protected abstract Task<ErrorInfo?> SubmitAsync();

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        // A command's Execute runs even when CanExecute is false.
        if (!CanSave)
        {
            return;
        }

        IsSaving = true;
        Error = null;
        ErrorInfo? error;
        try
        {
            error = await SubmitAsync();
        }
        finally
        {
            IsSaving = false;
        }

        if (error is null)
        {
            Close(true);
        }
        else
        {
            Error = Describe(error);
        }
    }

    [RelayCommand]
    private void Cancel() => Close(false);

    // A validation refusal names what is wrong with each field, which says more than the general line does.
    private string Describe(ErrorInfo error)
    {
        if (error.Args.TryGetValue("issues", out var raw) && raw is IReadOnlyList<ValidationIssue> { Count: > 0 } issues)
        {
            try
            {
                return string.Join(' ', issues.Select(issue => _text.Message(issue.MessageId)));
            }
            catch (KeyNotFoundException)
            {
                // An issue newer than this app's catalog falls back to the general line.
            }
        }

        return _text.Message(error);
    }
}
