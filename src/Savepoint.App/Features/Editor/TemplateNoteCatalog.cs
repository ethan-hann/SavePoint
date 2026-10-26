namespace Savepoint.App.Features.Editor;

/// <summary>
/// Template notes are English sentences stored in each template's file. A screen that shows them shows the catalog's wording of each
/// known sentence instead (D53.5), so the words are translatable and follow the app's voice, and shows nothing for any other text, so
/// a definition file's own words never reach the screen as app copy.
/// </summary>
internal static class TemplateNoteCatalog
{
    private static readonly (string Sentence, string Key)[] _known =
    [
        ("Saves are in Steam's userdata folder; add the saves folder by hand.", "gameEditor.templateNote.userdata"),
        ("Saves are in a folder named after your account id; add the saves folder by hand.", "gameEditor.templateNote.accountFolder"),
        ("We couldn't confirm where this game keeps its saves; add the saves folder by hand.", "gameEditor.templateNote.unconfirmed"),
        ("Saves are kept inside the game's install folder, so there is no separate saves folder to add.", "gameEditor.templateNote.insideInstall"),
        ("This game keeps saves in more than one place; add the saves folder by hand.", "gameEditor.templateNote.severalPlaces"),
        ("Uses anti-cheat; don't use SavePoint with its online mode.", "gameEditor.templateNote.antiCheat"),
    ];

    /// <summary>The catalog keys for the known sentences in a note, in the order they appear in it.</summary>
    /// <param name="notes">A game's notes; null or blank has none.</param>
    public static IReadOnlyList<string> KeysIn(string? notes) =>
        string.IsNullOrWhiteSpace(notes)
            ? []
            : [.. _known
                .Select(entry => (entry.Key, At: notes.IndexOf(entry.Sentence, StringComparison.Ordinal)))
                .Where(entry => entry.At >= 0)
                .OrderBy(entry => entry.At)
                .Select(entry => entry.Key)];

    /// <summary>What is left of a note once every known sentence is taken out; empty when the note holds nothing else.</summary>
    /// <param name="notes">A game's notes; null or blank leaves nothing.</param>
    public static string UnknownPart(string? notes)
    {
        var left = notes ?? string.Empty;
        foreach (var (sentence, _) in _known)
        {
            left = left.Replace(sentence, string.Empty, StringComparison.Ordinal);
        }

        return left.Trim();
    }
}
