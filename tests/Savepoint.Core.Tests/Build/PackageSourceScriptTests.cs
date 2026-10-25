using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Savepoint.Core.Tests.Git;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Build;

// Runs the source-archive helpers of build/package.ps1 (under pwsh) and build/package.sh (under bash, off Windows) on small made-up lists
// and folders. Nothing is downloaded.
public sealed class PackageSourceScriptTests : IDisposable
{
    private static string RepoRoot => TestEnvironment.RepoRoot ?? throw new InvalidOperationException("These tests read the repository's build files.");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "savepoint-pkgsrc-" + Guid.NewGuid().ToString("N"));

    public void Dispose() => GitWorkspace.DeleteDirectory(_root);

    private sealed record Pin(string File, string Url, string Sha256);

    private static readonly Pin[] _pins =
    [
        new("bash-5.3.015-2.src.tar.gz", "https://example.invalid/a/bash-5.3.015-2.src.tar.gz", "817A480AA08F0F112AF9496A9335D3A48F4C479EF5E1B1740DF6DCC76670D94A"),
        new("dash-0.5.13.4-1.src.tar.zst", "https://example.invalid/b/dash-0.5.13.4-1.src.tar.zst", "ee99c1925fc4cd90ffcd8e5bd100edb40628fd9659423ad8294dc376ceb44c1c"),
        new("zlib-1.3.1-1.src.tar.zst", "https://example.invalid/c/zlib-1.3.1-1.src.tar.zst", ""),
    ];

    private static readonly string[] _expectedLines =
    [
        "bash-5.3.015-2.src.tar.gz https://example.invalid/a/bash-5.3.015-2.src.tar.gz 817a480aa08f0f112af9496a9335d3a48f4c479ef5e1b1740df6dcc76670d94a",
        "dash-0.5.13.4-1.src.tar.zst https://example.invalid/b/dash-0.5.13.4-1.src.tar.zst ee99c1925fc4cd90ffcd8e5bd100edb40628fd9659423ad8294dc376ceb44c1c",
        "zlib-1.3.1-1.src.tar.zst https://example.invalid/c/zlib-1.3.1-1.src.tar.zst ",
    ];

    public enum Layout
    {
        Minified,
        FourSpaces,
        Tabs,
        ReorderedWithExtras,
    }

    // The same list in a different layout: whitespace, key order and extra properties differ, the content does not.
    private string WriteList(Layout layout)
    {
        JsonNode Entry(Pin pin, bool extras)
        {
            var node = new JsonObject();
            if (extras)
            {
                node["provides"] = "x 1";
                node["size"] = 12;
                node["sha256"] = pin.Sha256;
                node["url"] = pin.Url;
                node["file"] = pin.File;
            }
            else
            {
                node["file"] = pin.File;
                node["url"] = pin.Url;
                node["sha256"] = pin.Sha256;
                node["size"] = 12;
                node["provides"] = "x 1";
            }

            return node;
        }

        var archives = new JsonArray([.. _pins.Select(pin => Entry(pin, layout == Layout.ReorderedWithExtras))]);
        var document = new JsonObject();
        if (layout == Layout.ReorderedWithExtras)
        {
            document["archives"] = archives;
            document["bundle"] = "win-x64";
        }
        else
        {
            document["bundle"] = "win-x64";
            document["archives"] = archives;
        }

        var options = layout switch
        {
            Layout.Minified or Layout.ReorderedWithExtras => new JsonWriterOptions { Indented = layout == Layout.ReorderedWithExtras, IndentSize = 1 },
            Layout.FourSpaces => new JsonWriterOptions { Indented = true, IndentSize = 4 },
            _ => new JsonWriterOptions { Indented = true, IndentCharacter = '\t', IndentSize = 1 },
        };
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, options))
        {
            document.WriteTo(writer);
        }

        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "list-" + layout + ".json");
        File.WriteAllBytes(path, stream.ToArray());
        return path;
    }

    private static string Lib(string name) => Path.Combine(RepoRoot, "build", name);

    private static string BashQuote(string text) => "'" + text.Replace("'", "'\\''") + "'";

    private static string PwshQuote(string text) => "'" + text.Replace("'", "''") + "'";

    private static async Task<string> RunAsync(ProcessStartInfo info, string shell)
    {
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.UseShellExecute = false;
        Process? process = null;
        try
        {
            process = Process.Start(info);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Not every machine has the shell on its path.
        }

        Assert.SkipWhen(process is null, shell + " is not installed here.");
        using (process)
        {
            var output = process!.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            var text = (await output).Replace("\r\n", "\n");
            Assert.True(process.ExitCode == 0, $"{shell} failed ({process.ExitCode}): {await error}\n{text}");
            return text;
        }
    }

    private async Task<string> PwshAsync(string body)
    {
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "probe-" + Guid.NewGuid().ToString("N")[..8] + ".ps1");
        await File.WriteAllTextAsync(file, $". {PwshQuote(Lib("package-sources.ps1"))}\n$ErrorActionPreference = 'Stop'\n{body}\n", new UTF8Encoding(false), TestContext.Current.CancellationToken);
        var info = new ProcessStartInfo("pwsh") { ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", file } };
        return await RunAsync(info, "pwsh");
    }

    private async Task<string> BashAsync(string body)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "On Windows the bash on the path may be a different system's, so the bash helpers are tested where bash is the shell.");
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "probe-" + Guid.NewGuid().ToString("N")[..8] + ".sh");
        await File.WriteAllTextAsync(file, $"set -euo pipefail\n. {BashQuote(Lib("package-sources.sh"))}\n{body}\n", new UTF8Encoding(false), TestContext.Current.CancellationToken);
        var info = new ProcessStartInfo("bash") { ArgumentList = { file } };
        return await RunAsync(info, "bash");
    }

    private async Task<string[]> PwshListAsync(string manifest)
    {
        var output = await PwshAsync($"Get-PackageSourceArchives -ManifestPath {PwshQuote(manifest)} | ForEach-Object {{ '{{0}} {{1}} {{2}}' -f $_.File, $_.Url, $_.Sha256 }}");
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private async Task<string[]> BashListAsync(string manifest)
    {
        var output = await BashAsync($"package_source_pins {BashQuote(manifest)}");
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    [Theory]
    [InlineData(Layout.Minified)]
    [InlineData(Layout.FourSpaces)]
    [InlineData(Layout.Tabs)]
    [InlineData(Layout.ReorderedWithExtras)]
    public async Task The_powershell_script_reads_the_same_archives_whatever_the_layout_of_the_list(Layout layout)
    {
        var lines = await PwshListAsync(WriteList(layout));

        Assert.Equal(_expectedLines.Select(line => line.TrimEnd()), lines.Select(line => line.TrimEnd()));
    }

    [Theory]
    [InlineData(Layout.Minified)]
    [InlineData(Layout.FourSpaces)]
    [InlineData(Layout.Tabs)]
    [InlineData(Layout.ReorderedWithExtras)]
    public async Task The_bash_script_reads_the_same_archives_whatever_the_layout_of_the_list(Layout layout)
    {
        var lines = await BashListAsync(WriteList(layout));

        Assert.Equal(_expectedLines.Select(line => line.TrimEnd()), lines.Select(line => line.TrimEnd()));
    }

    [Fact]
    public async Task Both_scripts_read_every_archive_of_the_real_list_as_file_url_and_hash()
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(RepoRoot, "build", "msys2-sources.json"), TestContext.Current.CancellationToken));
        var expected = document.RootElement.GetProperty("archives").EnumerateArray()
            .Select(archive => $"{archive.GetProperty("file").GetString()} {archive.GetProperty("url").GetString()} {archive.GetProperty("sha256").GetString()}")
            .ToList();
        Assert.NotEmpty(expected);
        var real = Path.Combine(RepoRoot, "build", "msys2-sources.json");

        Assert.Equal(expected, await PwshListAsync(real));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(expected, await BashListAsync(real));
        }
    }

    private static readonly string[] _expectedTagLines =
    [
        "https://example.invalid/dugite v2.53.0-4 6600bdf3c7e2cbb127ac63d9d38da3a91267610c6034ecb8ff73e94f89508f33",
        "https://example.invalid/git v2.53.0 0492bcd8a9fc99bedca75dbec7c8c15b24dd1501c82d4518294ee0850f91f219",
        "https://example.invalid/gfw v2.53.0.windows.1 ",
    ];

    // The manifest has other sections around "sources"; the three entries come in a different order than the keys sort in.
    private string WriteTagManifest(Layout layout)
    {
        JsonObject Entry(string repository, string tag, string sha, bool reversed)
        {
            var node = new JsonObject();
            var pairs = new List<(string, JsonNode)> { ("repository", repository), ("tag", tag), ("sha256", sha), ("hashSource", "x") };
            if (reversed)
            {
                pairs.Reverse();
            }

            foreach (var (key, value) in pairs)
            {
                node[key] = value;
            }

            return node;
        }

        var reversed = layout == Layout.ReorderedWithExtras;
        var sources = new JsonObject
        {
            ["git-for-windows"] = Entry("https://example.invalid/gfw", "v2.53.0.windows.1", "", reversed),
            ["git"] = Entry("https://example.invalid/git", "v2.53.0", "0492BCD8A9FC99BEDCA75DBEC7C8C15B24DD1501C82D4518294EE0850F91F219", reversed),
            ["dugite-native"] = Entry("https://example.invalid/dugite", "v2.53.0-4", "6600bdf3c7e2cbb127ac63d9d38da3a91267610c6034ecb8ff73e94f89508f33", reversed),
        };
        var document = new JsonObject();
        if (reversed)
        {
            document["sources"] = sources;
            document["win-x64"] = new JsonObject { ["version"] = "1" };
        }
        else
        {
            document["win-x64"] = new JsonObject { ["version"] = "1" };
            document["sources"] = sources;
        }

        var options = layout switch
        {
            Layout.Minified => new JsonWriterOptions { Indented = false },
            Layout.FourSpaces => new JsonWriterOptions { Indented = true, IndentSize = 4 },
            Layout.Tabs => new JsonWriterOptions { Indented = true, IndentCharacter = '\t', IndentSize = 1 },
            _ => new JsonWriterOptions { Indented = true, IndentSize = 1 },
        };
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, options))
        {
            document.WriteTo(writer);
        }

        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "tags-" + layout + ".json");
        File.WriteAllBytes(path, stream.ToArray());
        return path;
    }

    [Theory]
    [InlineData(Layout.Minified)]
    [InlineData(Layout.FourSpaces)]
    [InlineData(Layout.Tabs)]
    [InlineData(Layout.ReorderedWithExtras)]
    public async Task The_powershell_script_reads_the_same_tag_sources_whatever_the_layout_of_the_manifest(Layout layout)
    {
        var output = await PwshAsync($"Get-TagSourcePins -ManifestPath {PwshQuote(WriteTagManifest(layout))} | ForEach-Object {{ '{{0}} {{1}} {{2}}' -f $_.Repository, $_.Tag, $_.Sha256 }}");

        Assert.Equal(_expectedTagLines.Select(line => line.TrimEnd()), output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd()));
    }

    [Theory]
    [InlineData(Layout.Minified)]
    [InlineData(Layout.FourSpaces)]
    [InlineData(Layout.Tabs)]
    [InlineData(Layout.ReorderedWithExtras)]
    public async Task The_bash_script_reads_the_same_tag_sources_whatever_the_layout_of_the_manifest(Layout layout)
    {
        var output = await BashAsync($"tag_source_pins {BashQuote(WriteTagManifest(layout))}");

        Assert.Equal(_expectedTagLines.Select(line => line.TrimEnd()), output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd()));
    }

    [Fact]
    public async Task Both_scripts_read_the_three_tags_of_the_real_manifest_alike()
    {
        var real = Path.Combine(RepoRoot, "build", "git-manifest.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(real, TestContext.Current.CancellationToken));
        var expected = document.RootElement.GetProperty("sources").EnumerateObject().OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .Select(entry => $"{entry.Value.GetProperty("repository").GetString()} {entry.Value.GetProperty("tag").GetString()} {entry.Value.GetProperty("sha256").GetString()}")
            .ToList();
        Assert.Equal(3, expected.Count);

        var pwsh = await PwshAsync($"Get-TagSourcePins -ManifestPath {PwshQuote(real)} | ForEach-Object {{ '{{0}} {{1}} {{2}}' -f $_.Repository, $_.Tag, $_.Sha256 }}");
        Assert.Equal(expected, pwsh.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(expected, (await BashAsync($"tag_source_pins {BashQuote(real)}")).Split('\n', StringSplitOptions.RemoveEmptyEntries));
        }
    }

    // A folder as an earlier run left it: one archive the list still names, two that an older list named (one half-downloaded), a folder
    // with a file inside, and a note.
    private string MakeFolder()
    {
        var folder = Path.Combine(_root, "packages-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        foreach (var name in new[] { "bash-5.3.015-2.src.tar.gz", "bash-5.2.0-1.src.tar.gz", "old-1.0-1.src.tar.zst.part", "sub/inner.txt", "notes.txt" })
        {
            File.WriteAllText(Path.Combine(folder, name), "x");
        }

        return folder;
    }

    private static void AssertOnlyTheListedOnesStay(string folder)
    {
        Assert.True(File.Exists(Path.Combine(folder, "bash-5.3.015-2.src.tar.gz")), "an archive the list names stays");
        Assert.False(File.Exists(Path.Combine(folder, "bash-5.2.0-1.src.tar.gz")), "an archive an older list named goes");
        Assert.False(File.Exists(Path.Combine(folder, "old-1.0-1.src.tar.zst.part")), "a half-downloaded file of an older list goes");
        Assert.False(File.Exists(Path.Combine(folder, "notes.txt")), "any other file in the folder goes");
        Assert.True(File.Exists(Path.Combine(folder, "sub", "inner.txt")), "only files directly in the folder are touched");
    }

    [Fact]
    public async Task The_powershell_script_removes_the_files_the_list_does_not_name_and_says_which()
    {
        var folder = MakeFolder();
        var elsewhere = Path.Combine(_root, "elsewhere.txt");
        await File.WriteAllTextAsync(elsewhere, "keep", TestContext.Current.CancellationToken);
        var linked = false;
        try
        {
            File.CreateSymbolicLink(Path.Combine(folder, "link.tar.gz"), elsewhere);
            linked = true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Creating a link needs a privilege on Windows that not every account has.
        }

        var output = await PwshAsync($"$listed = @(Get-PackageSourceArchives -ManifestPath {PwshQuote(WriteList(Layout.FourSpaces))})\nRemove-StalePackageSources -Directory {PwshQuote(folder)} -Archives $listed | ForEach-Object {{ 'removed ' + $_ }}");

        AssertOnlyTheListedOnesStay(folder);
        Assert.Contains("removed bash-5.2.0-1.src.tar.gz", output, StringComparison.Ordinal);
        Assert.Contains("removed old-1.0-1.src.tar.zst.part", output, StringComparison.Ordinal);
        Assert.DoesNotContain("bash-5.3.015-2.src.tar.gz", output, StringComparison.Ordinal);
        if (linked)
        {
            Assert.NotNull(new FileInfo(Path.Combine(folder, "link.tar.gz")).LinkTarget);
            Assert.True(File.Exists(elsewhere), "a link is left alone, and so is what it points at");
        }
    }

    [Fact]
    public async Task The_bash_script_removes_the_files_the_list_does_not_name_and_says_which()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Bash helpers run where bash is the shell.");
        var folder = MakeFolder();
        var elsewhere = Path.Combine(_root, "elsewhere.txt");
        await File.WriteAllTextAsync(elsewhere, "keep", TestContext.Current.CancellationToken);
        File.CreateSymbolicLink(Path.Combine(folder, "link.tar.gz"), elsewhere);

        var output = await BashAsync($"remove_stale_package_sources {BashQuote(folder)} \"$(package_source_pins {BashQuote(WriteList(Layout.FourSpaces))})\"");

        AssertOnlyTheListedOnesStay(folder);
        Assert.Contains("bash-5.2.0-1.src.tar.gz", output, StringComparison.Ordinal);
        Assert.Contains("old-1.0-1.src.tar.zst.part", output, StringComparison.Ordinal);
        Assert.DoesNotContain("bash-5.3.015-2.src.tar.gz", output, StringComparison.Ordinal);
        Assert.NotNull(new FileInfo(Path.Combine(folder, "link.tar.gz")).LinkTarget);
        Assert.True(File.Exists(elsewhere), "a link is left alone, and so is what it points at");
    }

    [Fact]
    public async Task The_powershell_script_given_an_empty_list_removes_nothing()
    {
        var folder = MakeFolder();

        await Assert.ThrowsAnyAsync<Exception>(() => PwshAsync($"Remove-StalePackageSources -Directory {PwshQuote(folder)} -Archives @()"));

        Assert.True(File.Exists(Path.Combine(folder, "bash-5.2.0-1.src.tar.gz")));
        Assert.True(File.Exists(Path.Combine(folder, "notes.txt")));
    }

    [Fact]
    public async Task The_bash_script_given_an_empty_list_removes_nothing()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Bash helpers run where bash is the shell.");
        var folder = MakeFolder();

        await Assert.ThrowsAnyAsync<Exception>(() => BashAsync($"remove_stale_package_sources {BashQuote(folder)} ''"));

        Assert.True(File.Exists(Path.Combine(folder, "bash-5.2.0-1.src.tar.gz")));
        Assert.True(File.Exists(Path.Combine(folder, "notes.txt")));
    }

    [Fact]
    public async Task Both_scripts_leave_a_folder_that_is_not_there_alone()
    {
        var missing = Path.Combine(_root, "never-made");

        await PwshAsync($"Remove-StalePackageSources -Directory {PwshQuote(missing)} -Archives @([pscustomobject]@{{ File = 'a'; Url = 'u'; Sha256 = '' }})");
        if (!OperatingSystem.IsWindows())
        {
            await BashAsync($"remove_stale_package_sources {BashQuote(missing)} 'a u '");
        }

        Assert.False(Directory.Exists(missing));
    }

    private static string CodeOf(string script) =>
        string.Join('\n', File.ReadAllLines(Path.Combine(RepoRoot, script)).Where(line => !line.TrimStart().StartsWith('#')));

    [Fact]
    public void Each_package_script_cleans_the_folder_before_it_downloads_anything_into_it()
    {
        var bash = CodeOf("build/package.sh");
        var pwsh = CodeOf("build/package.ps1");

        var bashClean = bash.IndexOf("remove_stale_package_sources", StringComparison.Ordinal);
        var bashDownload = bash.IndexOf("download \"$url\" \"$partial\"", StringComparison.Ordinal);
        Assert.True(bashClean >= 0 && bashClean < bashDownload, "package.sh has to clean the packages folder before its first download.");
        var pwshClean = pwsh.IndexOf("Remove-StalePackageSources", StringComparison.Ordinal);
        var pwshDownload = pwsh.IndexOf("Invoke-WebRequest -Uri $package.Url", StringComparison.Ordinal);
        Assert.True(pwshClean >= 0 && pwshClean < pwshDownload, "package.ps1 has to clean the packages folder before its first download.");
    }

    [Fact]
    public void The_bash_script_no_longer_reads_the_package_list_with_awk()
    {
        var package = CodeOf("build/package.sh");

        Assert.DoesNotContain("awk", CodeOf("build/package-sources.sh"), StringComparison.Ordinal);
        Assert.DoesNotContain("awk", package, StringComparison.Ordinal);
        Assert.Contains("package_source_pins build/msys2-sources.json", package, StringComparison.Ordinal);
        Assert.Contains("tag_source_pins build/git-manifest.json", package, StringComparison.Ordinal);
    }
}
