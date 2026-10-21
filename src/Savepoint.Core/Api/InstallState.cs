namespace Savepoint.Core.Api;

// Version is the profile's override when set, otherwise the version read from the exe; null when neither exists.
public sealed record InstallState(string? Version, int? ModFileCount);
