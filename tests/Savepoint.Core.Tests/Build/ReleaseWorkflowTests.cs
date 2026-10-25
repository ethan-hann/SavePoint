using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Build;

// The release workflow builds the packages when a version tag is pushed. It cannot run in a test, so the text tests read it for the rules
// that must never slip (what starts it, which scripts it runs, where the token is visible, that nothing publishes a release), and the
// behavior tests run the functions of build/release-assets.ps1 under pwsh on small files.
public partial class ReleaseWorkflowTests
{
    private const string _workflowPath = ".forgejo/workflows/release.yml";
    private const string _scriptPath = "build/release-assets.ps1";

    private static string RepoRoot => TestEnvironment.RepoRoot ?? throw new InvalidOperationException("These tests read the repository's build files.");

    private static string Workflow()
    {
        var path = Path.Combine(RepoRoot, _workflowPath);
        Assert.True(File.Exists(path), $"{_workflowPath} does not exist.");
        return File.ReadAllText(path);
    }

    private static string Script()
    {
        var path = Path.Combine(RepoRoot, _scriptPath);
        Assert.True(File.Exists(path), $"{_scriptPath} does not exist.");
        return File.ReadAllText(path);
    }

    private static string BuildScript(string name) => File.ReadAllText(Path.Combine(RepoRoot, "build", name));

    // The text of one job: from its key under "jobs:" to the next job key.
    private static string Job(string workflow, string name)
    {
        var match = Regex.Match(workflow, $@"(?ms)^  {Regex.Escape(name)}:\r?\n(.*?)(?=^  [a-z][a-z-]*:\r?\n|\z)");
        Assert.True(match.Success, $"The workflow has no job named {name}.");
        return match.Groups[1].Value;
    }

    // The steps of a job, each as its own text.
    private static string[] Steps(string job) =>
        Regex.Split(job[job.IndexOf("    steps:", StringComparison.Ordinal)..], @"\r?\n      - ").Skip(1).ToArray();

    [GeneratedRegex(@"\bbuild/[A-Za-z0-9._/-]+\.(?:ps1|sh|json)\b")]
    private static partial Regex BuildPathPattern();

    [Fact]
    public void Only_a_pushed_version_tag_starts_it()
    {
        var workflow = Workflow();

        var on = Regex.Match(workflow, @"(?ms)^on:\r?\n(.*?)(?=^\S)").Groups[1].Value;
        Assert.Matches(@"(?m)^  push:\s*\r?\n    tags:\s*\[\s*'v\*'\s*\]", on);
        Assert.DoesNotContain("branches", on, StringComparison.Ordinal);
        Assert.DoesNotContain("pull_request", on, StringComparison.Ordinal);
        Assert.DoesNotContain("workflow_dispatch", on, StringComparison.Ordinal);
    }

    // The pwsh dotnet tool is a launcher that starts dotnet from PATH; without it on PATH, pwsh aborts before running anything.
    [Theory]
    [InlineData(".forgejo/workflows/release.yml")]
    [InlineData(".forgejo/workflows/wiki.yml")]
    public void Installing_pwsh_as_a_tool_also_puts_dotnet_on_the_path(string workflowPath)
    {
        var workflow = File.ReadAllText(Path.Combine(RepoRoot, workflowPath));

        var installs = Regex.Matches(workflow, @"(?ms)dotnet tool install --tool-path /opt/pwsh.*?(?=\r?\n          fi)");
        Assert.NotEmpty(installs);
        Assert.All(installs, install => Assert.Contains("echo \"/opt/dotnet\" >> \"$GITHUB_PATH\"", install.Value, StringComparison.Ordinal));
    }

    // Velopack builds the AppImage with mksquashfs, which the container image does not have.
    [Fact]
    public void The_linux_job_installs_mksquashfs_before_it_packs()
    {
        var workflow = Workflow();

        var install = workflow.IndexOf("apt-get install -y --no-install-recommends squashfs-tools", StringComparison.Ordinal);
        var pack = workflow.IndexOf("bash build/package.sh", StringComparison.Ordinal);
        Assert.True(install >= 0, "The release workflow does not install squashfs-tools.");
        Assert.True(pack > install, "squashfs-tools is installed after the AppImage is packed.");
    }

