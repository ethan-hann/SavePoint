using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Savepoint.Core.Tests.Git;

namespace Savepoint.Core.Tests.Build;

// Runs build/fetch-git.sh's prune step (no download) against a made-up manifest written in different layouts: what the script reads from
// the file must not depend on whitespace, key order or extra properties.
public sealed class FetchGitManifestLayoutTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "savepoint-fetchlayout-" + Guid.NewGuid().ToString("N"));

    public void Dispose() => GitWorkspace.DeleteDirectory(_root);

    private string WriteManifest(PackageSourceScriptTests.Layout layout)
    {
        var reversed = layout == PackageSourceScriptTests.Layout.ReorderedWithExtras;

        JsonObject Ordered(params (string Key, JsonNode Value)[] pairs)
        {
            var node = new JsonObject();
            foreach (var (key, value) in reversed ? pairs.Reverse() : pairs)
            {
                node[key] = value;
            }

            return node;
        }

        var layouts = Ordered(("mingit", Ordered(("note", "x"), ("prune", new JsonArray("ucrt64/bin/libcurl-4.dll", "ucrt64/bin/*.tmp")))));
        var runtime = Ordered(
            ("version", "1.0"),
            ("url", "https://example.invalid/mingit.zip"),
            ("sha256", "0000000000000000000000000000000000000000000000000000000000000000"),
            ("archive", "zip"),
            ("layout", "mingit"),
            ("entry", "cmd/git.exe"),
            ("size", 12));
        var document = new JsonObject();
        var sections = new List<(string, JsonNode)> { ("layouts", layouts), ("win-x64", runtime), ("sources", new JsonObject { ["git"] = new JsonObject { ["tag"] = "v1" } }) };
        if (reversed)
        {
            sections.Reverse();
        }

        foreach (var (key, value) in sections)
        {
            document[key] = value;
        }

        var options = layout switch
        {
            PackageSourceScriptTests.Layout.Minified => new JsonWriterOptions { Indented = false },
            PackageSourceScriptTests.Layout.FourSpaces => new JsonWriterOptions { Indented = true, IndentSize = 4 },
            PackageSourceScriptTests.Layout.Tabs => new JsonWriterOptions { Indented = true, IndentCharacter = '\t', IndentSize = 1 },
            _ => new JsonWriterOptions { Indented = true, IndentSize = 1 },
        };
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, options))
        {
            document.WriteTo(writer);
        }

        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "manifest-" + layout + ".json");
        File.WriteAllBytes(path, stream.ToArray());
        return path;
    }

    private string MakeBundle(params string[] files)
    {
        var bundle = Path.Combine(_root, "bundle-" + Guid.NewGuid().ToString("N")[..8], "tree");
        foreach (var relative in files)
        {
            var path = Path.Combine(bundle, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "x");
        }

        return bundle;
    }

    private static async Task<(int ExitCode, string Output)> PruneAsync(string rid, string bundle, string manifest)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "On Windows the bash on the path may be a different system's, so the bash script is tested where bash is the shell.");
        var script = Path.Combine(GitManifestFile.BuildFolder, "fetch-git.sh");
        var info = new ProcessStartInfo("bash") { ArgumentList = { script, rid, "--prune-only", bundle, "--manifest", manifest }, RedirectStandardOutput = true, RedirectStandardError = true };
        Process? process = null;
        try
        {
            process = Process.Start(info);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Not every machine has bash on its path.
        }

        Assert.SkipWhen(process is null, "bash is not installed here.");
        using (process)
        {
            var output = process!.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            return (process.ExitCode, await output + await error);
        }
    }

    [Theory]
    [InlineData(PackageSourceScriptTests.Layout.Minified)]
    [InlineData(PackageSourceScriptTests.Layout.FourSpaces)]
    [InlineData(PackageSourceScriptTests.Layout.Tabs)]
    [InlineData(PackageSourceScriptTests.Layout.ReorderedWithExtras)]
    public async Task The_bash_script_prunes_with_the_list_of_the_runtimes_layout_whatever_the_layout_of_the_manifest(PackageSourceScriptTests.Layout layout)
    {
        var bundle = MakeBundle("cmd/git.exe", "ucrt64/bin/git.exe", "ucrt64/bin/libcurl-4.dll", "ucrt64/bin/scratch.tmp");

        var run = await PruneAsync("win-x64", bundle, WriteManifest(layout));

        Assert.True(run.ExitCode == 0, run.Output);
        Assert.False(File.Exists(Path.Combine(bundle, "ucrt64/bin/libcurl-4.dll")), "a file the layout's list names goes");
        Assert.False(File.Exists(Path.Combine(bundle, "ucrt64/bin/scratch.tmp")), "a file a pattern of the list matches goes");
        Assert.True(File.Exists(Path.Combine(bundle, "cmd/git.exe")));
        Assert.True(File.Exists(Path.Combine(bundle, "ucrt64/bin/git.exe")));
    }

    [Fact]
    public async Task The_bash_script_names_a_runtime_id_the_manifest_does_not_have()
    {
        var bundle = MakeBundle("cmd/git.exe");

        var run = await PruneAsync("linux-riscv", bundle, WriteManifest(PackageSourceScriptTests.Layout.Tabs));

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("linux-riscv", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_bash_script_refuses_a_manifest_that_is_not_json()
    {
        var bundle = MakeBundle("cmd/git.exe", "ucrt64/bin/libcurl-4.dll");
        Directory.CreateDirectory(_root);
        var broken = Path.Combine(_root, "broken.json");
        await File.WriteAllTextAsync(broken, "{ \"win-x64\": ", new UTF8Encoding(false), TestContext.Current.CancellationToken);

        var run = await PruneAsync("win-x64", bundle, broken);

        Assert.NotEqual(0, run.ExitCode);
        Assert.True(File.Exists(Path.Combine(bundle, "ucrt64/bin/libcurl-4.dll")), "nothing is pruned from a manifest that could not be read");
    }

    [Fact]
    public void The_bash_script_no_longer_reads_the_manifest_with_awk()
    {
        var code = string.Join('\n', File.ReadAllLines(Path.Combine(GitManifestFile.BuildFolder, "fetch-git.sh")).Where(line => !line.TrimStart().StartsWith('#')));

        Assert.DoesNotContain("awk", code, StringComparison.Ordinal);
    }
}
