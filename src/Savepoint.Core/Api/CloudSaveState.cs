namespace Savepoint.Core.Api;

/// <summary>Whether a game's store keeps cloud saves for it, as far as the store's own settings on this computer say.</summary>
public enum CloudSaveState
{
    /// <summary>The settings could not be read, or the game has no store SavePoint knows how to ask.</summary>
    Unknown,

    /// <summary>The store keeps this game's saves in its cloud.</summary>
    On,

    /// <summary>The store does not keep this game's saves in its cloud.</summary>
    Off,
}
