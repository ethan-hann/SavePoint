namespace Savepoint.Core.Api;

/// <summary>The profile a job works on, as the job began: its name stays readable after the profile is renamed or deleted.</summary>
/// <param name="Id">The profile's id.</param>
/// <param name="Name">The profile's name when the job began.</param>
public sealed record JobProfile(string Id, string Name);
