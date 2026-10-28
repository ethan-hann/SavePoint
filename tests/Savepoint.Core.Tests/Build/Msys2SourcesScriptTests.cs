using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Build;

// build/msys2-sources.ps1 turns the package list of a MinGit bundle into build/msys2-sources.json. The tests run its functions under pwsh
// with the network replaced by scriptblocks; none of them touches the network or a real user folder.
public class Msys2SourcesScriptTests
{
    private const string _scriptPath = "build/msys2-sources.ps1";

    private static string RepoRoot => TestEnvironment.RepoRoot ?? throw new InvalidOperationException("These tests read the repository's build files.");

    private static string Quote(string text) => "'" + text.Replace("'", "''") + "'";

    private static string LoadScript()
    {
        var path = Path.Combine(RepoRoot, _scriptPath);
        Assert.True(File.Exists(path), $"{_scriptPath} does not exist.");
        return $". {Quote(path)} -Action Load\n$ErrorActionPreference = 'Stop'\n";
    }

    // Runs pwsh on a probe script and returns its standard output.
    private static async Task<string> PwshAsync(string probe)
    {
        var output = await RunPwshAsync(probe, TestContext.Current.CancellationToken);
        Assert.SkipWhen(output is null, "pwsh is not installed here.");
        return output!;
    }

