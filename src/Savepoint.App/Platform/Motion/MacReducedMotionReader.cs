using Savepoint.App.Platform.Processes;

namespace Savepoint.App.Platform.Motion;

/// <summary>Reads the accessibility "Reduce motion" switch with <c>defaults read com.apple.universalaccess reduceMotion</c>.</summary>
internal sealed class MacReducedMotionReader : IReducedMotionReader
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(3);

    private readonly IProcessRunner _runner;

    public MacReducedMotionReader(IProcessRunner runner)
    {
        _runner = runner;
    }

    /// <inheritdoc />
    public async ValueTask<bool?> ReadAsync(CancellationToken cancellationToken)
    {
        var command = new ProcessCommand("/usr/bin/defaults", ["read", "com.apple.universalaccess", "reduceMotion"]);
        var output = await _runner.RunAsync(command, _timeout, cancellationToken).ConfigureAwait(false);
        // A key the user never touched makes defaults exit 1, which is the system saying nothing.
        return output is { ExitCode: 0 } ? Parse(output.StandardOutput) : null;
    }

    private static bool? Parse(string text) => text.Trim() switch
    {
        "1" or "true" => true,
        "0" or "false" => false,
        _ => null,
    };
}
