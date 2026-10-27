namespace Savepoint.App.Features.Editor;

// What a screen reader announces for a card's heading: the card's name and how much it matters, which sighted players see as a tag beside it.
public sealed partial class GameEditorViewModel
{
    /// <summary>The Game card's heading as a screen reader names it, with its "Required" tag.</summary>
    public string GameCardAutomationName => CardName("gameEditor.card.game", "gameEditor.tag.required");

    /// <summary>The Saves card's heading as a screen reader names it, with its "Recommended" tag.</summary>
    public string SavesCardAutomationName => CardName("gameEditor.card.saves", "gameEditor.tag.recommended");

    /// <summary>The Mods card's heading as a screen reader names it, with its "Optional" tag.</summary>
    public string ModsCardAutomationName => CardName("gameEditor.card.mods", "gameEditor.tag.optional");

    /// <summary>The Artwork card's heading as a screen reader names it, with its "Optional" tag.</summary>
    public string ArtworkCardAutomationName => CardName("gameEditor.artwork.label", "gameEditor.tag.optional");

    private string CardName(string cardKey, string tagKey) =>
        _text.Message("gameEditor.card.named", ("card", _text.Message(cardKey)), ("tag", _text.Message(tagKey)));
}