    [Fact]
    public void Every_build_file_it_names_exists()
    {
        var text = Workflow() + Script();

        var named = BuildPathPattern().Matches(text).Select(match => match.Value).Distinct().ToArray();

        Assert.Contains("build/package.sh", named);
        Assert.Contains("build/package.ps1", named);
        Assert.All(named, path => Assert.True(File.Exists(Path.Combine(RepoRoot, path)), $"{path} is named in the release workflow or its script but does not exist."));
    }

    [Fact]
    public void Linux_packs_on_the_container_runner_and_Windows_on_the_windows_runner()
    {
        var workflow = Workflow();

        var linux = Job(workflow, "linux");
        Assert.Contains("runs-on: ubuntu-latest", linux, StringComparison.Ordinal);
        Assert.Contains("build/package.sh", linux, StringComparison.Ordinal);
        Assert.DoesNotContain("package.ps1", linux, StringComparison.Ordinal);

        var windows = Job(workflow, "windows");
        Assert.Contains("runs-on: windows", windows, StringComparison.Ordinal);
        Assert.Contains("build/package.ps1", windows, StringComparison.Ordinal);
        Assert.DoesNotContain("package.sh", windows, StringComparison.Ordinal);
    }

    [Fact]
    public void The_checksums_job_waits_for_both_package_jobs()
    {
        var finish = Job(Workflow(), "checksums");

        Assert.Matches(@"needs:\s*\[\s*linux\s*,\s*windows\s*\]", finish);
    }

