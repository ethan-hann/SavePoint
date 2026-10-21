namespace Savepoint.App.Hosting;

/// <summary>The order the process starts in, apart from the platform calls, so a test can pin it.</summary>
internal static class Startup
{
    /// <summary>Runs the installer hooks, then the app.</summary>
    /// <param name="args">The command line, passed on to the app unchanged.</param>
    /// <param name="installerHooks">Handles the installer's and updater's own invocations of the executable; it may end the process.</param>
    /// <param name="app">Starts the app and returns its exit code.</param>
    internal static int Run(string[] args, Action installerHooks, Func<string[], int> app)
    {
        installerHooks();
        return app(args);
    }
}
