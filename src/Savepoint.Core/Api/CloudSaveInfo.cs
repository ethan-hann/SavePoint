namespace Savepoint.Core.Api;

/// <summary>The store a game's cloud-save setting was read from.</summary>
public enum CloudStore
{
    /// <summary>No store SavePoint can ask: the game is not from Steam or GOG Galaxy, or its store could not be told.</summary>
    None,

    /// <summary>Steam.</summary>
    Steam,

    /// <summary>GOG Galaxy.</summary>
    Gog,
}

/// <summary>What a game's store says about cloud saves, and which store said it.</summary>
/// <param name="State">Whether the store keeps the game's saves in its cloud.</param>
/// <param name="Store">The store asked; <see cref="CloudStore.None"/> when the game belongs to none.</param>
public sealed record CloudSaveInfo(CloudSaveState State, CloudStore Store);
