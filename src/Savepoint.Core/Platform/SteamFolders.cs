namespace Savepoint.Core.Platform;

/// <summary>
/// The Steam client's folder and every library folder Steam lists, as tier 2: each holds every game in it, so none may be tracked
/// as a whole. A game's own install folder beneath them is not affected. Lookups run once per platform, on the first table built.
/// </summary>
internal sealed class SteamFolders(PlatformPaths paths)
{
    private readonly Lazy<(string? Client, IReadOnlyList<string> Libraries)> _found = new(() => Find(paths));

    /// <summary>Adds the Steam folder and the libraries to <paramref name="builder"/>; adds nothing when Steam cannot be found.</summary>
    public void AddTo(DenylistBuilder builder)
    {
        var (client, libraries) = _found.Value;
        if (client is null)
        {
            return;
        }

        builder.Exact(client, "The Steam folder holds every Steam game");
        foreach (var library in libraries)
        {
            builder.Exact(library, "A Steam library folder holds every game in the library");
        }
    }

    // Nothing here may break a path check: any failure to look means the rules that need no lookup still apply.
    private static (string? Client, IReadOnlyList<string> Libraries) Find(PlatformPaths paths)
    {
        string? client;
        try
        {
            client = paths.SteamFolder();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return (null, []);
        }

        if (client is null)
        {
            return (null, []);
        }

        // The folder stays protected when only its library list cannot be read.
        try
        {
            var file = paths.Under(paths.Under(client, "steamapps"), "libraryfolders.vdf");
            return (client, SteamLibraryFile.LibraryPaths(paths.ReadFile(file), paths.Syntax));
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return (client, []);
        }
    }
}
