using System.Collections;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git;

// A user's own git setup must never change what SavePoint captures or how it commits.
public sealed class GitIsolationTests : IDisposable
{
    private readonly GitWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task Git_variables_inherited_from_the_parent_process_do_not_redirect_git()
    {
        var elsewhere = Path.Combine(_ws.Root, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        var parent = CurrentEnvironment();
        parent["GIT_DIR"] = Path.Combine(elsewhere, "bogus.git");
        parent["GIT_WORK_TREE"] = elsewhere;
        parent["GIT_INDEX_FILE"] = Path.Combine(elsewhere, "bogus-index");
        var runner = new GitRunner(new GitBinary(GitWorkspace.ExpectedBundleRoot, _ws.ConfigDir, parent, GitHost.Current));
        var repo = Path.Combine(_ws.Root, "real");
        Directory.CreateDirectory(repo);
        await runner.RunAsync(new GitInvocation(["init"]) { WorkingDirectory = repo }, TestContext.Current.CancellationToken);

        var result = await runner.RunAsync(new GitInvocation(["rev-parse", "--absolute-git-dir"]) { WorkingDirectory = repo }, TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(repo, ".git").Replace('\\', '/'), result.StandardOutput.Trim().Replace('\\', '/'));
        Assert.False(Directory.Exists(Path.Combine(elsewhere, "bogus.git")));
    }

    [Fact]
    public async Task A_hostile_user_config_is_ignored_and_the_commit_is_unsigned_and_authored_by_the_app()
    {
        var home = HostileHome("Wrong", "bogus-signer-that-does-not-exist");
        await _ws.InitAsync();
        _ws.Write("save.dat", "data");
        await _ws.RunAsync("add", "-A");

        await _ws.RunAsync(new GitInvocation(["commit", "-m", "capture"]) { ExtraEnvironment = HomeEnvironment(home) });

        var commit = (await _ws.RunAsync("cat-file", "-p", "HEAD")).StandardOutput;
        Assert.Contains("author SavePoint <savepoint@localhost>", commit);
        Assert.Contains("committer SavePoint <savepoint@localhost>", commit);
        Assert.DoesNotContain("gpgsig", commit);
    }

    [Fact]
    public async Task A_users_global_ignore_and_attributes_files_do_not_change_what_is_staged()
    {
        var home = HostileHome("Wrong", "bogus-signer-that-does-not-exist");
        var configDirectory = Path.Combine(home, ".config", "git");
        Directory.CreateDirectory(configDirectory);
        File.WriteAllText(Path.Combine(configDirectory, "ignore"), "*.esp\n");
        File.WriteAllText(Path.Combine(configDirectory, "attributes"), "*.esp -text export-ignore\n");
        await _ws.InitAsync();
        _ws.Write("plugin.esp", "mod");
        var extra = HomeEnvironment(home);
        extra["XDG_CONFIG_HOME"] = Path.Combine(home, ".config");

        await _ws.RunAsync(new GitInvocation(["add", "-A"]) { ExtraEnvironment = extra });
        var staged = await _ws.RunAsync("ls-files", "-z");
        var attributes = await _ws.RunAsync(new GitInvocation(["check-attr", "export-ignore", "--", "plugin.esp"]) { ExtraEnvironment = extra });

        Assert.Equal<IEnumerable<string>>(new[] { "plugin.esp" }, staged.SplitStandardOutputOnNul());
        Assert.Contains("unspecified", attributes.StandardOutput);
    }

    [Fact]
    public async Task Every_setting_git_sees_comes_from_the_file_this_app_owns()
    {
        var home = HostileHome("Wrong", "bogus-signer-that-does-not-exist");
        var programData = Path.Combine(_ws.Root, "programdata");
        Directory.CreateDirectory(Path.Combine(programData, "Git"));
        File.WriteAllText(Path.Combine(programData, "Git", "config"), "[user]\n\tname = FromProgramData\n");
        var extra = HomeEnvironment(home);
        extra["PROGRAMDATA"] = programData;
        await _ws.InitAsync();

        var everything = await _ws.RunAsync(new GitInvocation(["config", "--list", "--show-origin"]) { ExtraEnvironment = extra });

        var origins = everything.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => UnquoteOrigin(line[..line.IndexOf('\t')]))
            .Distinct()
            .ToList();
        var allowed = new[] { "file:" + _ws.Binary.GlobalConfigPath.Replace('\\', '/'), "file:" + _ws.GitDir.Replace('\\', '/') + "/config", "command line:" };
        Assert.Contains("file:" + _ws.Binary.GlobalConfigPath.Replace('\\', '/'), origins, StringComparer.OrdinalIgnoreCase);
        Assert.All(origins, origin => Assert.Contains(origin, allowed, StringComparer.OrdinalIgnoreCase));
        Assert.DoesNotContain("FromProgramData", everything.StandardOutput);
        Assert.DoesNotContain("Wrong", everything.StandardOutput);
    }

