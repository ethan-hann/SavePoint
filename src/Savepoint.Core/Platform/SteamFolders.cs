namespace Savepoint.Core.Platform;

/// <summary>
/// The Steam client's folder and every library folder Steam lists, as tier 2: each holds every game in it, so none may be tracked
/// as a whole. A game's own install folder beneath them is not affected. A lookup that finds nothing or fails is retried on a
/// later check, at most once per <see cref="RetryInterval"/>, so a path check never waits on repeated file reads.
/// </summary>
internal sealed class SteamFolders(PlatformPaths paths, TimeProvider? clock = null)
{
    /// <summary>The shortest time between two attempts to find the folder or read the library list.</summary>
    public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly object _gate = new();
    private string? _client;
    private IReadOnlyList<string> _libraries = [];
    private bool _listRead;
    private long _lastAttempt;
    private bool _attempted;

    /// <summary>Adds the Steam folder and the libraries found so far to <paramref name="builder"/>; adds nothing when Steam cannot be found.</summary>
    public void AddTo(DenylistBuilder builder)
    {
        string? client;
        IReadOnlyList<string> libraries;
        lock (_gate)
        {
            Refresh();
            (client, libraries) = (_client, _libraries);
        }

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

    // Called with the gate held. A found folder and a read list are kept for good; anything missing is looked for again.
    private void Refresh()
    {
        if (_listRead)
        {
            return;
        }

        var now = _clock.GetTimestamp();
        if (_attempted && _clock.GetElapsedTime(_lastAttempt, now) < RetryInterval)
        {
            return;
        }

        _attempted = true;
        _lastAttempt = now;

        // Nothing here may break a path check: any failure to look leaves the rules that need no lookup in force.
        try
        {
            _client ??= paths.SteamFolder();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return;
        }

        if (_client is null)
        {
            return;
        }

        try
        {
            var file = paths.Under(paths.Under(_client, "steamapps"), "libraryfolders.vdf");
            if (paths.ReadFile(file) is { } text)
            {
                _libraries = SteamLibraryFile.LibraryPaths(text, paths.Syntax);
                _listRead = true;
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // The folder stays protected; the list is tried again on a later check.
        }
    }
}
