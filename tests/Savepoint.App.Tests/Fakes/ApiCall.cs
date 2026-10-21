namespace Savepoint.App.Tests.Fakes;

// Api is the interface name (for example "IProfilesApi"); cancellation tokens are left out of Args.
public sealed record ApiCall(string Api, string Member, IReadOnlyList<object?> Args);