    [Fact]
    public async Task A_repositorys_own_hooks_and_program_settings_never_run_a_program()
    {
        await _ws.InitAsync();
        var hookRan = Path.Combine(_ws.Root, "hook-ran.txt");
        var monitorRan = Path.Combine(_ws.Root, "monitor-ran.txt");
        var hooks = Path.Combine(_ws.GitDir, "hooks");
        Directory.CreateDirectory(hooks);
        foreach (var name in new[] { "post-commit", "reference-transaction", "post-checkout", "pre-commit" })
        {
            var hook = Path.Combine(hooks, name);
            File.WriteAllText(hook, $"#!/bin/sh\necho ran >> '{InShell(hookRan)}'\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        // The setting runs a command, here one that ignores the arguments git hands it.
        await _ws.RunAsync("config", "--local", "core.fsmonitor", $"echo ran >> '{InShell(monitorRan)}' #");
        _ws.Write("save.dat", "data");

        await _ws.RunAsync("add", "-A");
        await _ws.RunAsync("status", "--short");
        await _ws.RunAsync("commit", "-m", "capture");
        await _ws.RunAsync("checkout", "-b", "other");
        await _ws.RunAsync("diff", "--name-only");

        Assert.False(File.Exists(hookRan), "a hook of the repository ran");
        Assert.False(File.Exists(monitorRan), "the program named by core.fsmonitor ran");
    }

    [Fact]
    public async Task The_pinned_settings_are_in_effect()
    {
        await _ws.InitAsync();

        var result = await _ws.RunAsync("config", "--list");

        var settings = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("safe.directory=*", settings);
        Assert.Contains("core.longpaths=true", settings);
        Assert.Contains("core.autocrlf=false", settings);
        Assert.Contains("core.quotepath=false", settings);
        Assert.Contains("gc.auto=0", settings);
        Assert.Contains("maintenance.auto=false", settings);
        Assert.Contains("commit.gpgsign=false", settings);
        Assert.Contains("tag.gpgsign=false", settings);
        Assert.Contains("advice.detachedhead=false", settings);
        Assert.Contains("init.defaultbranch=savepoint-unborn", settings);
    }

    [Fact]
    public async Task A_new_repository_starts_on_the_placeholder_branch()
    {
        await _ws.InitAsync();

        var head = await _ws.RunAsync("symbolic-ref", "HEAD");

        Assert.Equal("refs/heads/savepoint-unborn", head.StandardOutput.Trim());
    }

    [Fact]
    public async Task Output_stays_in_english_whatever_the_user_locale_is()
    {
        await _ws.InitAsync();
        var extra = new Dictionary<string, string?> { ["LANG"] = "de_DE.UTF-8", ["LC_MESSAGES"] = "de_DE.UTF-8", ["LANGUAGE"] = "de" };

        var result = await _ws.RunAsync(new GitInvocation(["rev-parse", "--verify", "no-such-ref^{commit}"]) { ExtraEnvironment = extra, AllowedExitCodes = [128] });

        Assert.Contains("fatal: Needed a single revision", result.StandardError);
    }

    // The path as a shell inside single quotes reads it; forward slashes work for the shell that ships with git on Windows too.
    private static string InShell(string path) => path.Replace('\\', '/');

    // Git quotes an origin path that holds backslashes, as "file:\"C:\\dir\\config\"".
    private static string UnquoteOrigin(string origin)
    {
        if (!origin.StartsWith("file:\"", StringComparison.Ordinal))
        {
            return origin.Replace('\\', '/');
        }

        return ("file:" + origin["file:\"".Length..^1].Replace("\\\\", "\\", StringComparison.Ordinal)).Replace('\\', '/');
    }

    private static Dictionary<string, string> CurrentEnvironment()
    {
        var env = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            env[(string)entry.Key] = (string?)entry.Value ?? string.Empty;
        }

        return env;
    }

    private string HostileHome(string userName, string gpgProgram)
    {
        var home = Path.Combine(_ws.Root, "home");
        Directory.CreateDirectory(home);
        File.WriteAllText(
            Path.Combine(home, ".gitconfig"),
            $"[commit]\n\tgpgsign = true\n[user]\n\tname = {userName}\n\temail = wrong@example.com\n[gpg]\n\tprogram = {gpgProgram}\n[init]\n\tdefaultBranch = main\n[core]\n\tautocrlf = true\n");
        return home;
    }

    private static Dictionary<string, string?> HomeEnvironment(string home) => new()
    {
        ["HOME"] = home,
        ["USERPROFILE"] = home,
        ["XDG_CONFIG_HOME"] = Path.Combine(home, ".config"),
    };
}
