using System.Text.Json;
using System.Text.RegularExpressions;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Build;

// The notices promise the sources of the MSYS2 and MINGW packages inside the Windows git bundle. build/msys2-sources.json lists them with
// pinned hashes, and both package scripts download and compare from it. None of these tests touches the network.
public partial class PackageSourcesTests
{
    private static string RepoRoot => TestEnvironment.RepoRoot ?? throw new InvalidOperationException("These tests read the repository's build files.");

    private sealed record Archive(string File, string Url, string Sha256, long Size, string[] Provides);

    private static JsonElement Root()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "build", "msys2-sources.json")));
        return document.RootElement.Clone();
    }

    private static IReadOnlyList<Archive> Archives() =>
        [.. Root().GetProperty("archives").EnumerateArray().Select(archive => new Archive(
            archive.GetProperty("file").GetString()!,
            archive.GetProperty("url").GetString()!,
            archive.GetProperty("sha256").GetString()!,
            archive.GetProperty("size").GetInt64(),
            archive.GetProperty("provides").GetString()!.Split(", ", StringSplitOptions.RemoveEmptyEntries)))];

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Pattern();

    [Fact]
    public void Every_archive_has_a_download_address_named_after_its_file_and_a_lowercase_sha256()
    {
        var archives = Archives();

        Assert.NotEmpty(archives);
        Assert.All(archives, archive =>
        {
            Assert.StartsWith("https://", archive.Url, StringComparison.Ordinal);
            Assert.EndsWith("/" + archive.File, archive.Url, StringComparison.Ordinal);
            Assert.Matches(@"\.src\.tar\.(gz|zst)$", archive.File);
            Assert.True(Sha256Pattern().IsMatch(archive.Sha256), $"{archive.File} does not pin a 64 digit lowercase SHA-256.");
            Assert.True(archive.Size > 0, $"{archive.File} has no size.");
            Assert.NotEmpty(archive.Provides);
        });
    }

    [Fact]
    public void Archives_come_from_msys2_or_from_the_git_for_windows_package_releases()
    {
        Assert.All(Archives(), archive => Assert.True(
            archive.Url.StartsWith("https://repo.msys2.org/msys/sources/", StringComparison.Ordinal)
            || archive.Url.StartsWith("https://repo.msys2.org/mingw/sources/", StringComparison.Ordinal)
            || archive.Url.StartsWith("https://github.com/git-for-windows/pacman-repo/releases/download/", StringComparison.Ordinal),
            $"{archive.File} is not hosted where the sources are published."));
    }

    [Fact]
    public void No_file_name_or_package_is_listed_twice()
    {
        var archives = Archives();

        Assert.Equal(archives.Count, archives.Select(archive => archive.File).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(archives.Sum(archive => archive.Provides.Length), archives.SelectMany(archive => archive.Provides).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void The_list_is_for_the_version_of_the_windows_bundle_the_manifest_fetches()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(GitManifestFile.ManifestPath));
        var fetched = document.RootElement.GetProperty("win-x64").GetProperty("version").GetString();

        Assert.Equal("win-x64", Root().GetProperty("bundle").GetString());
        Assert.Equal(fetched, Root().GetProperty("bundleVersion").GetString());
    }

    [Fact]
    public void Every_package_the_windows_bundle_lists_has_its_source_in_the_list()
    {
        // Only a Windows build copies the bundle next to the tests; elsewhere there is no package list to compare with.
        var listing = Path.Combine(AppContext.BaseDirectory, "git", "etc", "package-versions.txt");
        Assert.SkipUnless(File.Exists(listing), "Only a Windows build copies the git bundle next to the tests, so there is no package list to compare with.");

        var inBundle = File.ReadAllLines(listing).Select(line => line.Trim()).Where(line => line.Length > 0).ToHashSet(StringComparer.Ordinal);
        var covered = Archives().SelectMany(archive => archive.Provides).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(inBundle.Except(covered));
        Assert.Empty(covered.Except(inBundle));
    }

    // The scripts' header comments name the manifest and the folder, so only the lines that run count.
    private static string CodeOf(string script) =>
        string.Join('\n', File.ReadAllLines(Path.Combine(RepoRoot, script)).Where(line => !line.TrimStart().StartsWith('#')));

    [Fact]
    public void The_powershell_script_reads_the_manifest_and_downloads_and_compares_each_archive()
    {
        var code = CodeOf("build/package.ps1");

        Assert.Contains("build/msys2-sources.json", code, StringComparison.Ordinal);
        Assert.Contains("foreach ($package in Get-PackageSourceArchives)", code, StringComparison.Ordinal);
        Assert.Contains("Invoke-WebRequest -Uri $package.Url", code, StringComparison.Ordinal);
        Assert.Contains("$package.Sha256 -ne $actual", code, StringComparison.Ordinal);
        Assert.Contains("'packages'", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_bash_script_downloads_and_compares_each_archive_it_parses_from_the_manifest()
    {
        var code = CodeOf("build/package.sh");

        Assert.Contains("build/msys2-sources.json", code, StringComparison.Ordinal);
        Assert.Contains("done <<<\"$package_pins\"", code, StringComparison.Ordinal);
        Assert.Contains("download \"$url\" \"$partial\"", code, StringComparison.Ordinal);
        Assert.Contains("\"$pinned\" != \"$actual\"", code, StringComparison.Ordinal);
        Assert.Contains("$source_dir/packages", code, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_bash_parse_yields_every_archive_of_the_real_manifest_as_file_url_and_hash()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot, "build", "package.sh"));
        const string start = "package_pins=\"$(awk '";
        const string end = "' build/msys2-sources.json)\"";
        var from = script.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, "package.sh no longer parses build/msys2-sources.json with awk.");
        from += start.Length;
        var program = script[from..script.IndexOf(end, from, StringComparison.Ordinal)];

        var folder = Directory.CreateTempSubdirectory("savepoint-awk-");
        try
        {
            var file = Path.Combine(folder.FullName, "parse.awk");
            await File.WriteAllTextAsync(file, program, TestContext.Current.CancellationToken);
            var info = new System.Diagnostics.ProcessStartInfo("awk") { RedirectStandardOutput = true, UseShellExecute = false };
            info.ArgumentList.Add("-f");
            info.ArgumentList.Add(file);
            info.ArgumentList.Add(Path.Combine(RepoRoot, "build", "msys2-sources.json"));
            System.Diagnostics.Process? process = null;
            try
            {
                process = System.Diagnostics.Process.Start(info);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Not every machine has awk on its path.
            }

            Assert.SkipWhen(process is null, "awk is not installed here.");
            using (process)
            {
                var output = await process!.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
                await process.WaitForExitAsync(TestContext.Current.CancellationToken);

                var parsed = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r')).ToList();
                var expected = Archives().Select(archive => $"{archive.File} {archive.Url} {archive.Sha256}").ToList();
                Assert.NotEmpty(expected);
                Assert.Equal(expected, parsed);
            }
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
