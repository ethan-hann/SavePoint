using Savepoint.App.Platform.Processes;

namespace Savepoint.App.Platform.Motion;

/// <summary>
/// Reads GNOME's "Animations" switch with <c>gsettings get org.gnome.desktop.interface enable-animations</c>, which is
/// the opposite of reduce motion. Other desktops have no such schema and answer "unknown".
/// </summary>
internal sealed class GnomeReducedMotionReader : IReducedMotionReader
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(3);

    private readonly IProcessRunner _runner;

    public GnomeReducedMotionReader(IProcessRunner runner)
    {
        _runner = runner;
    }

    /// <inheritdoc />
    public async ValueTask<bool?> ReadAsync(CancellationToken cancellationToken)
    {
        var command = new ProcessCommand("gsettings", ["get", "org.gnome.desktop.interface", "enable-animations"]);
        var output = await _runner.RunAsync(command, _timeout, cancellationToken).ConfigureAwait(false);
        // Without the schema gsettings exits 1 and says so on stderr.
        return output is { ExitCode: 0 } ? Parse(output.StandardOutput) : null;
    }

    private static bool? Parse(string text) => text.Trim() switch
    {
        "true" => false,
        "false" => true,
        _ => null,
    };
}
