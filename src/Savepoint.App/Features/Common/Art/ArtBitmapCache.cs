using Avalonia.Media.Imaging;

namespace Savepoint.App.Features.Common.Art;

/// <summary>What one decoded bitmap was decoded for: the file, which version of the game's artwork it belonged to, and the display size.</summary>
/// <param name="Path">The image file.</param>
/// <param name="Revision">The artwork revision the file was read at, so a file replaced under the same name is read again.</param>
/// <param name="Width">Tile width in device-independent pixels.</param>
/// <param name="Height">Tile height in device-independent pixels.</param>
/// <param name="ScalePercent">The render scaling in percent: 100 for 1.0, 150 for 1.5.</param>
public readonly record struct ArtKey(string Path, int Revision, int Width, int Height, int ScalePercent);

/// <summary>A small bounded cache of decoded artwork, so a list that is rebuilt shows its images again at once. Safe for any thread.</summary>
public sealed class ArtBitmapCache
{
    private readonly object _gate = new();

    // Most recently used first.
    private readonly LinkedList<(ArtKey Key, Bitmap Bitmap)> _order = new();
    private readonly Dictionary<ArtKey, LinkedListNode<(ArtKey Key, Bitmap Bitmap)>> _entries = [];

    /// <summary>Creates a cache that keeps at most <paramref name="capacity"/> bitmaps.</summary>
    /// <param name="capacity">How many bitmaps to keep; a value below 1 keeps one.</param>
    public ArtBitmapCache(int capacity = 64)
    {
        Capacity = Math.Max(1, capacity);
    }

    /// <summary>The most bitmaps kept.</summary>
    public int Capacity { get; }

    /// <summary>How many bitmaps are kept now.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>Finds a bitmap and marks it as just used.</summary>
    /// <param name="key">What it was decoded for.</param>
    /// <param name="bitmap">The bitmap, when found.</param>
    public bool TryGet(ArtKey key, out Bitmap? bitmap)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                bitmap = node.Value.Bitmap;
                return true;
            }
        }

        bitmap = null;
        return false;
    }

    /// <summary>Keeps a bitmap, dropping the least recently used one beyond the capacity.</summary>
    /// <param name="key">What it was decoded for.</param>
    /// <param name="bitmap">The decoded bitmap.</param>
    public void Add(ArtKey key, Bitmap bitmap)
    {
        lock (_gate)
        {
            if (_entries.Remove(key, out var old))
            {
                _order.Remove(old);
            }

            _entries[key] = _order.AddFirst((key, bitmap));
            while (_entries.Count > Capacity && _order.Last is { } least)
            {
                _order.RemoveLast();
                _entries.Remove(least.Value.Key);
            }
        }
    }
}
