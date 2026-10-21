using CommunityToolkit.Mvvm.ComponentModel;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.Launch;

/// <summary>One radio card of the post-play dialog: an icon, a title, what choosing it does, and for the choices that name something, a name field.</summary>
public sealed partial class PostPlayChoiceViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNameField))]
    private bool _isSelected;

    [ObservableProperty]
    private string _name;

    /// <param name="kind">Which completion the card stands for.</param>
    /// <param name="icon">A Material Symbols glyph.</param>
    /// <param name="title">The card's headline.</param>
    /// <param name="subtitle">What choosing it does.</param>
    /// <param name="nameLabel">The field's label, or null for a card with no field.</param>
    /// <param name="suggestedName">What the field starts with.</param>
    public PostPlayChoiceViewModel(CompletionChoice kind, string icon, string title, string subtitle, string? nameLabel = null, string suggestedName = "")
    {
        Kind = kind;
        Icon = icon;
        Title = title;
        Subtitle = subtitle;
        NameLabel = nameLabel;
        SuggestedName = suggestedName;
        _name = suggestedName;
    }

    /// <summary>Which completion the card stands for.</summary>
    public CompletionChoice Kind { get; }

    /// <summary>A Material Symbols glyph.</summary>
    public string Icon { get; }

    /// <summary>The card's headline.</summary>
    public string Title { get; }

    /// <summary>What choosing it does.</summary>
    public string Subtitle { get; }

    /// <summary>The field's label ("Name it:"), or null when the card has no field.</summary>
    public string? NameLabel { get; }

    /// <summary>What the name field starts with, and what a blank name falls back to.</summary>
    public string SuggestedName { get; }

    /// <summary>True when the card has a name field.</summary>
    public bool HasNameField => NameLabel is not null;

    /// <summary>True while the card is selected and has a field, which is when the field is shown.</summary>
    public bool ShowNameField => IsSelected && HasNameField;

    /// <summary>The name to use: what was typed, trimmed, or the suggestion when it is blank.</summary>
    public string EffectiveName => string.IsNullOrWhiteSpace(Name) ? SuggestedName : Name.Trim();
}
