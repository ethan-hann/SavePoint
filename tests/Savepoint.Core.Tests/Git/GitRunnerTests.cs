using System.Collections;
using System.Security.Cryptography;
using System.Text;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git;

public sealed class GitRunnerTests : IDisposable
{
    private readonly GitWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task The_bundled_git_reports_a_version_with_an_empty_PATH()
    {
        var parent = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            parent[(string)entry.Key] = (string?)entry.Value ?? string.Empty;
        }

        parent["PATH"] = string.Empty;
        var binary = new GitBinary(GitWorkspace.ExpectedBundleRoot, _ws.ConfigDir, parent, GitHost.Current);

        var result = await new GitRunner(binary).RunAsync(new GitInvocation(["--version"]), TestContext.Current.CancellationToken);

        Assert.StartsWith("git version ", result.StandardOutput);
    }

    [Fact]
    public void The_executable_lives_under_the_bundle_root()
    {
        var root = Path.GetFullPath(GitWorkspace.ExpectedBundleRoot);

        Assert.Equal(root, _ws.Binary.Root);
        Assert.StartsWith(root, _ws.Binary.ExecutablePath);
        Assert.True(File.Exists(_ws.Binary.ExecutablePath), _ws.Binary.ExecutablePath);
    }

    [Fact]
    public async Task Git_finds_its_helpers_inside_the_bundle()
    {
        var result = await _ws.RunPlainAsync("--exec-path");

        Assert.StartsWith(Slashed(Path.GetFullPath(GitWorkspace.ExpectedBundleRoot)), Slashed(result.StandardOutput.Trim()), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Text_stdin_reaches_git_in_full()
    {
        const string text = "first line\nsecond line with caf\u00e9\n";

        var result = await _ws.RunPlainAsync(new GitInvocation(["hash-object", "--stdin"]) { Stdin = text });

        Assert.Equal(BlobId(Encoding.UTF8.GetBytes(text)), result.StandardOutput.Trim());
    }

    [Fact]
    public async Task Binary_stdout_returns_the_exact_bytes_of_a_blob()
    {
        await _ws.InitAsync();
        var blob = new byte[300_000];
        new Random(7).NextBytes(blob);
        blob[10] = 0x0D;
        blob[11] = 0x0A;
        blob[12] = 0xFF;
        var hash = (await _ws.RunAsync(new GitInvocation(["hash-object", "-w", "--stdin"]) { StdinBytes = blob })).StandardOutput.Trim();

        var result = await _ws.RunAsync(new GitInvocation(["cat-file", "blob", hash]) { BinaryStdout = true });

        Assert.Equal(blob, result.StandardOutputBytes);
        Assert.Equal(string.Empty, result.StandardOutput);
    }

    [Fact]
    public async Task A_failing_command_throws_with_its_arguments_exit_code_and_stderr()
    {
        await _ws.InitAsync();

        var thrown = await Assert.ThrowsAsync<GitCommandException>(() => _ws.RunAsync("rev-parse", "--verify", "no-such-ref^{commit}"));

        Assert.Equal(128, thrown.ExitCode);
        Assert.Equal<IEnumerable<string>>(new[] { "rev-parse", "--verify", "no-such-ref^{commit}" }, thrown.Arguments);
        Assert.Contains("fatal:", thrown.StandardError);
    }

    [Fact]
    public async Task An_allowed_exit_code_comes_back_as_a_result()
    {
        await _ws.InitAsync();

        var result = await _ws.RunAsync(new GitInvocation(["rev-parse", "--verify", "no-such-ref^{commit}"]) { AllowedExitCodes = [0, 128] });

        Assert.Equal(128, result.ExitCode);
        Assert.Contains("fatal:", result.StandardError);
    }

    [Fact]
    public async Task Arguments_with_spaces_quotes_and_a_trailing_backslash_arrive_intact()
    {
        string[] values = ["a b", "say \"hi\"", "C:\\folder\\", "caf\u00e9 \u65e5\u672c"];
        foreach (var value in values)
        {
            var result = await _ws.RunPlainAsync("-c", $"savepoint.probe={value}", "config", "--get", "savepoint.probe");

            Assert.Equal(value, result.StandardOutput.TrimEnd('\n'));
        }
    }

    [Fact]
    public async Task The_work_tree_is_the_default_working_directory()
    {
        await _ws.InitAsync();
        _ws.Write("probe.txt", "probe content\n");

        // A relative path only resolves when the process starts inside the work tree.
        var result = await _ws.RunAsync("hash-object", "probe.txt");

        Assert.Equal(BlobId(Encoding.UTF8.GetBytes("probe content\n")), result.StandardOutput.Trim());
    }

    [Fact]
    public async Task An_explicit_working_directory_wins_over_the_work_tree()
    {
        await _ws.InitAsync();
        Directory.CreateDirectory(Path.Combine(_ws.WorkTree, "sub"));

        var result = await _ws.RunAsync(new GitInvocation(["rev-parse", "--show-prefix"]) { WorkingDirectory = Path.Combine(_ws.WorkTree, "sub") });

        Assert.Equal("sub/", result.StandardOutput.Trim());
    }

    [Fact]
    public async Task A_missing_working_directory_is_reported_as_such()
    {
        var missing = Path.Combine(_ws.Root, "not-there");

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => _ws.RunPlainAsync(new GitInvocation(["--version"]) { WorkingDirectory = missing }));
    }

    [Fact]
    public async Task Stdout_lines_stream_to_the_callback_and_are_not_collected_twice()
    {
        var lines = new List<string>();

        var result = await _ws.RunPlainAsync(new GitInvocation(["config", "--list"]) { OnStdoutLine = lines.Add });

        Assert.Contains("safe.directory=*", lines);
        Assert.Equal(string.Empty, result.StandardOutput);
    }

    [Fact]
    public async Task An_exception_in_a_callback_stops_the_call_and_reaches_the_caller()
    {
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _ws.RunPlainAsync(new GitInvocation(["--version"]) { OnStdoutLine = _ => throw new InvalidOperationException("from the callback") }));

        Assert.Equal("from the callback", thrown.Message);
    }

    [Fact]
    public async Task A_large_stdin_and_a_large_output_do_not_deadlock()
    {
        await _ws.InitAsync();
        const string emptyTree = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";
        const int lines = 100_000;
        var request = new StringBuilder(lines * (emptyTree.Length + 1));
        for (var i = 0; i < lines; i++)
        {
            request.Append(emptyTree).Append('\n');
        }

        var result = await _ws.RunAsync(new GitInvocation(["cat-file", "--batch-check"]) { Stdin = request.ToString() });

        Assert.Equal(lines, result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void The_repository_and_work_tree_travel_in_the_environment()
    {
        var gitDir = Path.Combine(_ws.Root, "g");
        var workTree = Path.Combine(_ws.Root, "w");

        var env = _ws.Runner.BuildEnvironment(new GitInvocation(["status"]) { GitDir = gitDir, WorkTree = workTree });

        Assert.Equal(gitDir, env.GetValueOrDefault("GIT_DIR"));
        Assert.Equal(workTree, env.GetValueOrDefault("GIT_WORK_TREE"));
    }

    [Fact]
    public void A_work_tree_without_a_git_dir_is_refused()
    {
        var invocation = new GitInvocation(["status"]) { WorkTree = _ws.Root };

        Assert.Throws<ArgumentException>(() => _ws.Runner.BuildEnvironment(invocation));
    }

    [Theory]
    [InlineData(true, "0")]
    [InlineData(false, null)]
    public void Read_only_calls_do_not_take_optional_locks(bool readOnly, string? expected)
    {
        var env = _ws.Runner.BuildEnvironment(new GitInvocation(["status"]) { ReadOnly = readOnly });

        Assert.Equal(expected, env.GetValueOrDefault("GIT_OPTIONAL_LOCKS"));
    }

    [Fact]
    public void Extra_environment_overrides_the_base_and_null_removes_a_variable()
    {
        var invocation = new GitInvocation(["status"])
        {
            ExtraEnvironment = new Dictionary<string, string?> { ["GIT_AUTHOR_NAME"] = "Someone", ["LANG"] = null, ["MY_VARIABLE"] = "1" },
        };

        var env = _ws.Runner.BuildEnvironment(invocation);

        Assert.Equal("Someone", env.GetValueOrDefault("GIT_AUTHOR_NAME"));
        Assert.False(env.ContainsKey("LANG"));
        Assert.Equal("1", env.GetValueOrDefault("MY_VARIABLE"));
        Assert.Equal("SavePoint", env.GetValueOrDefault("GIT_COMMITTER_NAME"));
    }

    [Fact]
    public async Task More_than_one_stdin_source_is_refused()
    {
        var invocation = new GitInvocation(["hash-object", "--stdin"]) { Stdin = "a", StdinBytes = [1] };

        await Assert.ThrowsAsync<ArgumentException>(() => _ws.Runner.RunAsync(invocation, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Binary_stdout_cannot_be_combined_with_a_stdout_callback()
    {
        var invocation = new GitInvocation(["--version"]) { BinaryStdout = true, OnStdoutLine = _ => { } };

        await Assert.ThrowsAsync<ArgumentException>(() => _ws.Runner.RunAsync(invocation, TestContext.Current.CancellationToken));
    }

    private static string Slashed(string path) => path.Replace('\\', '/');

    private static string BlobId(byte[] content)
    {
        var header = Encoding.ASCII.GetBytes($"blob {content.Length}\0");
        return Convert.ToHexStringLower(SHA1.HashData([.. header, .. content]));
    }
}
