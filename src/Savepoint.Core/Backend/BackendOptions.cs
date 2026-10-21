using Savepoint.Core.Git;
using Savepoint.Core.Platform;
using Savepoint.Core.Services.Storage;

namespace Savepoint.Core.Backend;

/// <summary>
/// What <see cref="CoreBackend.Create"/> builds the backend from. Every member is optional: the defaults are the real machine,
/// and tests replace the parts they need with temp folders and fakes.
/// </summary>
public sealed record BackendOptions
{
    /// <summary>The operating system's folders and tables; <see cref="PlatformFactory.ForCurrentOs"/> when null.</summary>
    public IPlatform? Platform { get; init; }

    /// <summary>Where the games, profiles and settings files live; the platform's catalog folder when null.</summary>
    public string? CatalogDirectory { get; init; }

    /// <summary>The folder SavePoint is installed in, which games may not overlap; the app's base directory when null.</summary>
    public string? InstallDirectory { get; init; }

    /// <summary>The bundled git; located next to the app (or at <c>SAVEPOINT_GIT_ROOT</c>) when null.</summary>
    public GitBinary? Git { get; init; }

    /// <summary>Free and total space of a volume; the real drives when null.</summary>
    public IDiskSpace? DiskSpace { get; init; }

    /// <summary>The clock for timestamps; the system clock when null.</summary>
    public TimeProvider? Time { get; init; }

    /// <summary>Tells the services which games are being played; nothing is reported running when null.</summary>
    public IGameSessions? Sessions { get; init; }

    /// <summary>
    /// Builds the launch component from the shared services. The backend attaches the sessions it returns, and answers
    /// <see cref="CoreBackend.Launch"/> with its Api; without it every launch call fails with <c>error.launch.unavailable</c>.
    /// </summary>
    public Func<BackendServices, LaunchComponents>? Launch { get; init; }

    /// <summary>How many loose objects in a repository make a capture queue an optimization (D13.17); 5000 by default.</summary>
    public int LooseObjectThreshold { get; init; } = 5000;

    /// <summary>Tuning for the git engine; its defaults when null. The clock is always replaced by <see cref="Time"/>.</summary>
    public GitEngineOptions? Engine { get; init; }

    // Called with the source and the destination after each file of a storage move is copied and before it is verified; tests use it to inject faults.
    internal Action<string, string>? AfterRelocationCopy { get; init; }
}
