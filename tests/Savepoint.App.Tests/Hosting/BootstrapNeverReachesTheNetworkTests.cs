using Savepoint.Testing;

namespace Savepoint.App.Tests.Hosting;

// AppBootstrap hands the backend the real artwork handler, which reaches Steam's servers. A test that starts the backend through it
// has to cut that off or throw before the backend starts; this reads every such call in the tests so a new one cannot forget.
public class BootstrapNeverReachesTheNetworkTests
{
    private const string _call = "AppBootstrap.Start(";

    [Fact]
    public void Every_test_that_starts_the_backend_through_the_bootstrap_removes_the_artwork_handler_or_never_gets_that_far()
    {
        var tests = Path.Combine(TestEnvironment.RepoRoot ?? throw new InvalidOperationException("The tests did not run inside a checkout."), "tests", "Savepoint.App.Tests");
        var uncut = new List<string>();
        var calls = 0;

        foreach (var file in Directory.EnumerateFiles(tests, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(path => !path.EndsWith("BootstrapNeverReachesTheNetworkTests.cs", StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(file);
            for (var at = text.IndexOf(_call, StringComparison.Ordinal); at >= 0; at = text.IndexOf(_call, at + _call.Length, StringComparison.Ordinal))
            {
                calls++;
                var arguments = ArgumentsFrom(text, at + _call.Length);

                // The log tests change the options through their own helper, which removes the handler.
                if (!arguments.Contains("ArtworkHttp", StringComparison.Ordinal) && !arguments.Contains("Over(", StringComparison.Ordinal) && !arguments.Contains("throw", StringComparison.Ordinal))
                {
                    var line = text.AsSpan(0, at).Count('\n') + 1;
                    uncut.Add($"{Path.GetRelativePath(tests, file)}:{line}");
                }
            }
        }

        Assert.True(calls > 0, "No call to AppBootstrap.Start was found, so this check reads nothing.");
        Assert.True(uncut.Count == 0, "These tests start the backend with the real artwork handler, which reaches the network: " + string.Join(", ", uncut));
    }

    // The text of a call's argument list, up to the parenthesis that closes it; a parenthesis inside a string does not count.
    private static string ArgumentsFrom(string text, int start)
    {
        var depth = 1;
        var inString = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '"')
            {
                inString = true;
            }
            else if (c == '(')
            {
                depth++;
            }
            else if (c == ')' && --depth == 0)
            {
                return text[start..i];
            }
        }

        return text[start..];
    }
}
