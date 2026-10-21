using Savepoint.App.Features.Common.Art;

namespace Savepoint.App.Composition;

public static partial class FeatureComposition
{
    /// <summary>
    /// The artwork provider the shell hands to every tile (D30): over the artwork Api, with its own decoded-image cache. Null when
    /// the app has no artwork Api, in which case every game keeps its name tint.
    /// </summary>
    /// <param name="context">Shared App services.</param>
    public static IArtworkProvider? Artwork(FeatureContext context) =>
        context.Services.Artwork is { } api ? new ArtworkProvider(api, context.Dispatcher) : null;
}
