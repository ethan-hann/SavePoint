using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

[Collection(EngineCollections.C)]
public sealed class BranchNameTests : IDisposable
{
    // Names where the engine is stricter on purpose: git allows them under refs/heads but they are unsafe as branch names.
    private static readonly string[] _stricterThanGit = ["HEAD", "@", "-leading-dash"];

    // One name per rule, plus names that must be accepted.
    private static readonly string[] _names =
    [
        "profile-1", "profiles/a1b2c3", "Caf\u00e9", "a.b",
        "a..b", "ends.lock", "at@{brace", "star*", "open[bracket", "back\\\\slash", "caret^", "ends-with-dot.", ".starts-with-dot", "folder/part.lock/more",
        "HEAD", "-leading-dash",
    ];

    private readonly EngineWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task The_engine_and_git_agree_on_which_names_are_valid()
    {
        var disagreements = new List<string>();
        foreach (var name in _names)
        {
            var result = await _ws.Runner.RunAsync(
                new GitInvocation(["check-ref-format", "refs/heads/" + name]) { WorkingDirectory = _ws.Root, AllowedExitCodes = [0, 1, 128], ExtraEnvironment = new Dictionary<string, string?> { ["GIT_CEILING_DIRECTORIES"] = _ws.Root } },
                TestContext.Current.CancellationToken);
            var engineAccepts = TryCreate(name);
            var expected = _stricterThanGit.Contains(name) ? false : result.ExitCode == 0;
            if (engineAccepts != expected)
            {
                disagreements.Add($"'{name}': git accepts {result.ExitCode == 0}, the engine accepts {engineAccepts}");
            }
        }

        Assert.Empty(disagreements);
    }

    private static bool TryCreate(string name)
    {
        try
        {
            _ = new CaptureRequest(name, "message", "id");
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
