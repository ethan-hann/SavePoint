using System.Diagnostics;
using System.Text;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Build;

// Parses every script under build/ without running it, so a quoting slip fails the gate instead of the next run of the script on
// another machine. The checks are also run on made-up scripts in a temp folder, which shows a slip is reported by file and line.
public sealed class BuildScriptSyntaxTests : IDisposable
{
    // A cold PowerShell start on a loaded Windows machine takes tens of seconds; a program that takes longer than this has hung.
    private static readonly TimeSpan _processTimeout = TimeSpan.FromMinutes(2);

    private readonly string _root =Directory.CreateTempSubdirectory("savepoint-buildsyntax-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static string BuildFolder => Path.Combine(TestEnvironment.RepoRoot ?? throw new InvalidOperationException("These tests read the repository's build files."), "build");

    private static string[] ScriptsUnder(string folder, string extension) =>
        [.. Directory.EnumerateFiles(folder, "*" + extension, SearchOption.AllDirectories).Order(StringComparer.Ordinal)];

    private string MakeScript(string relativePath, string text)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }

    // Starts a program with an argument list; null when it is not on this machine.
    private static Process? TryStart(ProcessStartInfo info)
    {
        info.UseShellExecute = false;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        try
        {
            return Process.Start(info);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    // The bash on the path of a Windows machine may belong to another system, so Git for Windows' own is preferred there.
    private static string BashProgram()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var variable in new[] { "ProgramW6432", "ProgramFiles" })
            {
                var programs = Environment.GetEnvironmentVariable(variable);
                var candidate = programs is null ? null : Path.Combine(programs, "Git", "bin", "bash.exe");
                if (candidate is not null && File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return "bash";
    }

    // One line per syntax error, "<path relative to the folder>:<line>: <message>". Null when pwsh is not installed here.
    private async Task<IReadOnlyList<string>?> PowerShellErrorsAsync(string folder)
    {
        var scripts = ScriptsUnder(folder, ".ps1");
        var probe = MakeScript("probe/parse.ps1", """
            param([Parameter(ValueFromRemainingArguments)][string[]]$Files)
            foreach ($file in $Files) {
                $tokens = $null
                $errors = $null
                [void][System.Management.Automation.Language.Parser]::ParseFile($file, [ref]$tokens, [ref]$errors)
                foreach ($e in $errors) {
                    [Console]::Out.Write(('{0}:{1}: {2}' -f $file, $e.Extent.StartLineNumber, $e.Message) + "`n")
                }
            }
            """);
        var info = new ProcessStartInfo("pwsh") { StandardOutputEncoding = Encoding.UTF8 };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", probe })
        {
            info.ArgumentList.Add(argument);
        }

        foreach (var script in scripts)
        {
            info.ArgumentList.Add(script);
        }

        using var process = TryStart(info);
        if (process is null)
        {
            return null;
        }

        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(_processTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"The PowerShell parse check did not finish within {_processTimeout.TotalSeconds:0} s.");
        }

        var text = await output;
        Assert.True(process.ExitCode == 0, $"The PowerShell parse check itself failed ({process.ExitCode}): {await error}\n{text}");
        var prefix = folder + Path.DirectorySeparatorChar;
        return [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(line => line.Replace(prefix, string.Empty, StringComparison.Ordinal))];
    }

    // One line per script bash refuses to parse, "<path relative to the folder>: <what bash said>". Null when bash is not installed here.
    private static async Task<IReadOnlyList<string>?> BashErrorsAsync(string folder, string? program = null, TimeSpan? limit = null)
    {
        program ??= BashProgram();
        var wait = limit ?? _processTimeout;

        // On Windows without Git for Windows, "bash" can be the stub for a missing Linux distribution: it starts, then fails for any
        // input. A bash that cannot parse nothing is not a bash, so the check is skipped rather than blaming every script.
        var probe = await RunBashAsync(program, [], wait);
        if (probe is not { ExitCode: 0 })
        {
            return null;
        }

        var errors = new List<string>();
        foreach (var script in ScriptsUnder(folder, ".sh"))
        {
            var run = await RunBashAsync(program, await File.ReadAllBytesAsync(script, TestContext.Current.CancellationToken), wait);
            if (run is not { ExitCode: 0 })
            {
                errors.Add($"{Path.GetRelativePath(folder, script)}: {run?.Error}");
            }
        }

        return errors;
    }

    private sealed record BashRun(int ExitCode, string Error);

    // Parses a script with "bash -n". The script goes in on standard input, so no path has to be understood by a bash that may be
    // another system's. Null when the program is not on this machine; a run past the limit is stopped and reported as a failure.
    private static async Task<BashRun?> RunBashAsync(string program, byte[] script, TimeSpan limit)
    {
        var info = new ProcessStartInfo(program) { RedirectStandardInput = true };
        info.ArgumentList.Add("-n");
        using var process = TryStart(info);
        if (process is null)
        {
            return null;
        }

        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(limit);
        try
        {
            try
            {
                await process.StandardInput.BaseStream.WriteAsync(script, timeout.Token);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // Bash stops reading at the first syntax error, which closes the pipe; its report is what counts.
            }

            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            return new BashRun(-1, $"bash did not finish within {limit.TotalSeconds:0} s.");
        }

        await output;
        return new BashRun(process.ExitCode, (await error).Trim());
    }

    [Fact]
    public async Task Every_powershell_script_under_build_parses()
    {
        Assert.NotEmpty(ScriptsUnder(BuildFolder, ".ps1"));

        var errors = await PowerShellErrorsAsync(BuildFolder);

        Assert.SkipUnless(errors is not null, "pwsh is not installed here.");
        Assert.True(errors!.Count == 0, "These build scripts do not parse:\n" + string.Join('\n', errors));
    }

    [Fact]
    public async Task Every_bash_script_under_build_parses()
    {
        Assert.NotEmpty(ScriptsUnder(BuildFolder, ".sh"));

        var errors = await BashErrorsAsync(BuildFolder);

        Assert.SkipUnless(errors is not null, "bash is not installed here.");
        Assert.True(errors!.Count == 0, "These build scripts do not parse:\n" + string.Join('\n', errors));
    }

    [Fact]
    public async Task A_powershell_script_with_an_apostrophe_inside_a_single_quoted_string_is_reported_by_file_and_line()
    {
        MakeScript("fine.ps1", "Write-Host 'all well'\n");
        MakeScript("broken.ps1", "Write-Host 'fine'\nWrite-Host 'it's wrong'\n");

        var errors = await PowerShellErrorsAsync(_root);

        Assert.SkipUnless(errors is not null, "pwsh is not installed here.");
        Assert.NotEmpty(errors!);
        Assert.All(errors!, line => Assert.StartsWith("broken.ps1:2: ", line, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_powershell_script_in_a_subfolder_is_checked_too()
    {
        MakeScript(Path.Combine("sub", "inner.ps1"), "Write-Host 'it's wrong'\n");

        var errors = await PowerShellErrorsAsync(_root);

        Assert.SkipUnless(errors is not null, "pwsh is not installed here.");
        Assert.NotEmpty(errors!);
        Assert.StartsWith(Path.Combine("sub", "inner.ps1") + ":1: ", errors![0], StringComparison.Ordinal);
    }

    // A stand-in for "bash" that behaves like Windows' stub for a missing Linux distribution: it starts, then fails for any input.
    private string StubBash(string body)
    {
        if (OperatingSystem.IsWindows())
        {
            return MakeScript(Path.Combine("stub", "bash.cmd"), "@echo off\r\n" + body + "\r\n");
        }

        var path = MakeScript(Path.Combine("stub", "bash.sh"), "#!/bin/sh\n" + body + "\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    [Fact]
    public async Task A_bash_that_cannot_parse_an_empty_script_counts_as_not_installed()
    {
        MakeScript(Path.Combine("scripts", "fine.sh"), "echo 'all well'\n");
        var stub = StubBash("exit 1");

        var errors = await BashErrorsAsync(Path.Combine(_root, "scripts"), stub);

        Assert.Null(errors);
    }

    [Fact]
    public async Task A_bash_that_never_finishes_is_stopped_and_counts_as_not_installed()
    {
        MakeScript(Path.Combine("scripts", "fine.sh"), "echo 'all well'\n");
        var stub = StubBash(OperatingSystem.IsWindows() ? "ping -n 60 127.0.0.1" : "sleep 60");

        var watch = Stopwatch.StartNew();
        var errors = await BashErrorsAsync(Path.Combine(_root, "scripts"), stub, TimeSpan.FromSeconds(1));

        Assert.Null(errors);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"The check waited {watch.Elapsed.TotalSeconds:0} s for a bash that hangs.");
    }

    [Fact]
    public async Task A_bash_script_with_an_unclosed_quote_is_reported_by_file()
    {
        MakeScript("fine.sh", "echo 'all well'\n");
        MakeScript("broken.sh", "echo 'fine'\necho 'unclosed\n");

        var errors = await BashErrorsAsync(_root);

        Assert.SkipUnless(errors is not null, "bash is not installed here.");
        var error = Assert.Single(errors!);
        Assert.StartsWith("broken.sh: ", error, StringComparison.Ordinal);
        Assert.True(error.Length > "broken.sh: ".Length, "the report says what bash found wrong");
    }
}
