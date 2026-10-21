using System.Text;

namespace Savepoint.Core.Catalog;

public static class CatalogIds
{
    private const string _alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
    private const int _suffixLength = 4;
    private const int _maxSlugLength = 40;

    public static string NewGameId(string name, Random? random = null) => NewId(name, "game", random);

    // The branch is named after the id so a profile and its branch can be matched by eye.
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
