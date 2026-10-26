namespace Savepoint.App.Features.Help;

/// <summary>One topic of the "How SavePoint works" page: a heading, a short text, and either a picture or a plain list.</summary>
/// <param name="Title">The heading.</param>
/// <param name="Body">Two to four sentences.</param>
/// <param name="Picture">The picture drawn under the text; <see cref="HelpPicture.None"/> for none.</param>
/// <param name="Items">A list shown under the text; empty for none.</param>
/// <param name="Notes">Paragraphs shown under the list; empty for none.</param>
/// <param name="Section">The name other screens use to open the page at this topic; null for a topic nothing links to.</param>
public sealed record HowItWorksTopic(string Title, string Body, HelpPicture Picture, IReadOnlyList<string> Items, IReadOnlyList<string> Notes, HowItWorksSection? Section = null)
{
    /// <summary>True when a picture is drawn.</summary>
    public bool HasPicture => Picture != HelpPicture.None;

    /// <summary>True when the topic has a list.</summary>
    public bool HasItems => Items.Count > 0;

    /// <summary>True when the topic has paragraphs under its list.</summary>
    public bool HasNotes => Notes.Count > 0;
}
