namespace Savepoint.FakeGame;

// A stand-in game for end-to-end tests: deterministic, offline, and it writes only where it is told to.
internal static class Program
{
    private const int _badArguments = 2;

    private static async Task<int> Main(string[] args)
    {
        if (!Options.TryParse(args, out var options, out var error))
        {
            await Console.Error.WriteLineAsync($"FakeGame: {error}");
            await Console.Error.WriteLineAsync(Options.Usage);
            return _badArguments;
        }

        return await new FakeGameRun(options, Console.In, Console.Out, Console.Error).RunAsync();
    }
}
