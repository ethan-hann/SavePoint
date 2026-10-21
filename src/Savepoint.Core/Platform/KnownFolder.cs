namespace Savepoint.Core.Platform;

/// <summary>Windows shell folders a user can redirect (OneDrive folder backup moves Documents, for one).</summary>
public enum KnownFolder
{
    /// <summary>Documents.</summary>
    Documents,

    /// <summary>The desktop.</summary>
    Desktop,

    /// <summary>Downloads.</summary>
    Downloads,

    /// <summary>Saved Games, where some games keep their saves.</summary>
    SavedGames,

    /// <summary>AppData\Roaming.</summary>
    RoamingAppData,

    /// <summary>AppData\Local.</summary>
    LocalAppData,

    /// <summary>AppData\LocalLow, where Unity games save.</summary>
    LocalLow,

    /// <summary>Program Files.</summary>
    ProgramFiles,

    /// <summary>Program Files (x86).</summary>
    ProgramFilesX86,

    /// <summary>ProgramData, shared by all users.</summary>
    ProgramData,

    /// <summary>The Windows folder.</summary>
    Windows,
}
