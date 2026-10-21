using Savepoint.Core.Api;

namespace Savepoint.App.Features.Launch;

/// <summary>One entry of the launch dialog's save picker.</summary>
/// <param name="Entry">The save.</param>
/// <param name="Title">The name with its date ("Deep cave run — 5-30-26, 11:42 PM").</param>
/// <param name="Subtitle">What else is worth knowing ("Most recent · 2 saves in this profile").</param>
public sealed record SaveOptionViewModel(SaveEntry Entry, string Title, string Subtitle);
