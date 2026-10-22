using Savepoint.App.Features.Common.Art;

namespace Savepoint.App.Composition;

public static partial class FeatureComposition
{
    /// <summary>The artwork provider the shell hands to every tile (D30): over the artwork Api, with its own decoded-image cache.</summary>
    /// <param name="context">Shared App services.</param>
    public static IArtworkProvider Artwork(FeatureContext context) => new ArtworkProvider(context.Services.Artwork, context.Dispatcher);
}
