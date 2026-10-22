using Savepoint.Core.Api;

namespace Savepoint.Core.Backend;

/// <summary>What the launch side hands the backend: its Api, and the sessions the services consult.</summary>
/// <param name="Api">Answers <see cref="CoreBackend.Launch"/>.</param>
/// <param name="Sessions">Tells the services which games and profiles are being played.</param>
public sealed record LaunchComponents(ILaunchApi Api, IGameSessions Sessions);
