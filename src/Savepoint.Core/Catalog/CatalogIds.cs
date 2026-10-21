using System.Text;

namespace Savepoint.Core.Catalog;

/// <summary>
/// Makes ids for games and profiles: a short lowercase ASCII slug of the name plus four random letters or digits, such as
/// <c>skyrim-se-4k2x</c>. They are safe as a folder or branch name on every OS. Uniqueness is not checked here.
/// </summary>
public static class CatalogIds
{
    private const string _alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
    private const int _suffixLength = 4;
    private const int _maxSlugLength = 40;

    /// <summary>Makes an id for a new game from its name; <c>game</c> stands in when the name has no letters or digits.</summary>
    /// <param name="name">The game's name. Letters and digits are kept, anything else becomes a dash, and the slug stops at 40 characters.</param>
    /// <param name="random">The source of the random suffix; defaults to a shared instance, and tests pass a seeded one.</param>
    public static string NewGameId(string name, Random? random = null) => NewId(name, "game", random);

    /// <summary>
    /// Makes a profile with a fresh id, the trimmed name, and the branch <c>profile/</c> plus the id, so a profile and its branch
    /// can be matched by eye. Every other field has its default, and nothing is saved.
    /// </summary>
    /// <param name="gameId">The game the profile belongs to.</param>
    /// <param name="name">The profile's name; <c>profile</c> stands in for the id when it has no letters or digits.</param>
    /// <param name="random">The source of the random suffix; defaults to a shared instance, and tests pass a seeded one.</param>
    public static Profile NewProfile(string gameId, string name, Random? random = null)
    {
        var id = NewId(name, "profile", random);
        return new Profile(Id: id, GameId: gameId, Name: name.Trim(), BranchName: $"profile/{id}");
    }

    private static string NewId(string name, string emptyFallback, Random? random)
    {
        random ??= Random.Shared;

        var slug = Slugify(name);
        if (slug.Length == 0)
        {
            slug = emptyFallback;
        }

        var suffix = new char[_suffixLength];
        for (var i = 0; i < suffix.Length; i++)
        {
            suffix[i] = _alphabet[random.Next(_alphabet.Length)];
        }

        return $"{slug}-{new string(suffix)}";
    }

    // ASCII only so ids are safe folder and branch names on every OS; anything else collapses to a dash.
    private static string Slugify(string name)
    {
        var slug = new StringBuilder();
        var pendingDash = false;
        foreach (var c in name)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                if (pendingDash && slug.Length > 0)
                {
                    slug.Append('-');
                }

                slug.Append(char.ToLowerInvariant(c));
                pendingDash = false;
            }
            else
            {
                pendingDash = true;
            }
        }

        if (slug.Length > _maxSlugLength)
        {
            slug.Length = _maxSlugLength;
        }

        // Cutting at the limit can leave a dash at the end.
        return slug.ToString().TrimEnd('-');
    }
}
