using Savepoint.Core.Api;

namespace Savepoint.Core.CloudSaves;

/// <summary>Reads one store's cloud-save setting for a game. An implementation only reads; it never writes a store's files.</summary>
internal interface IStoreCloudReader
{
    /// <summary>Reads the setting.</summary>
    /// <param name="storeGameId">The game's id in this store: the Steam app id or the GOG product id, as digits.</param>
    /// <returns>The state; <see cref="CloudSaveState.Unknown"/> when it cannot be read. Never throws.</returns>
    CloudSaveState Read(string storeGameId);
}