    // Starts pwsh on a probe script; null when pwsh is not installed here.
    private static async Task<string?> RunPwshAsync(string probe, CancellationToken cancellationToken)
    {
        var folder = Directory.CreateTempSubdirectory("savepoint-msys2-probe-");
        try
        {
            var file = Path.Combine(folder.FullName, "probe.ps1");
            await File.WriteAllTextAsync(file, probe, new UTF8Encoding(false), cancellationToken);
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

            if (process is null)
            {
                return null;
            }

            using (process)
            {
                var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var error = process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);
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

    private static string Sha256Of(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public async Task The_package_list_gives_each_name_and_version_once_in_file_order()
    {
        var folder = Directory.CreateTempSubdirectory("savepoint-msys2-list-");
        try
        {
            var list = Path.Combine(folder.FullName, "package-versions.txt");
            await File.WriteAllTextAsync(list, "bash 5.3.015-2\r\nzlib 1.3.2-1\r\n\r\n  grep 1~3.0-7  \r\nbash 5.3.015-2\r\n", TestContext.Current.CancellationToken);

            var output = await PwshAsync(LoadScript()
                + $"Read-PackageVersions -Path {Quote(list)} | ForEach-Object {{ [Console]::Out.Write(\"$($_.Name)|$($_.Version)`n\") }}\n");

            Assert.Equal("bash|5.3.015-2\nzlib|1.3.2-1\ngrep|1~3.0-7\n", output);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_line_without_a_version_is_refused_with_its_text()
    {
        var folder = Directory.CreateTempSubdirectory("savepoint-msys2-list-");
        try
        {
            var list = Path.Combine(folder.FullName, "package-versions.txt");
            await File.WriteAllTextAsync(list, "bash 5.3.015-2\nloneliness\n", TestContext.Current.CancellationToken);

            var output = await PwshAsync(LoadScript()
                + $"try {{ Read-PackageVersions -Path {Quote(list)} | Out-Null; [Console]::Out.Write('no error') }} catch {{ [Console]::Out.Write($_.Exception.Message) }}\n");

            Assert.Contains("loneliness", output, StringComparison.Ordinal);
            Assert.DoesNotContain("no error", output, StringComparison.Ordinal);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private const string _noTags = "-GetText { param($url) $null }";

    private static string CandidateProbe(string name, string version, string getText = _noTags) =>
        $"Get-SourceCandidates -Name {Quote(name)} -Version {Quote(version)} {getText} | ForEach-Object {{ [Console]::Out.Write(\"$($_.Base) $($_.Url)`n\") }}\n";

    [Fact]
    public async Task An_msys_package_is_looked_for_on_the_msys_mirror_as_zst_then_gz()
    {
        var output = await PwshAsync(LoadScript() + CandidateProbe("sed", "4.9-1"));

        Assert.Equal(
            "sed https://repo.msys2.org/msys/sources/sed-4.9-1.src.tar.zst\n"
            + "sed https://repo.msys2.org/msys/sources/sed-4.9-1.src.tar.gz\n",
            output);
    }

    [Fact]
    public async Task A_mingw_package_is_looked_for_on_the_mingw_mirror_under_its_name_without_the_environment_prefix()
    {
        var output = await PwshAsync(LoadScript() + CandidateProbe("mingw-w64-ucrt-x86_64-zlib", "1.3.2-2"));

        Assert.Equal(
            "zlib https://repo.msys2.org/mingw/sources/mingw-w64-zlib-1.3.2-2.src.tar.zst\n"
            + "zlib https://repo.msys2.org/mingw/sources/mingw-w64-zlib-1.3.2-2.src.tar.gz\n",
            output);
    }

    // The rows of the theories below. Their probes run in one pwsh start for the whole class (see TheoryRowsAsync), because a pwsh start
    // costs far more than the lookup it runs.
    private static readonly (string Name, string Source, string Mirror)[] _builtFromRows =
    [
        ("gcc-libs", "gcc", "msys"),
        ("heimdal-libs", "heimdal", "msys"),
        ("libsqlite", "sqlite", "msys"),
        ("libreadline", "readline", "msys"),
        ("libpcre", "pcre", "msys"),
        ("libintl", "gettext", "msys"),
        ("libopenssl", "openssl", "msys"),
        ("mingw-w64-ucrt-x86_64-libatomic", "gcc", "mingw"),
        ("mingw-w64-ucrt-x86_64-cc-libs", "gcc", "mingw"),
        ("mingw-w64-ucrt-x86_64-libstdc++", "gcc", "mingw"),
        ("mingw-w64-ucrt-x86_64-libwinpthread", "winpthreads", "mingw"),
        ("mingw-w64-ucrt-x86_64-gitk", "git", "mingw"),
    ];

    private static readonly (string Name, string Source)[] _suffixedRows =
    [
        ("mingw-w64-ucrt-x86_64-curl-winssl", "curl"),
        ("mingw-w64-ucrt-x86_64-libssh2-wincng", "libssh2"),
        ("mingw-w64-ucrt-x86_64-gettext-runtime", "gettext"),
        ("mingw-w64-ucrt-x86_64-git-gui", "git"),
        ("mingw-w64-ucrt-x86_64-git-for-windows-addons", "git"),
    ];

    private static readonly (string Status, string Reason)[] _refusedRows =
    [
        ("[GNUPG:] BADSIG FA11531AA0AA7F57 Someone", "bad signature"),
        ("[GNUPG:] ERRSIG FA11531AA0AA7F57 1 10 00 1668872992 9 5F944B027F7FE2091985AA2EFA11531AA0AA7F57", "could not be checked"),
        ("[GNUPG:] REVKEYSIG FA11531AA0AA7F57 Someone", "revoked"),
        ("[GNUPG:] NEWSIG", "no valid signature"),
    ];

    public static TheoryData<string, string, string> BuiltFromRows()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var (name, source, mirror) in _builtFromRows)
        {
            data.Add(name, source, mirror);
        }

        return data;
    }

    public static TheoryData<string, string> SuffixedRows()
    {
        var data = new TheoryData<string, string>();
        foreach (var (name, source) in _suffixedRows)
        {
            data.Add(name, source);
        }

        return data;
    }

    public static TheoryData<string, string> RefusedRows()
    {
        var data = new TheoryData<string, string>();
        foreach (var (status, reason) in _refusedRows)
        {
            data.Add(status, reason);
        }

        return data;
    }

    // One probe per theory row, keyed by the row's kind and its first value.
    private static IEnumerable<(string Key, string Code)> TheoryRowProbes() =>
        _builtFromRows.Select(row => ("built:" + row.Name, CandidateProbe(row.Name, "1.0-1")))
            .Concat(_suffixedRows.Select(row => ("suffixed:" + row.Name, CandidateProbe(row.Name, "1.0-1"))))
            .Concat(_refusedRows.Select(row => ("refused:" + row.Status, StatusProbe(row.Status))));

    private const string _rowMarker = "@@row@@";

    // The probes of every theory row in one pwsh start. A row that throws leaves its error as its output, so its own assertions fail and the
    // other rows are not lost. Null when pwsh is not installed here.
    private static readonly Lazy<Task<Dictionary<string, string>?>> _theoryRows = new(() => Task.Run(async () =>
    {
        var probes = TheoryRowProbes().ToList();
        var script = new StringBuilder(LoadScript());
        foreach (var (key, code) in probes)
        {
            script.Append(CultureInfo.InvariantCulture, $"[Console]::Out.Write({Quote(_rowMarker + key + _rowMarker)})\ntry {{\n{code}}} catch {{ [Console]::Out.Write(\"ERROR: $_\") }}\n");
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var output = await RunPwshAsync(script.ToString(), timeout.Token);
        if (output is null)
        {
            return null;
        }

        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        var parts = output.Split(_rowMarker);
        for (var i = 1; i + 1 < parts.Length; i += 2)
        {
            rows[parts[i]] = parts[i + 1];
        }

        return rows;
    }));

    private static async Task<string> TheoryRowAsync(string key)
    {
        var rows = await _theoryRows.Value;
        Assert.SkipWhen(rows is null, "pwsh is not installed here.");
        Assert.True(rows!.TryGetValue(key, out var output), $"The shared pwsh run has no output for {key}.");
        return output!;
    }

    [Theory]
    [MemberData(nameof(BuiltFromRows))]
    public async Task A_package_built_from_another_package_is_looked_for_under_the_one_it_is_built_from(string name, string source, string mirror)
    {
        var output = await TheoryRowAsync("built:" + name);

        var prefix = mirror == "mingw" ? "mingw-w64-" : "";
        Assert.StartsWith($"{source} https://repo.msys2.org/{mirror}/sources/{prefix}{source}-1.0-1.src.tar.zst\n", output, StringComparison.Ordinal);
        Assert.DoesNotContain(" https://repo.msys2.org/" + mirror + "/sources/" + prefix + name, output, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(SuffixedRows))]
    public async Task A_package_named_after_another_with_a_suffix_falls_back_to_the_shorter_name(string name, string source)
    {
        var output = await TheoryRowAsync("suffixed:" + name);

        Assert.Contains($"{source} https://repo.msys2.org/mingw/sources/mingw-w64-{source}-1.0-1.src.tar.zst\n", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_package_in_the_git_for_windows_pacman_repo_is_tried_there_first_by_its_release_tag()
    {
        const string tags = "-GetText { param($url) if ($url -eq 'https://raw.githubusercontent.com/git-for-windows/pacman-repo/refs/heads/x86_64/mingw-w64-ucrt-x86_64-curl.versions.json') "
            + "{ '{ \"8.22.0-1\": \"2026-09-04T10-05-59.011681900Z\", \"8.22.0-2\": \"2026-10-05T15-43-47.005265900Z\" }' } else { $null } }";

        var output = await PwshAsync(LoadScript() + CandidateProbe("mingw-w64-ucrt-x86_64-curl-winssl", "8.22.0-2", tags));

        const string release = "https://github.com/git-for-windows/pacman-repo/releases/download/2026-10-05T15-43-47.005265900Z/mingw-w64-curl-8.22.0-2.src.tar.gz";
        Assert.Equal(
            "curl-winssl https://repo.msys2.org/mingw/sources/mingw-w64-curl-winssl-8.22.0-2.src.tar.zst\n"
            + "curl-winssl https://repo.msys2.org/mingw/sources/mingw-w64-curl-winssl-8.22.0-2.src.tar.gz\n"
            + $"curl {release}\n"
            + "curl https://repo.msys2.org/mingw/sources/mingw-w64-curl-8.22.0-2.src.tar.zst\n"
            + "curl https://repo.msys2.org/mingw/sources/mingw-w64-curl-8.22.0-2.src.tar.gz\n",
            output);
    }

    [Fact]
    public async Task A_release_tag_only_counts_for_the_exact_version()
    {
        const string tags = "-GetText { param($url) if ($url -like '*/x86_64/bash.versions.json') { '{ \"5.3.015-1\": \"2026-06-04T13-03-48.731874200Z\" }' } else { $null } }";

        var output = await PwshAsync(LoadScript() + CandidateProbe("bash", "5.3.015-2", tags));

        Assert.Equal(
            "bash https://repo.msys2.org/msys/sources/bash-5.3.015-2.src.tar.zst\n"
            + "bash https://repo.msys2.org/msys/sources/bash-5.3.015-2.src.tar.gz\n",
            output);
    }

    [Fact]
    public async Task The_pacman_repo_is_asked_for_each_package_once()
    {
        // Two packages of one source ask for the same version list; the second ask is answered from memory.
        var output = await PwshAsync(LoadScript()
            + "$script:asked = 0\n"
            + "$cache = @{}\n"
            + "$ask = { param($url) $script:asked++; $null }\n"
            + "Get-SourceCandidates -Name 'mingw-w64-ucrt-x86_64-libatomic' -Version '16.2.0-4' -GetText $ask -Cache $cache | Out-Null\n"
            + "Get-SourceCandidates -Name 'mingw-w64-ucrt-x86_64-libgomp' -Version '16.2.0-4' -GetText $ask -Cache $cache | Out-Null\n"
            + "[Console]::Out.Write($script:asked)\n");

        Assert.Equal("1", output);
    }

    // A fake network: -Serve maps a url to the text that "downloads" there; every other url is not found. Calls are recorded.
    private const string _fakeNetwork =
        "$script:calls = [System.Collections.Generic.List[string]]::new()\n"
        + "$download = { param($url, $outFile) $script:calls.Add($url); if ($served.ContainsKey($url)) { [System.IO.File]::WriteAllText($outFile, $served[$url]); $true } else { $false } }\n"
        + "$getText = { param($url) $null }\n";

    [Fact]
    public async Task Packages_of_one_source_share_one_archive_found_after_the_first_candidate_fails()
    {
        var folder = Directory.CreateTempSubdirectory("savepoint-msys2-find-");
        try
        {
            var cache = Path.Combine(folder.FullName, "cache");
            var output = await PwshAsync(LoadScript() + _fakeNetwork
                + "$served = @{ 'https://repo.msys2.org/mingw/sources/mingw-w64-gcc-16.2.0-4.src.tar.gz' = 'gcc source' }\n"
                + "$packages = @(\n"
                + "  [pscustomobject]@{ Name = 'mingw-w64-ucrt-x86_64-libgomp'; Version = '16.2.0-4' },\n"
                + "  [pscustomobject]@{ Name = 'mingw-w64-ucrt-x86_64-gcc-libs'; Version = '16.2.0-4' })\n"
                + $"$found = Find-PackageSources -Packages $packages -Download $download -GetText $getText -CacheDirectory {Quote(cache)}\n"
                + "$a = $found.Archives\n"
                + "[Console]::Out.Write(\"archives=$(@($a).Count) missing=$(@($found.Missing).Count)`n\")\n"
                + "[Console]::Out.Write(\"$($a[0].File)|$($a[0].Url)|$($a[0].Sha256)|$($a[0].Size)|$($a[0].Provides -join ', ')`n\")\n"
                + "[Console]::Out.Write(($script:calls -join \"`n\") + \"`n\")\n");

            var lines = output.Split('\n');
            Assert.Equal("archives=1 missing=0", lines[0]);
            Assert.Equal(
                "mingw-w64-gcc-16.2.0-4.src.tar.gz|https://repo.msys2.org/mingw/sources/mingw-w64-gcc-16.2.0-4.src.tar.gz|" + Sha256Of("gcc source")
                + "|10|mingw-w64-ucrt-x86_64-gcc-libs 16.2.0-4, mingw-w64-ucrt-x86_64-libgomp 16.2.0-4",
                lines[1]);
            // The zst candidate failed once, the gz one was downloaded once, and the second package reused it.
            Assert.Equal(
                [
                    "https://repo.msys2.org/mingw/sources/mingw-w64-gcc-16.2.0-4.src.tar.zst",
                    "https://repo.msys2.org/mingw/sources/mingw-w64-gcc-16.2.0-4.src.tar.gz",
                ],
                lines[2..4]);
            Assert.Equal("gcc source", await File.ReadAllTextAsync(Path.Combine(cache, "mingw-w64-gcc-16.2.0-4.src.tar.gz"), TestContext.Current.CancellationToken));
            Assert.Empty(Directory.GetFiles(cache, "*.part"));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task An_archive_already_in_the_cache_is_hashed_without_downloading_it_again()
    {
        var folder = Directory.CreateTempSubdirectory("savepoint-msys2-cache-");
        try
        {
            var cache = Directory.CreateDirectory(Path.Combine(folder.FullName, "cache")).FullName;
            await File.WriteAllTextAsync(Path.Combine(cache, "sed-4.9-1.src.tar.zst"), "cached sed", TestContext.Current.CancellationToken);

            var output = await PwshAsync(LoadScript() + _fakeNetwork
                + "$served = @{}\n"
                + "$packages = @([pscustomobject]@{ Name = 'sed'; Version = '4.9-1' })\n"
                + $"$found = Find-PackageSources -Packages $packages -Download $download -GetText $getText -CacheDirectory {Quote(cache)}\n"
                + "[Console]::Out.Write(\"$($found.Archives[0].Sha256) $($found.Archives[0].Size) downloads=$($script:calls.Count)\")\n");

            Assert.Equal($"{Sha256Of("cached sed")} 10 downloads=0", output);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Archives_are_listed_by_file_name_and_the_json_has_the_fields_the_package_scripts_read()
    {
        var output = await PwshAsync(LoadScript()
            + "$archives = @(\n"
            + "  [pscustomobject]@{ File = 'zlib-1.3.2-1.src.tar.zst'; Url = 'https://repo.msys2.org/msys/sources/zlib-1.3.2-1.src.tar.zst'; Sha256 = 'bb'; Size = 22; Provides = @('zlib 1.3.2-1') },\n"
            + "  [pscustomobject]@{ File = 'bash-5.3.015-2.src.tar.gz'; Url = 'https://example.test/bash-5.3.015-2.src.tar.gz'; Sha256 = 'aa'; Size = 11; Provides = @('bash 5.3.015-2', 'bash-x 5.3.015-2') })\n"
            + "[Console]::Out.Write((ConvertTo-SourceListJson -Archives $archives -BundleVersion '2.56.0.2' -Date '2026-10-25'))\n");

        const string expected = """
            {
              "bundle": "win-x64",
              "bundleVersion": "2.56.0.2",
              "listedIn": "etc/package-versions.txt inside MinGit-2.56.0.2-64-bit.zip",
              "hashSource": "downloaded once on 2026-10-25 and hashed; the msys2 and mingw entries come from repo.msys2.org, the rest from Git for Windows' pacman-repo releases, where build-extra/get-sources.sh looks first",
              "archives": [
                {
                  "file": "bash-5.3.015-2.src.tar.gz",
                  "url": "https://example.test/bash-5.3.015-2.src.tar.gz",
                  "sha256": "aa",
                  "size": 11,
                  "provides": "bash 5.3.015-2, bash-x 5.3.015-2"
                },
                {
                  "file": "zlib-1.3.2-1.src.tar.zst",
                  "url": "https://repo.msys2.org/msys/sources/zlib-1.3.2-1.src.tar.zst",
                  "sha256": "bb",
                  "size": 22,
                  "provides": "zlib 1.3.2-1"
                }
              ]
            }

            """;
        Assert.Equal(expected.Replace("\r\n", "\n"), output);
    }

    private const string _primary = "5F944B027F7FE2091985AA2EFA11531AA0AA7F57";
    private const string _subkey = "91883E11E83DC29D14104DB4EDD44359093056EE";

    private static string StatusProbe(string status, string expected = _primary) =>
        $"$status = @('{string.Join("','", status.Split('\n'))}')\n"
        + $"$verdict = Get-SignatureVerdict -Status $status -Fingerprint '{expected}'\n"
        + "[Console]::Out.Write(\"[$verdict]\")\n";

    [Fact]
    public async Task A_good_signature_by_the_pinned_key_passes()
    {
        var output = await PwshAsync(LoadScript() + StatusProbe(
            $"[GNUPG:] NEWSIG\n[GNUPG:] GOODSIG FA11531AA0AA7F57 Someone\n[GNUPG:] VALIDSIG {_primary} 2022-11-19 1668872992 0 4 0 1 10 00 {_primary}"));

        Assert.Equal("[]", output);
    }

    [Fact]
    public async Task A_signature_made_by_a_subkey_counts_for_the_primary_key_it_belongs_to()
    {
        var output = await PwshAsync(LoadScript() + StatusProbe(
            $"[GNUPG:] GOODSIG EDD44359093056EE Someone\n[GNUPG:] VALIDSIG {_subkey} 2026-09-30 1790758609 0 4 0 1 8 00 {_primary}"));

        Assert.Equal("[]", output);
    }

    [Fact]
    public async Task A_good_signature_by_another_key_is_refused_and_names_both_keys()
    {
        const string other = "69985C5EB351011C78DF7F6D755B8182ACD22879";

        var output = await PwshAsync(LoadScript() + StatusProbe(
            $"[GNUPG:] GOODSIG 755B8182ACD22879 Someone\n[GNUPG:] VALIDSIG {other} 2022-11-19 1668872992 0 4 0 1 10 00 {other}"));

        Assert.Contains(other, output, StringComparison.Ordinal);
        Assert.Contains(_primary, output, StringComparison.Ordinal);
        Assert.NotEqual("[]", output);
    }

    [Theory]
    [MemberData(nameof(RefusedRows))]
    public async Task Anything_but_a_valid_signature_is_refused_with_a_reason(string status, string reason)
    {
        var output = await TheoryRowAsync("refused:" + status);

        Assert.Contains(reason, output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_bad_signature_is_not_rescued_by_a_valid_one_beside_it()
    {
        var output = await PwshAsync(LoadScript() + StatusProbe(
            $"[GNUPG:] BADSIG FA11531AA0AA7F57 Someone\n[GNUPG:] VALIDSIG {_primary} 2022-11-19 1668872992 0 4 0 1 10 00 {_primary}"));

        Assert.Contains("bad signature", output, StringComparison.Ordinal);
    }

    // Find-PackageSources with a verifier: the .sig beside each archive is fetched from the same address plus ".sig".
    private const string _fakeSignedNetwork =
        _fakeNetwork
        + "$served = @{ 'https://repo.msys2.org/msys/sources/sed-4.9-1.src.tar.zst' = 'sed source'; 'https://repo.msys2.org/msys/sources/sed-4.9-1.src.tar.zst.sig' = 'sed signature' }\n"
        + "$packages = @([pscustomobject]@{ Name = 'sed'; Version = '4.9-1' })\n";

    [Fact]
    public async Task With_a_verifier_each_archive_is_checked_against_the_signature_fetched_beside_it()
    {
        var folder = Directory.CreateTempSubdirectory("savepoint-msys2-sig-");
        try
        {
            var cache = Path.Combine(folder.FullName, "cache");
            var output = await PwshAsync(LoadScript() + _fakeSignedNetwork
                + "$verify = { param($Archive, $Signature, $Url) [Console]::Out.Write(\"verify $([System.IO.File]::ReadAllText($Archive))|$([System.IO.File]::ReadAllText($Signature))|$Url`n\"); $null }\n"
                + $"$found = Find-PackageSources -Packages $packages -Download $download -GetText $getText -CacheDirectory {Quote(cache)} -VerifySignature $verify\n"
                + "[Console]::Out.Write(\"archives=$(@($found.Archives).Count)`n\")\n");

            Assert.Equal(
                "verify sed source|sed signature|https://repo.msys2.org/msys/sources/sed-4.9-1.src.tar.zst\narchives=1\n",
                output);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task An_archive_with_a_bad_signature_stops_the_run_and_is_not_kept()
    {
        var folder = Directory.CreateTempSubdirectory("savepoint-msys2-sig-");
        try
        {
            var cache = Path.Combine(folder.FullName, "cache");
            var output = await PwshAsync(LoadScript() + _fakeSignedNetwork
                + "$verify = { param($Archive, $Signature, $Url) 'bad signature from nobody' }\n"
                + "try { Find-PackageSources -Packages $packages -Download $download -GetText $getText "
                + $"-CacheDirectory {Quote(cache)} -VerifySignature $verify | Out-Null; [Console]::Out.Write('no error') }} "
                + "catch { [Console]::Out.Write($_.Exception.Message) }\n");

            Assert.Contains("sed-4.9-1.src.tar.zst", output, StringComparison.Ordinal);
            Assert.Contains("bad signature from nobody", output, StringComparison.Ordinal);
            Assert.DoesNotContain("no error", output, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(cache));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task An_archive_without_a_signature_stops_the_run()
    {
        var folder = Directory.CreateTempSubdirectory("savepoint-msys2-sig-");
        try
        {
            var cache = Path.Combine(folder.FullName, "cache");
            var output = await PwshAsync(LoadScript() + _fakeNetwork
                + "$served = @{ 'https://repo.msys2.org/msys/sources/sed-4.9-1.src.tar.zst' = 'sed source' }\n"
                + "$packages = @([pscustomobject]@{ Name = 'sed'; Version = '4.9-1' })\n"
                + "$verify = { param($Archive, $Signature, $Url) $null }\n"
                + "try { Find-PackageSources -Packages $packages -Download $download -GetText $getText "
                + $"-CacheDirectory {Quote(cache)} -VerifySignature $verify | Out-Null; [Console]::Out.Write('no error') }} "
                + "catch { [Console]::Out.Write($_.Exception.Message) }\n");

            Assert.Contains("no signature", output, StringComparison.Ordinal);
            Assert.Contains("https://repo.msys2.org/msys/sources/sed-4.9-1.src.tar.zst.sig", output, StringComparison.Ordinal);
            Assert.DoesNotContain("no error", output, StringComparison.Ordinal);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task The_pinned_keys_are_the_ones_the_signers_name_and_each_host_has_its_own()
    {
        var output = await PwshAsync(LoadScript()
            + "foreach ($signer in $signers) {\n"
            + "  $keys = (Get-Content -LiteralPath (Join-Path $keyDirectory $signer.KeyFile) -Raw)\n"
            + "  [Console]::Out.Write(\"$($signer.Fingerprint)|$($signer.UrlPrefixes -join ',')|$($keys.StartsWith('-----BEGIN PGP PUBLIC KEY BLOCK-----'))`n\")\n"
            + "}\n");

        Assert.Equal(
            "5F944B027F7FE2091985AA2EFA11531AA0AA7F57|https://repo.msys2.org/|True\n"
            + "E8325679DFFF09668AD8D7B67115A57376871B1C|https://github.com/git-for-windows/pacman-repo/releases/download/|True\n",
            output);
    }

    // The gpg that ships with Git for Windows is an msys program: it refuses a Windows-style home folder (its agent socket name cannot hold a
    // colon) and takes /c/Users/... instead. Gpg4win and Linux gpg take the folder as it is.
    [Fact]
    public async Task A_gpg_home_is_given_to_the_gpg_of_git_for_windows_in_its_own_form_and_to_any_other_as_it_is()
    {
        var output = await PwshAsync(LoadScript()
            + "$home1 = 'C:\\Users\\x y\\Temp\\abc'\n"
            + "foreach ($gpg in @('C:\\Program Files\\Git\\usr\\bin\\gpg.exe', 'D:\\Git\\usr\\bin\\gpg.exe', 'C:\\Program Files (x86)\\GnuPG\\bin\\gpg.exe', '/usr/bin/gpg')) {\n"
            + "  [Console]::Out.Write(\"$(ConvertTo-GpgHome -Path $home1 -GpgPath $gpg)`n\")\n"
            + "}\n"
            + "[Console]::Out.Write(\"$(ConvertTo-GpgHome -Path '/tmp/abc' -GpgPath 'C:\\Program Files\\Git\\usr\\bin\\gpg.exe')`n\")\n");

        Assert.Equal(
            "/c/Users/x y/Temp/abc\n/c/Users/x y/Temp/abc\nC:\\Users\\x y\\Temp\\abc\nC:\\Users\\x y\\Temp\\abc\n/tmp/abc\n",
            output);
    }

    [Fact]
    public async Task The_real_gpg_accepts_a_good_signature_refuses_a_changed_file_and_refuses_an_unpinned_host()
    {
        var folder = Directory.CreateTempSubdirectory("savepoint-msys2-gpg-");
        try
        {
            var output = await PwshAsync(LoadScript()
                + $"$work = {Quote(folder.FullName)}\n"
                + "if (-not (Get-Command gpg -ErrorAction SilentlyContinue)) { [Console]::Out.Write('no gpg'); return }\n"
                + "$signingHome = New-GpgHome\n"
                + "$homeArgument = ConvertTo-GpgHome -Path $signingHome\n"
                + "try {\n"
                + "  & gpg --homedir $homeArgument --batch --quiet --passphrase '' --quick-generate-key 'Test Signer <signer@example.test>' rsa2048 sign never 2>$null\n"
                + "  $fingerprint = (& gpg --homedir $homeArgument --batch --with-colons --list-keys | Where-Object { $_ -like 'fpr:*' } | Select-Object -First 1).Split(':')[9]\n"
                + "  $keys = Join-Path $work 'keys'\n"
                + "  New-Item -ItemType Directory -Force -Path $keys | Out-Null\n"
                + "  & gpg --homedir $homeArgument --batch --armor --output (Join-Path $keys 'test.asc') --export $fingerprint\n"
                + "  $archive = Join-Path $work 'archive.tar.gz'\n"
                + "  [System.IO.File]::WriteAllText($archive, 'original')\n"
                + "  & gpg --homedir $homeArgument --batch --detach-sign --output \"$archive.sig\" $archive 2>$null\n"
                + "  $signer = [pscustomobject]@{ Name = 'test'; Fingerprint = $fingerprint; KeyFile = 'test.asc'; UrlPrefixes = @('https://example.test/') }\n"
                + "  $verifier = New-SignatureVerifier -KeyDirectory $keys -Signers @($signer)\n"
                + "  try {\n"
                + "    $good = $verifier.Verify($archive, \"$archive.sig\", 'https://example.test/archive.tar.gz')\n"
                + "    $elsewhere = $verifier.Verify($archive, \"$archive.sig\", 'https://elsewhere.test/archive.tar.gz')\n"
                + "    [System.IO.File]::WriteAllText($archive, 'changed')\n"
                + "    $changed = $verifier.Verify($archive, \"$archive.sig\", 'https://example.test/archive.tar.gz')\n"
                + "  }\n"
                + "  finally { $verifier.Dispose() }\n"
                + "  [Console]::Out.Write(\"good=[$good]`nelsewhere=[$elsewhere]`nchanged=[$changed]`n\")\n"
                + "}\n"
                + "finally { & gpgconf --homedir $homeArgument --kill gpg-agent 2>$null; Remove-Item -LiteralPath $signingHome -Recurse -Force -ErrorAction SilentlyContinue }\n");

            Assert.SkipWhen(output == "no gpg", "gpg is not installed here.");
            var lines = output.Split('\n');
            Assert.Equal("good=[]", lines[0]);
            Assert.Contains("no pinned key", lines[1], StringComparison.Ordinal);
            Assert.Contains("bad signature", lines[2], StringComparison.Ordinal);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private static async Task<(string Folder, string Bundle)> BundleWithAsync(string listing)
    {
        var folder = Directory.CreateTempSubdirectory("savepoint-msys2-bundle-");
        var bundle = Directory.CreateDirectory(Path.Combine(folder.FullName, "bundle", "etc")).Parent!.FullName;
        await File.WriteAllTextAsync(Path.Combine(bundle, "etc", "package-versions.txt"), listing, TestContext.Current.CancellationToken);
        return (folder.FullName, bundle);
    }

    [Fact]
    public async Task A_complete_run_writes_the_list_with_unix_line_endings_and_no_byte_order_mark()
    {
        var (folder, bundle) = await BundleWithAsync("sed 4.9-1\nsed 4.9-1\n");
        try
        {
            var outFile = Path.Combine(folder, "out", "msys2-sources.json");
            await PwshAsync(LoadScript() + _fakeNetwork
                + "$served = @{ 'https://repo.msys2.org/msys/sources/sed-4.9-1.src.tar.zst' = 'sed source' }\n"
                + $"Invoke-SourceListGeneration -BundleDirectory {Quote(bundle)} -OutFile {Quote(outFile)} -CacheDirectory {Quote(Path.Combine(folder, "cache"))} "
                + "-BundleVersion '2.56.0.2' -Date '2026-10-25' -Download $download -GetText $getText | Out-Null\n");

            var bytes = await File.ReadAllBytesAsync(outFile, TestContext.Current.CancellationToken);
            Assert.NotEqual((byte)0xEF, bytes[0]);
            var text = Encoding.UTF8.GetString(bytes);
            Assert.DoesNotContain('\r', text);
            Assert.EndsWith("  ]\n}\n", text, StringComparison.Ordinal);
            Assert.Contains("\"sha256\": \"" + Sha256Of("sed source") + "\"", text, StringComparison.Ordinal);
            Assert.Contains("\"provides\": \"sed 4.9-1\"", text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Packages_without_a_source_are_reported_with_the_addresses_tried_and_nothing_is_written()
    {
        var (folder, bundle) = await BundleWithAsync("sed 4.9-1\nnosuch 1.0-1\n");
        try
        {
            var outFile = Path.Combine(folder, "out", "msys2-sources.json");
            var output = await PwshAsync(LoadScript() + _fakeNetwork
                + "$served = @{ 'https://repo.msys2.org/msys/sources/sed-4.9-1.src.tar.zst' = 'sed source' }\n"
                + $"try {{ Invoke-SourceListGeneration -BundleDirectory {Quote(bundle)} -OutFile {Quote(outFile)} -CacheDirectory {Quote(Path.Combine(folder, "cache"))} "
                + "-BundleVersion '2.56.0.2' -Date '2026-10-25' -Download $download -GetText $getText | Out-Null; [Console]::Out.Write('no error') } "
                + "catch { [Console]::Out.Write($_.Exception.Message) }\n");

            Assert.DoesNotContain("no error", output, StringComparison.Ordinal);
            Assert.Contains("nosuch 1.0-1", output, StringComparison.Ordinal);
            Assert.Contains("https://repo.msys2.org/msys/sources/nosuch-1.0-1.src.tar.zst", output, StringComparison.Ordinal);
            Assert.DoesNotContain("sed 4.9-1", output, StringComparison.Ordinal);
            Assert.False(File.Exists(outFile), "A list with a hole in it must not be written.");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task A_folder_without_a_package_list_is_not_a_bundle()
    {
        var folder = Directory.CreateTempSubdirectory("savepoint-msys2-empty-");
        try
        {
            var output = await PwshAsync(LoadScript()
                + $"try {{ Invoke-SourceListGeneration -BundleDirectory {Quote(folder.FullName)} -OutFile {Quote(Path.Combine(folder.FullName, "out.json"))} "
                + $"-CacheDirectory {Quote(Path.Combine(folder.FullName, "cache"))} -BundleVersion '1.0' -Date '2026-10-25' | Out-Null; [Console]::Out.Write('no error') }} "
                + "catch { [Console]::Out.Write($_.Exception.Message) }\n");

            Assert.Contains("has no etc/package-versions.txt", output, StringComparison.Ordinal);
            Assert.DoesNotContain("no error", output, StringComparison.Ordinal);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
