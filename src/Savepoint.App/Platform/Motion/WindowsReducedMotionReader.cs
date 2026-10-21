namespace Savepoint.App.Platform.Motion;

/// <summary>
/// Windows keeps the preference as "show animations in Windows" (<c>SPI_GETCLIENTAREAANIMATION</c>), which is the
/// opposite of reduce motion.
/// </summary>
internal sealed class WindowsReducedMotionReader : IReducedMotionReader
{
    private readonly Func<bool?> _animationsEnabled;

    /// <param name="animationsEnabled">Reads <c>SPI_GETCLIENTAREAANIMATION</c>: true when animations are on, null when the call fails.</param>
    public WindowsReducedMotionReader(Func<bool?> animationsEnabled)
    {
        _animationsEnabled = animationsEnabled;
    }

    /// <inheritdoc />
    public ValueTask<bool?> ReadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(!_animationsEnabled());
}
