namespace Savepoint.Core.Services.Exe;

/// <summary>Which kinds of launchable file <see cref="ExecutableScanner"/> suggests.</summary>
[Flags]
public enum ExecutableKinds
{
    /// <summary>Nothing is suggested.</summary>
    None = 0,

    /// <summary><c>.exe</c> files: Windows games, and Windows games run through Proton or Wine on other systems.</summary>
    WindowsExe = 1,

    /// <summary>Native Linux programs: files with an execute bit that are real binaries or scripts, plus <c>.sh</c> and <c>.x86_64</c> files.</summary>
    LinuxNative = 2,

    /// <summary>macOS <c>.app</c> bundles, suggested as the bundle itself.</summary>
    MacApp = 4,
}
