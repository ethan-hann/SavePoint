using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

public sealed class BranchNameTests : IDisposable
{
    // Names where the engine is stricter on purpose: git allows them under refs/heads but they are unsafe as branch names.
    private static readonly string[] _stricterThanGit = ["HEAD", "@", "-leading-dash"];

    private readonly EngineWorkspace _ws = new();

    public static TheoryData<string> Names =>
    [
        "profile-1", "profiles/a1b2c3", "Caf\u00e9", "a.b", "a_b", "UPPER", "1234",
        "", "a..b", "-leading-dash", "has space", "ends.lock", "trailing/", "/leading", "a//b", "tilde~1", "colon:name",
        "HEAD", "@", "new\nline", "at@{brace", "what?", "star*", "open[bracket", "back\\slash", "caret^",
        "ends-with-dot.", ".starts-with-dot", "folder/.hidden", "folder/part.lock/more", "tab\there",
    ];

    public void Dispose() => _ws.Dispose();

    [Theory]
    [MemberData(nameof(Names))]
    public async Task The_engine_and_git_agree_on_which_names_are_valid(string name)
    {
        var result = await _ws.Runner.RunAsync(
            new GitInvocation(["check-ref-format", "refs/heads/" + name]) { WorkingDirectory = _ws.Root, AllowedExitCodes = [0, 1, 128], ExtraEnvironment = new Dictionary<string, string?> { ["GIT_CEILING_DIRECTORIES"] = _ws.Root } },
            TestContext.Current.CancellationToken);
        var gitAccepts = result.ExitCode == 0;

        var engineAccepts = TryCreate(name);

        if (_stricterThanGit.Contains(name))
        {
            Assert.False(engineAccepts);
        }
        else
        {
            Assert.Equal(gitAccepts, engineAccepts);
        }
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