    [Fact]
    public void Source_archives_are_not_packed_into_a_release_because_they_have_a_release_of_their_own()
    {
        var workflow = Workflow();

        Assert.DoesNotContain("--sources", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(@"package\.ps1[^\r\n]*-Sources", workflow);
    }

    [Fact]
    public void Each_package_job_refuses_a_tag_that_differs_from_the_version_before_it_builds()
    {
        var workflow = Workflow();

        foreach (var name in new[] { "linux", "windows" })
        {
            var job = Job(workflow, name);
            var verify = job.IndexOf("release-assets.ps1 -Action Verify", StringComparison.Ordinal);
            var pack = job.IndexOf("build/package.", StringComparison.Ordinal);
            Assert.True(verify >= 0, $"The {name} job never verifies the tag.");
            Assert.True(verify < pack, $"The {name} job builds before it verifies the tag.");
        }
    }

    [Fact]
    public void The_upload_directories_and_required_names_are_what_the_package_scripts_produce()
    {
        var workflow = Workflow();
        var script = Script();
        var packageSh = BuildScript("package.sh");
        var packagePs1 = BuildScript("package.ps1");

        // Each package script writes artifacts/release/<rid>; the upload step of the same job reads that folder.
        Assert.Contains("rid=linux-x64", packageSh, StringComparison.Ordinal);
        Assert.Contains("release_dir=\"$repo/artifacts/release/$rid\"", packageSh, StringComparison.Ordinal);
        Assert.Contains("$rid = 'win-x64'", packagePs1, StringComparison.Ordinal);
        Assert.Contains("$releaseDir = Join-Path $repoRoot \"artifacts/release/$rid\"", packagePs1, StringComparison.Ordinal);
        Assert.Contains("-Action Upload -Directory artifacts/release/linux-x64", Job(workflow, "linux"), StringComparison.Ordinal);
        Assert.Contains("-Action Upload -Directory artifacts/release/win-x64", Job(workflow, "windows"), StringComparison.Ordinal);

        // The files a finished release must hold are the ones the scripts rename their packages to.
        var required = Regex.Match(script, @"foreach \(\$required in ([^)\r\n]+)\)").Groups[1].Value
            .Split(',').Select(name => name.Trim().Trim('\'')).ToArray();
        Assert.Equal(["SavePoint.AppImage", "SavePoint-win-Setup.exe", "SavePoint-win-Portable.zip"], required);
        Assert.Contains("$pack_id.AppImage\" \"$release_dir/SavePoint.AppImage\"", packageSh, StringComparison.Ordinal);
        Assert.Contains("foreach ($suffix in '-win-Setup.exe', '-win-Portable.zip')", packagePs1, StringComparison.Ordinal);
        Assert.Contains("\"SavePoint$suffix\"", packagePs1, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_publishes_or_undrafts_a_release()
    {
        var script = Script();
        var both = script + Workflow();

        Assert.Contains("draft = $true", script, StringComparison.Ordinal);
        Assert.Contains("prerelease = $true", script, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?i)draft""?\s*[=:]\s*\$?false", both);
        // The only writes to an existing release are asset uploads and deletes; a PATCH of the release itself would be the way to publish it.
        Assert.DoesNotMatch(@"(?i)(-Method|--request|-X)\s+PATCH", both);
        Assert.DoesNotMatch(@"(?i)(-Method|--request|-X)\s+PUT", both);
    }

    [Fact]
    public void The_tokens_are_set_only_on_the_steps_that_run_the_release_script()
    {
        var workflow = Workflow();

        foreach (var name in new[] { "linux", "windows", "checksums" })
        {
            var job = Job(workflow, name);
            var beforeSteps = job[..job.IndexOf("    steps:", StringComparison.Ordinal)];
            Assert.DoesNotContain("TOKEN", beforeSteps, StringComparison.Ordinal);
            foreach (var step in Steps(job))
            {
                var uses = step.Contains("release-assets.ps1", StringComparison.Ordinal);
                Assert.Equal(uses, step.Contains("secrets.RELEASE_TOKEN", StringComparison.Ordinal));
                Assert.Equal(uses, step.Contains("secrets.GITHUB_TOKEN", StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public void The_token_goes_to_curl_through_a_header_file_with_no_line_ending_and_never_as_an_argument()
    {
        var script = Script();

        Assert.Contains("RELEASE_TOKEN", script, StringComparison.Ordinal);
        Assert.Contains("GITHUB_TOKEN", script, StringComparison.Ordinal);
        // A CRLF piped on Windows would end up inside the header value.
        Assert.DoesNotContain("| & $curl", script, StringComparison.Ordinal);
        Assert.Contains("[System.IO.File]::WriteAllText($headerFile, \"Authorization: token $token\"", script, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?i)--header\s+""?Authorization", script);
    }

    [Fact]
    public void The_checksum_file_and_the_sources_link_have_the_names_the_release_page_shows()
    {
        var script = Script();

        Assert.Contains("SHA256SUMS.txt", script, StringComparison.Ordinal);
        Assert.Contains("Source code of the bundled git", script, StringComparison.Ordinal);
        Assert.Contains("external_url", script, StringComparison.Ordinal);
        Assert.Contains("git-sources-", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_hash_is_made_without_a_method_newer_than_the_oldest_PowerShell_it_asks_for()
    {
        var script = Script();

        // PowerShell 7.4 runs on .NET 8, which has no ToHexStringLower.
        Assert.DoesNotContain("ToHexStringLower", script, StringComparison.Ordinal);
        Assert.Matches(@"#Requires -Version 7\.4", script);
    }

    [Fact]
    public void The_PowerShell_tool_is_installed_at_a_pinned_version()
    {
        var workflow = Workflow();

        Assert.All(
            Regex.Matches(workflow, @"dotnet tool install[^\r\n]*PowerShell[^\r\n]*").Select(match => match.Value),
            line => Assert.Matches(@"--version \d+\.\d+\.\d+", line));
    }

    // --- behavior: the functions of the script, run under pwsh on small files ---

    private static async Task<string> PwshAsync(string body)
    {
        var folder = Directory.CreateTempSubdirectory("savepoint-release-");
        try
        {
            var file = Path.Combine(folder.FullName, "probe.ps1");
            var script = $". '{Path.Combine(RepoRoot, _scriptPath).Replace("'", "''")}' -Action Load\n$ErrorActionPreference = 'Stop'\n{body}\n";
            await File.WriteAllTextAsync(file, script, new UTF8Encoding(false), TestContext.Current.CancellationToken);
            var info = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-NonInteractive");
            info.ArgumentList.Add("-File");
            info.ArgumentList.Add(file);
            Process? process = null;
            try
            {
                process = Process.Start(info);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Not every machine has PowerShell on its path.
            }

            Assert.SkipWhen(process is null, "pwsh is not installed here.");
            using (process)
            {
                var output = process!.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
                var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
                await process.WaitForExitAsync(TestContext.Current.CancellationToken);
                var text = await output;
                Assert.True(process.ExitCode == 0, $"pwsh failed ({process.ExitCode}): {await error}\n{text}");
                return text;
            }
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private static string Quote(string path) => "'" + path.Replace("'", "''") + "'";

    [Fact]
    public async Task A_file_is_cut_into_numbered_parts_that_join_back_into_it()
    {
        var folder = Directory.CreateTempSubdirectory("savepoint-parts-");
        try
        {
            var original = Enumerable.Range(0, 25).Select(i => (byte)(i * 7 + 1)).ToArray();
            var source = Path.Combine(folder.FullName, "big.bin");
            await File.WriteAllBytesAsync(source, original, TestContext.Current.CancellationToken);
            var target = Directory.CreateDirectory(Path.Combine(folder.FullName, "out")).FullName;

            var output = await PwshAsync($"ConvertTo-Json -InputObject @(Split-Asset {Quote(source)} {Quote(target)} -PartSize 10 | ForEach-Object {{ Split-Path -Leaf $_ }})");

            var names = JsonSerializer.Deserialize<string[]>(output)!;
            Assert.Equal(["big.bin.part1", "big.bin.part2", "big.bin.part3"], names);
            Assert.Equal([10L, 10L, 5L], names.Select(name => new FileInfo(Path.Combine(target, name)).Length));
            var joined = names.SelectMany(name => File.ReadAllBytes(Path.Combine(target, name))).ToArray();
            Assert.Equal(original, joined);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_file_that_fills_its_parts_exactly_gets_no_empty_part()
    {
        var folder = Directory.CreateTempSubdirectory("savepoint-parts-");
        try
        {
            var source = Path.Combine(folder.FullName, "even.bin");
            await File.WriteAllBytesAsync(source, new byte[20], TestContext.Current.CancellationToken);
            var target = Directory.CreateDirectory(Path.Combine(folder.FullName, "out")).FullName;

            var output = await PwshAsync($"ConvertTo-Json -InputObject @(Split-Asset {Quote(source)} {Quote(target)} -PartSize 10 | ForEach-Object {{ Split-Path -Leaf $_ }})");

            Assert.Equal(["even.bin.part1", "even.bin.part2"], JsonSerializer.Deserialize<string[]>(output)!);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task The_hash_of_the_parts_in_order_is_the_lowercase_hash_of_the_whole_file()
    {
        var folder = Directory.CreateTempSubdirectory("savepoint-hash-");
        try
        {
            var first = Path.Combine(folder.FullName, "a");
            var second = Path.Combine(folder.FullName, "b");
            await File.WriteAllBytesAsync(first, Encoding.ASCII.GetBytes("hello "), TestContext.Current.CancellationToken);
            await File.WriteAllBytesAsync(second, Encoding.ASCII.GetBytes("world"), TestContext.Current.CancellationToken);

            var output = await PwshAsync($"Get-StreamHash @({Quote(first)}, {Quote(second)})");

            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes("hello world"))).ToLowerInvariant(), output.Trim());
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_file_is_split_only_when_it_is_over_95_million_bytes_and_into_parts_of_90_MiB()
    {
        var output = await PwshAsync("""
            $r = foreach ($length in 94999999, 95000000, 95000001, 200000000) { ,@(Get-AssetNames 'x.bin' $length) }
            ConvertTo-Json -InputObject @($r | ForEach-Object { ,($_ -join ' ') })
            """);

        Assert.Equal(
            ["x.bin", "x.bin", "x.bin.part1 x.bin.part2", "x.bin.part1 x.bin.part2 x.bin.part3"],
            JsonSerializer.Deserialize<string[]>(output)!);
    }

    [Fact]
    public async Task Parts_that_a_run_will_not_send_again_are_stale_and_other_files_are_left_alone()
    {
        var output = await PwshAsync("""
            $existing = 'A.part1', 'A.part2', 'A.part3', 'B.zip', 'C.exe', 'SHA256SUMS.txt'
            $whole = @(Get-StaleAssetNames $existing @('A', 'B.zip'))
            $fewer = @(Get-StaleAssetNames @('A', 'A.part1', 'A.part2', 'A.part3', 'B.zip') @('A.part1', 'A.part2'))
            $same = @(Get-StaleAssetNames @('B.zip') @('B.zip'))
            ConvertTo-Json -InputObject @(($whole -join ' '), ($fewer -join ' '), ($same -join ' '))
            """);

        Assert.Equal(["A.part1 A.part2 A.part3", "A A.part3", ""], JsonSerializer.Deserialize<string[]>(output)!);
    }

    private static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(text))).ToLowerInvariant();

    // Runs the body and returns its result, or the error text when it throws, so a missing function shows up as a wrong value.
    private static string Guarded(string body) => $"try {{ {body} }} catch {{ 'ERROR: ' + $_.Exception.Message }}";

    [Fact]
    public async Task Hashing_before_the_upload_lists_each_file_each_part_and_the_joined_file()
    {
        var folder = Directory.CreateTempSubdirectory("savepoint-sums-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder.FullName, "small.txt"), "tiny", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(folder.FullName, "big.bin"), "0123456789ABCDEFGHIJKLMNOPQRS", TestContext.Current.CancellationToken);
            var scratch = Directory.CreateDirectory(Path.Combine(folder.FullName, "out")).FullName;

            var output = await PwshAsync(Guarded($$"""
                $entries = @(Get-ChildItem -LiteralPath {{Quote(folder.FullName)}} -File | Sort-Object Name | ForEach-Object { Get-SumEntries $_.FullName {{Quote(scratch)}} -SplitAbove 20 -PartSize 10 })
                ConvertTo-Json -InputObject @($entries | ForEach-Object { "$($_.Name) $($_.Size) $($_.Hash) $([bool]$_.Path)" })
                """));

            Assert.Equal(
                [
                    $"big.bin.part1 10 {Sha("0123456789")} True",
                    $"big.bin.part2 10 {Sha("ABCDEFGHIJ")} True",
                    $"big.bin.part3 9 {Sha("KLMNOPQRS")} True",
                    $"big.bin 29 {Sha("0123456789ABCDEFGHIJKLMNOPQRS")} False",
                    $"small.txt 4 {Sha("tiny")} True",
                ],
                JsonSerializer.Deserialize<string[]>(output)!);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_sum_list_is_read_back_into_the_entries_that_were_written()
    {
        var output = await PwshAsync(Guarded("""
            $text = Format-SumList @([pscustomobject]@{ Name = 'b.zip'; Hash = 'bb'; Size = 7 }, [pscustomobject]@{ Name = 'a.exe'; Hash = 'aa'; Size = 3 })
            $back = @(Read-SumList $text)
            ConvertTo-Json -InputObject @(($text -split "`n" | Where-Object { $_ }), @($back | ForEach-Object { "$($_.Name) $($_.Size) $($_.Hash)" }))
            """));

        var results = JsonSerializer.Deserialize<string[][]>(output)!;
        Assert.Equal(["aa 3 a.exe", "bb 7 b.zip"], results[0]);
        Assert.Equal(["a.exe 3 aa", "b.zip 7 bb"], results[1]);
    }

    private const string _mergeSetup = """
        function Asset($name, $size) { [pscustomobject]@{ name = $name; size = $size } }
        $listA = Format-SumList @([pscustomobject]@{ Name = 'a.exe'; Hash = 'aa'; Size = 3 }, [pscustomobject]@{ Name = 'big.bin'; Hash = 'ff'; Size = 15 }, [pscustomobject]@{ Name = 'big.bin.part1'; Hash = 'f1'; Size = 10 }, [pscustomobject]@{ Name = 'big.bin.part2'; Hash = 'f2'; Size = 5 })
        $listB = Format-SumList @([pscustomobject]@{ Name = 'b.zip'; Hash = 'bb'; Size = 7 })
        $assets = @((Asset 'a.exe' 3), (Asset 'big.bin.part1' 10), (Asset 'big.bin.part2' 5), (Asset 'b.zip' 7))
        function Try-Merge($lists, $assets) { try { (Merge-Sums $lists $assets) -join '|' } catch { 'ERROR: ' + $_.Exception.Message } }
        """;

    [Fact]
    public async Task The_lists_of_both_jobs_become_one_checksum_file_when_they_match_the_release()
    {
        var output = await PwshAsync(Guarded(_mergeSetup + "\nTry-Merge @($listA, $listB) $assets"));

        Assert.Equal("aa  a.exe|bb  b.zip|ff  big.bin|f1  big.bin.part1|f2  big.bin.part2", output.Trim());
    }

    [Fact]
    public async Task A_release_asset_with_a_different_size_than_its_list_is_refused()
    {
        var output = await PwshAsync(Guarded(_mergeSetup + "\nTry-Merge @($listA, $listB) @($assets | ForEach-Object { if ($_.name -eq 'b.zip') { Asset 'b.zip' 8 } else { $_ } })"));

        Assert.Equal("ERROR: b.zip is 8 bytes on the release but its list says 7.", output.Trim());
    }

    [Fact]
    public async Task A_release_asset_no_list_mentions_is_refused()
    {
        var output = await PwshAsync(Guarded(_mergeSetup + "\nTry-Merge @($listA) $assets"));

        Assert.Equal("ERROR: b.zip is on the release but in none of the hash lists.", output.Trim());
    }

    [Fact]
    public async Task A_listed_file_the_release_lacks_is_refused()
    {
        var output = await PwshAsync(Guarded(_mergeSetup + "\nTry-Merge @($listA, $listB) @($assets | Where-Object { $_.name -ne 'big.bin.part2' })"));

        Assert.Equal("ERROR: big.bin.part2 is in a hash list but not on the release.", output.Trim());
    }

    [Fact]
    public async Task A_gap_in_the_numbering_of_the_parts_on_the_release_is_refused()
    {
        var output = await PwshAsync(Guarded("""
            function Asset($name, $size) { [pscustomobject]@{ name = $name; size = $size } }
            $list = Format-SumList @([pscustomobject]@{ Name = 'x'; Hash = 'xx'; Size = 3 }, [pscustomobject]@{ Name = 'x.part1'; Hash = 'x1'; Size = 1 }, [pscustomobject]@{ Name = 'x.part3'; Hash = 'x3'; Size = 1 }, [pscustomobject]@{ Name = 'x.part2'; Hash = 'x2'; Size = 1 })
            try { (Merge-Sums @($list) @((Asset 'x.part1' 1), (Asset 'x.part3' 1))) -join '|' } catch { 'ERROR: ' + $_.Exception.Message }
            """));

        Assert.Equal("ERROR: x.part2 is in a hash list but not on the release.", output.Trim());
    }

    [Fact]
    public async Task Parts_whose_list_has_no_line_for_a_missing_middle_part_are_still_refused()
    {
        var output = await PwshAsync(Guarded("""
            function Asset($name, $size) { [pscustomobject]@{ name = $name; size = $size } }
            $list = Format-SumList @([pscustomobject]@{ Name = 'x'; Hash = 'xx'; Size = 3 }, [pscustomobject]@{ Name = 'x.part1'; Hash = 'x1'; Size = 1 }, [pscustomobject]@{ Name = 'x.part3'; Hash = 'x3'; Size = 1 })
            try { (Merge-Sums @($list) @((Asset 'x.part1' 1), (Asset 'x.part3' 1))) -join '|' } catch { 'ERROR: ' + $_.Exception.Message }
            """));

        Assert.Equal("ERROR: The parts of x are not numbered 1 to 2.", output.Trim());
    }

    [Fact]
    public void Checksums_reads_the_job_lists_from_the_workflow_and_downloads_no_package_file()
    {
        var script = Script();
        var workflow = Workflow();

        var checksums = script[script.IndexOf("'Checksums' {", StringComparison.Ordinal)..];
        Assert.DoesNotContain("browser_download_url", checksums, StringComparison.Ordinal);
        Assert.Contains("SUMS_LINUX", Steps(Job(workflow, "checksums")).Single(step => step.Contains("-Action Checksums", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Contains("needs.linux.outputs.sums", Job(workflow, "checksums"), StringComparison.Ordinal);
        Assert.Contains("needs.windows.outputs.sums", Job(workflow, "checksums"), StringComparison.Ordinal);
        foreach (var name in new[] { "linux", "windows" })
        {
            var job = Job(workflow, name);
            Assert.Matches(@"(?m)^    outputs:\r?\n      sums: \$\{\{ steps\.upload\.outputs\.sums \}\}", job);
            Assert.Matches(@"(?ms)id: upload\r?\n.*release-assets\.ps1 -Action Upload", job);
        }
    }

    [Fact]
    public async Task Parts_numbered_with_a_gap_are_refused()
    {
        var output = await PwshAsync("""
            $ok = 'ok'; try { Assert-PartsNumbered 'f' @(1, 2, 3) } catch { $ok = 'threw' }
            $gap = 'no'; try { Assert-PartsNumbered 'f' @(1, 3) } catch { $gap = $_.Exception.Message }
            $late = 'no'; try { Assert-PartsNumbered 'f' @(2, 3) } catch { $late = 'threw' }
            ConvertTo-Json -InputObject @($ok, $gap, $late)
            """);

        var results = JsonSerializer.Deserialize<string[]>(output)!;
        Assert.Equal("ok", results[0]);
        Assert.Equal("The parts of f are not numbered 1 to 2.", results[1]);
        Assert.Equal("threw", results[2]);
    }
}
