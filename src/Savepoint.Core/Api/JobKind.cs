namespace Savepoint.Core.Api;

public enum JobKind
{
    // Preparing, switching folders, and starting the game; the play session itself continues in ILaunchApi.
    Launch,
    Capture,
    Compact,
    RelocateStorage,
    ClearData,
}
