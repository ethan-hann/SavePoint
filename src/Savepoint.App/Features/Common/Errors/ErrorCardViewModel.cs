using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.Core.Errors;

namespace Savepoint.App.Features.Common.Errors;

/// <summary>A button on an error card.</summary>
/// <param name="Label">The button text, already localized.</param>
/// <param name="Command">What it does.</param>
/// <param name="IsPrimary">Draws the button filled; the card's one recommended action.</param>
public sealed record ErrorCardAction(string Label, ICommand Command, bool IsPrimary = false);

/// <summary>
/// The view model of an error card: an icon, a headline, a plain-language body, the path involved on a
/// recessed monospace surface, optional extra content and actions, and a "Show details" toggle that reveals the
/// developer detail in a selectable box. Build one from an <see cref="ErrorInfo"/> with the constructor, or from
/// scratch with <see cref="Custom"/> for a card that is not one error (such as "Only part of the snapshot was taken").
/// </summary>
public sealed partial class ErrorCardViewModel : ObservableObject
{
    private readonly UiFormatter _text;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailsToggleLabel))]
    private bool _showDetails;

    /// <summary>Builds the card for an error: headline, icon and tone from <see cref="ErrorPresentation"/>, body from the error's message.</summary>
    /// <param name="error">The error to show.</param>
    /// <param name="text">Formats the message and the card's own labels.</param>
    /// <param name="actions">Buttons to show, in order; at most one should be primary.</param>
    /// <param name="supplement">A view model for extra content between the path and the buttons; its view is found by <see cref="ViewLocator"/>.</param>
    public ErrorCardViewModel(ErrorInfo error, UiFormatter text, IReadOnlyList<ErrorCardAction>? actions = null, object? supplement = null)
    {
        _text = text;
        var kind = ErrorPresentation.For(error.MessageId);
        Icon = kind.Icon;
        Tone = kind.Tone;
        Title = text.Message(kind.TitleKey);
        Body = text.Message(error);
        Path = PathFor(error, text);
        Detail = string.IsNullOrWhiteSpace(error.Detail) ? null : error.Detail;
        Actions = actions ?? [];
        Supplement = supplement;
    }

    private ErrorCardViewModel(UiFormatter text, string icon, ErrorTone tone, string title, string body, string? path, string? detail, IReadOnlyList<ErrorCardAction>? actions, object? supplement)
    {
        _text = text;
        Icon = icon;
        Tone = tone;
        Title = title;
        Body = body;
        Path = string.IsNullOrEmpty(path) ? null : path;
        Detail = string.IsNullOrWhiteSpace(detail) ? null : detail;
        Actions = actions ?? [];
        Supplement = supplement;
    }

    /// <summary>
    /// What the card's recessed surface shows for an error: the folder or file it names, or for a full disk on Windows the drive to
    /// clear space on, which is what a person can act on; null when the error names no path.
    /// </summary>
    /// <param name="error">The error.</param>
    /// <param name="text">Tells which operating system the copy is for.</param>
    public static string? PathFor(ErrorInfo error, UiFormatter text)
    {
        if (!error.Args.TryGetValue("path", out var value) || value is not string { Length: > 0 } path)
        {
            return null;
        }

        return error.MessageId == "error.diskSpace.low" && text.Os == "windows" ? UiFormatter.Drive(path) ?? path : path;
    }

    /// <summary>The Material Symbols glyph.</summary>
    public string Icon { get; private set; }

    /// <summary>Which color the icon takes.</summary>
    public ErrorTone Tone { get; private set; }

    /// <summary>The headline.</summary>
    public string Title { get; private set; }

    /// <summary>What happened and what to do about it.</summary>
    public string Body { get; private set; }

    /// <summary>The folder or file involved, shown on its own surface; null when the error names none.</summary>
    public string? Path { get; private set; }

    /// <summary>True when <see cref="Path"/> is set.</summary>
    public bool HasPath => Path is not null;

    /// <summary>The developer detail behind "Show details"; null when there is none.</summary>
    public string? Detail { get; private set; }

    /// <summary>True when there is detail to reveal, which is when the toggle is offered.</summary>
    public bool HasDetail => Detail is not null;

    /// <summary>Whether the card draws its icon in the error color.</summary>
    public bool IsError => Tone == ErrorTone.Error;

    /// <summary>Whether the card draws its icon in the warning color.</summary>
    public bool IsWarning => Tone == ErrorTone.Warning;

    /// <summary>Whether the card draws its icon in a neutral color.</summary>
    public bool IsNeutral => Tone == ErrorTone.Neutral;

    /// <summary>"Show details" or "Hide details", following <see cref="ShowDetails"/>.</summary>
    public string DetailsToggleLabel => ShowDetails ? _text.Message("errorCard.hideDetails") : _text.Message("errorCard.showDetails");

    /// <summary>The buttons, in order.</summary>
    public IReadOnlyList<ErrorCardAction> Actions { get; }

    /// <summary>Extra content between the path and the buttons, or null.</summary>
    public object? Supplement { get; }

    /// <summary>Builds a card that is not a single error.</summary>
    public static ErrorCardViewModel Custom(
        UiFormatter text,
        string icon,
        ErrorTone tone,
        string title,
        string body,
        string? path = null,
        string? detail = null,
        IReadOnlyList<ErrorCardAction>? actions = null,
        object? supplement = null) =>
        new(text, icon, tone, title, body, path, detail, actions, supplement);

    [RelayCommand]
    private void ToggleDetails() => ShowDetails = !ShowDetails;
}
