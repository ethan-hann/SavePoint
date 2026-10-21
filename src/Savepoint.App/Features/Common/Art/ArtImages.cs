using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using Savepoint.Core.Artwork;

namespace Savepoint.App.Features.Common.Art;

/// <summary>
/// Decodes artwork off the UI thread at the size a tile shows it, and keeps the results in a bounded cache. One instance is shared
/// by every tile of the running app.
/// </summary>
public sealed class ArtImages
{
    private readonly ConcurrentDictionary<ArtKey, Lazy<Task<Bitmap?>>> _inFlight = new();

    // Requests whose file could not be decoded, so a layout pass never retries a broken file.
    private readonly ConcurrentDictionary<ArtKey, byte> _failed = new();

    private int _decodes;

    /// <summary>Creates the decoder over its own cache.</summary>
    /// <param name="cache">The cache to use; a new one of 64 bitmaps when null.</param>
    public ArtImages(ArtBitmapCache? cache = null)
    {
        Cache = cache ?? new ArtBitmapCache();
    }

    /// <summary>The cache of decoded bitmaps.</summary>
    public ArtBitmapCache Cache { get; }

    /// <summary>How many files have been decoded, for tests.</summary>
    internal int DecodeCount => Volatile.Read(ref _decodes);

    /// <summary>Called on the decoding thread just before a file is decoded, for tests.</summary>
    internal Action<string>? Decoding { get; set; }

    /// <summary>The bitmap already decoded for a request, or null.</summary>
    /// <param name="key">The request.</param>
    public Bitmap? TryGet(ArtKey key) => Cache.TryGet(key, out var bitmap) ? bitmap : null;

    /// <summary>Decodes the file for a request, off the UI thread; null when it cannot be decoded.</summary>
    /// <param name="key">The request.</param>
    /// <param name="cancellationToken">Stops the wait; a decode already running finishes and is cached.</param>
    public Task<Bitmap?> LoadAsync(ArtKey key, CancellationToken cancellationToken = default)
    {
        if (TryGet(key) is { } cached)
        {
            return Task.FromResult<Bitmap?>(cached);
        }

        if (_failed.ContainsKey(key))
        {
            return Task.FromResult<Bitmap?>(null);
        }

        // Requests made while one is running wait for it instead of decoding the same file again.
        var load = _inFlight.GetOrAdd(key, request => new Lazy<Task<Bitmap?>>(() => Task.Run(() => DecodeAndKeep(request)))).Value;
        return cancellationToken.CanBeCanceled ? load.WaitAsync(cancellationToken) : load;
    }

    private Bitmap? DecodeAndKeep(ArtKey key)
    {
        try
        {
            Decoding?.Invoke(key.Path);
            Interlocked.Increment(ref _decodes);
            var bitmap = Decode(key);
            if (bitmap is null)
            {
                _failed[key] = 0;
            }
            else
            {
                Cache.Add(key, bitmap);
            }

            return bitmap;
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
        }
    }

    // The size is what a tile needs to cover itself, scaled to the screen and never past the file's own size.
    private static Bitmap? Decode(ArtKey key)
    {
        try
        {
            if (ImageProbe.ReadFile(key.Path) is not { } info)
            {
                return null;
            }

            var cover = Math.Max(key.Width, key.Height * (double)info.Width / info.Height) * (key.ScalePercent / 100.0);
            var pixels = Math.Clamp((int)Math.Ceiling(cover), 1, info.Width);
            using var stream = new FileStream(key.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Bitmap.DecodeToWidth(stream, pixels, BitmapInterpolationMode.HighQuality);
        }
        catch (Exception)
        {
            // Decoders fail in many ways on a file that is not what its structure claims; to a tile that is simply no image.
            return null;
        }
    }
}
