using System.Collections.Concurrent;

namespace Savepoint.Core.Services.Exe;

/// <summary>Remembers detected executable versions by a caller-supplied content id, so an unchanged file is read once (D13.15).</summary>
/// <remarks>
/// The id should identify the content, such as the blob id of the executable at a profile's tip, so an entry never
/// goes stale. Entries live as long as the cache; there are few of them and each is a short string. Concurrent callers
/// asking for the same new id may each read it once. Thread-safe.
/// </remarks>
public sealed class ExeVersionCache
{
    private readonly ConcurrentDictionary<string, Remembered> _versions = new(StringComparer.Ordinal);

    /// <summary>The number of content ids remembered.</summary>
    public int Count => _versions.Count;

    /// <summary>Returns the remembered version for <paramref name="contentId"/>, reading it first when it is new.</summary>
    /// <param name="contentId">Identifies the content; equal ids must mean equal bytes.</param>
    /// <param name="read">
    /// Loads the version when it is not remembered, typically by fetching the executable and calling
    /// <see cref="ExeVersionReader"/>. It should return null only when the file has no version, because null is
    /// remembered too; a failure to read should throw.
    /// </param>
    /// <param name="cancellationToken">Passed to <paramref name="read"/>; checked before anything else.</param>
    /// <returns>The version, or null when the file declares none.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    /// <remarks>An exception from <paramref name="read"/> reaches the caller and nothing is remembered.</remarks>
    public async Task<string?> GetOrReadAsync(string contentId, Func<CancellationToken, Task<string?>> read, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(contentId);
        ArgumentNullException.ThrowIfNull(read);
        cancellationToken.ThrowIfCancellationRequested();

        if (_versions.TryGetValue(contentId, out var known))
        {
            return known.Version;
        }

        var version = await read(cancellationToken).ConfigureAwait(false);
        _versions.TryAdd(contentId, new Remembered(version));
        return version;
    }

    // A wrapper, so "remembered as having no version" differs from "not remembered".
    private sealed record Remembered(string? Version);
}
