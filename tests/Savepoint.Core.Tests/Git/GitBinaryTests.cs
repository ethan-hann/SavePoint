using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git;

public sealed class GitBinaryTests : IDisposable
{
    private static readonly GitHost _windows = new(IsWindows: true, IsLinux: false);
    private static readonly GitHost _linux = new(IsWindows: false, IsLinux: true);
    private static readonly GitHost _mac = new(IsWindows: false, IsLinux: false);

    private readonly string _temp = Path.Combine(Path.GetTempPath(), "savepoint-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose() => GitWorkspace.DeleteDirectory(_temp);

    [Fact]
    public void Root_comes_from_the_override_when_one_is_given()
    {
        Assert.Equal("/somewhere/else", GitBinary.ResolveRoot("/somewhere/else", "/app/"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Root_defaults_to_the_git_folder_next_to_the_application(string? overrideRoot)
    {
        Assert.Equal(Path.Combine("/app", "git"), GitBinary.ResolveRoot(overrideRoot, "/app"));
    }

    [Fact]
    public void A_MinGit_bundle_without_its_real_git_falls_back_to_the_launcher()
    {
        var root = MakeBundle("cmd/git.exe");

        var binary = Create(root, _windows);

        Assert.Equal(GitBundleLayout.MinGit, binary.Layout);
        Assert.Equal(Path.Combine(Path.GetFullPath(root), "cmd", "git.exe"), binary.ExecutablePath);
    }

    // cmd/git.exe only starts the real git, so calling it doubles every git process.
    [Theory]
    [InlineData("ucrt64")]
    [InlineData("mingw64")]
    [InlineData("clangarm64")]
    public void A_MinGit_bundle_runs_its_real_git_directly(string toolchain)
    {
        var root = MakeBundle("cmd/git.exe", $"{toolchain}/bin/git.exe");

        var binary = Create(root, _windows);

        Assert.Equal(GitBundleLayout.MinGit, binary.Layout);
        Assert.Equal(Path.Combine(Path.GetFullPath(root), toolchain, "bin", "git.exe"), binary.ExecutablePath);
    }

    [Fact]
    public void A_MinGit_bundle_run_directly_puts_its_own_folders_first_on_the_path_and_names_its_helpers()
    {
        var parent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Path"] = @"C:\Windows\system32;C:\Program Files\Git\cmd" };
        var root = MakeBundle("cmd/git.exe", "ucrt64/bin/git.exe");

        var binary = Create(root, _windows, parent);

        var full = Path.GetFullPath(root);
        var path = binary.BaseEnvironment.GetValueOrDefault("PATH");
        Assert.Equal(
            $@"{Path.Combine(full, "ucrt64", "bin")};{Path.Combine(full, "usr", "bin")};C:\Windows\system32;C:\Program Files\Git\cmd",
            path);
        Assert.Equal(Path.Combine(full, "ucrt64", "libexec", "git-core"), binary.BaseEnvironment.GetValueOrDefault("GIT_EXEC_PATH"));
        Assert.False(binary.BaseEnvironment.ContainsKey("GIT_TEMPLATE_DIR"));
        Assert.False(binary.BaseEnvironment.ContainsKey("PREFIX"));
    }

    [Fact]
    public void A_bundle_with_bin_git_is_Dugite()
    {
        var root = MakeBundle("bin/git");

        var binary = Create(root, _linux);

        Assert.Equal(GitBundleLayout.Dugite, binary.Layout);
        Assert.Equal(Path.Combine(Path.GetFullPath(root), "bin", "git"), binary.ExecutablePath);
    }

    [Theory]
    [InlineData(true, GitBundleLayout.MinGit)]
    [InlineData(false, GitBundleLayout.Dugite)]
    public void An_empty_root_falls_back_to_the_layout_of_the_host(bool isWindows, GitBundleLayout expected)
    {
        var root = MakeBundle();

        var binary = Create(root, new GitHost(isWindows, IsLinux: false));

        Assert.Equal(expected, binary.Layout);
    }

    [Fact]
    public void Inherited_git_variables_never_reach_the_base_environment()
    {
        var parent = new Dictionary<string, string>
        {
            ["GIT_DIR"] = "/elsewhere/.git",
            ["GIT_WORK_TREE"] = "/elsewhere",
            ["GIT_INDEX_FILE"] = "/elsewhere/index",
            ["GIT_EXEC_PATH"] = "/usr/lib/git-core",
            ["GIT_CONFIG_COUNT"] = "1",
            ["GIT_SSH_COMMAND"] = "evil",
            ["GIT_OPTIONAL_LOCKS"] = "0",
        };

        var root = MakeBundle("bin/git");

        var binary = Create(root, _linux, parent);

        var inherited = new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_SSH_COMMAND", "GIT_OPTIONAL_LOCKS" };
        Assert.All(inherited, name => Assert.False(binary.BaseEnvironment.ContainsKey(name), name));
        Assert.Equal(Path.Combine(Path.GetFullPath(root), "libexec", "git-core"), binary.BaseEnvironment.GetValueOrDefault("GIT_EXEC_PATH"));
    }

    [Fact]
    public void Hooks_and_the_file_system_monitor_are_switched_off_through_the_environment_whatever_the_parent_set()
    {
        var parent = new Dictionary<string, string> { ["GIT_CONFIG_COUNT"] = "1", ["GIT_CONFIG_KEY_0"] = "core.hooksPath", ["GIT_CONFIG_VALUE_0"] = "/elsewhere" };

        var binary = Create(MakeBundle("bin/git"), _linux, parent);

        var environment = binary.BaseEnvironment;
        Assert.Equal("2", environment["GIT_CONFIG_COUNT"]);
        Assert.Equal("core.hooksPath", environment["GIT_CONFIG_KEY_0"]);
        Assert.Equal(binary.NoHooksPath, environment["GIT_CONFIG_VALUE_0"]);
        Assert.Equal("core.fsmonitor", environment["GIT_CONFIG_KEY_1"]);
        Assert.Equal("false", environment["GIT_CONFIG_VALUE_1"]);
        Assert.True(Directory.Exists(binary.NoHooksPath));
        Assert.Empty(Directory.GetFileSystemEntries(binary.NoHooksPath));
    }

    [Fact]
    public void Git_variables_are_stripped_whatever_their_case_where_names_ignore_case()
    {
        var parent = new Dictionary<string, string> { ["git_dir"] = "/elsewhere/.git", ["Git_Work_Tree"] = "/elsewhere" };

        var binary = Create(MakeBundle("cmd/git.exe"), _windows, parent);

        Assert.DoesNotContain(binary.BaseEnvironment.Keys, key => key.StartsWith("GIT_DIR", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(binary.BaseEnvironment.Keys, key => key.StartsWith("GIT_WORK_TREE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Variables_that_are_not_git_variables_pass_through()
    {
        var parent = new Dictionary<string, string>
        {
            ["PATH"] = "/usr/bin",
            ["HOME"] = "/home/someone",
            ["SystemRoot"] = "C:\\Windows",
            ["GITHUB_TOKEN"] = "stays",
        };

        var binary = Create(MakeBundle("bin/git"), _linux, parent);

        Assert.Equal("/usr/bin", binary.BaseEnvironment.GetValueOrDefault("PATH"));
        Assert.Equal("/home/someone", binary.BaseEnvironment.GetValueOrDefault("HOME"));
        Assert.Equal("C:\\Windows", binary.BaseEnvironment.GetValueOrDefault("SystemRoot"));
        Assert.Equal("stays", binary.BaseEnvironment.GetValueOrDefault("GITHUB_TOKEN"));
    }

    [Fact]
    public void System_config_is_off_and_global_config_is_the_file_this_app_owns()
    {
        var binary = Create(MakeBundle("bin/git"), _linux);

        Assert.Equal("1", binary.BaseEnvironment.GetValueOrDefault("GIT_CONFIG_NOSYSTEM"));
        Assert.Equal("1", binary.BaseEnvironment.GetValueOrDefault("GIT_ATTR_NOSYSTEM"));
        Assert.Equal(binary.GlobalConfigPath, binary.BaseEnvironment.GetValueOrDefault("GIT_CONFIG_GLOBAL"));
        Assert.StartsWith(Path.GetFullPath(Path.Combine(_temp, "config")), binary.GlobalConfigPath);
        Assert.True(File.Exists(binary.GlobalConfigPath));
    }

    [Fact]
    public void Identity_is_fixed()
    {
        var binary = Create(MakeBundle("bin/git"), _linux);

        Assert.Equal("SavePoint", binary.BaseEnvironment.GetValueOrDefault("GIT_AUTHOR_NAME"));
        Assert.Equal("savepoint@localhost", binary.BaseEnvironment.GetValueOrDefault("GIT_AUTHOR_EMAIL"));
        Assert.Equal("SavePoint", binary.BaseEnvironment.GetValueOrDefault("GIT_COMMITTER_NAME"));
        Assert.Equal("savepoint@localhost", binary.BaseEnvironment.GetValueOrDefault("GIT_COMMITTER_EMAIL"));
    }

    [Fact]
    public void Prompts_are_off()
    {
        var binary = Create(MakeBundle("bin/git"), _linux);

        Assert.Equal("0", binary.BaseEnvironment.GetValueOrDefault("GIT_TERMINAL_PROMPT"));
        Assert.Equal(string.Empty, binary.BaseEnvironment.GetValueOrDefault("GIT_ASKPASS"));
    }

    [Fact]
    public void Output_language_is_pinned_to_C()
    {
        var parent = new Dictionary<string, string> { ["LANG"] = "de_DE.UTF-8", ["LC_ALL"] = "fr_FR.UTF-8", ["LANGUAGE"] = "de" };

        var binary = Create(MakeBundle("bin/git"), _linux, parent);

        Assert.Equal("C", binary.BaseEnvironment.GetValueOrDefault("LANG"));
        Assert.Equal("C", binary.BaseEnvironment.GetValueOrDefault("LC_ALL"));
        Assert.Equal("C", binary.BaseEnvironment.GetValueOrDefault("LANGUAGE"));
    }

    [Fact]
    public void A_Dugite_bundle_points_git_at_its_own_helpers_and_templates()
    {
        var root = MakeBundle("bin/git");

        var binary = Create(root, _mac);

        var full = Path.GetFullPath(root);
        Assert.Equal(Path.Combine(full, "libexec", "git-core"), binary.BaseEnvironment.GetValueOrDefault("GIT_EXEC_PATH"));
        Assert.Equal(Path.Combine(full, "share", "git-core", "templates"), binary.BaseEnvironment.GetValueOrDefault("GIT_TEMPLATE_DIR"));
        Assert.False(binary.BaseEnvironment.ContainsKey("PREFIX"));
    }

    [Fact]
    public void A_Dugite_bundle_on_Linux_also_sets_the_prefix()
    {
        var root = MakeBundle("bin/git");

        var binary = Create(root, _linux);

        Assert.Equal(Path.GetFullPath(root), binary.BaseEnvironment.GetValueOrDefault("PREFIX"));
    }

    [Fact]
    public void A_MinGit_bundle_run_through_its_launcher_leaves_helper_lookup_to_the_launcher()
    {
        var parent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Path"] = @"C:\Windows\system32" };

        var binary = Create(MakeBundle("cmd/git.exe"), _windows, parent);

        Assert.Equal(@"C:\Windows\system32", binary.BaseEnvironment.GetValueOrDefault("PATH"));
        Assert.False(binary.BaseEnvironment.ContainsKey("GIT_EXEC_PATH"));
        Assert.False(binary.BaseEnvironment.ContainsKey("GIT_TEMPLATE_DIR"));
        Assert.False(binary.BaseEnvironment.ContainsKey("PREFIX"));
    }

    [Fact]
    public void Writing_the_config_twice_leaves_an_unchanged_file_alone()
    {
        var root = MakeBundle("bin/git");
        var first = Create(root, _linux);
        Assert.True(File.Exists(first.GlobalConfigPath));
        var stamp = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(first.GlobalConfigPath, stamp);

        var second = Create(root, _linux);

        Assert.Equal(first.GlobalConfigPath, second.GlobalConfigPath);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(second.GlobalConfigPath));
    }

    private GitBinary Create(string root, GitHost host, IReadOnlyDictionary<string, string>? parent = null) =>
        new(root, Path.Combine(_temp, "config"), parent ?? new Dictionary<string, string>(), host);

    private string MakeBundle(params string[] files)
    {
        var root = Path.Combine(_temp, "bundle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (var file in files)
        {
            var path = Path.Combine(root, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, []);
        }

        return root;
    }
}
