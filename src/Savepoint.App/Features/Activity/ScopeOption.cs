namespace Savepoint.App.Features.Activity;

/// <summary>Which jobs the Activity screen lists.</summary>
public enum ActivityScope
{
    /// <summary>The selected game's jobs.</summary>
    ThisGame,

    /// <summary>Every game's jobs, and the jobs that span every game.</summary>
    AllGames,
}

/// <summary>One choice in the scope control.</summary>
/// <param name="Scope">What choosing it shows.</param>
/// <param name="Label">The choice's text, already localized.</param>
public sealed record ScopeOption(ActivityScope Scope, string Label);
