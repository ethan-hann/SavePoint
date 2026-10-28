using System.Diagnostics;
using System.Text;

namespace Savepoint.Core.Tests.Build;

/// <summary>Runs a build script under pwsh or bash for the tests of build/, with a time limit and the program found the way the other build tests find it.</summary>
internal static class ScriptProcess
{
    // A cold PowerShell start on a loaded Windows machine takes tens of seconds, and a gate run starts it many times.
    private static readonly TimeSpan _limit = TimeSpan.FromMinutes(5);

    internal sealed record Result(int ExitCode, string Output);

    // The bash on the path of a Windows machine may belong to another system, so Git for Windows' own is preferred there.
    internal static string BashProgram()
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

    // Git for Windows' bash reads forward slashes in every path it is given.
    internal static string ForBash(string path) => path.Replace('\\', '/');

    /// <summary>Starts a program with an argument list and returns what it printed; null when the program is not installed here.</summary>
    internal static async Task<Result?> RunAsync(string program, IEnumerable<string> arguments, string workingDirectory, IReadOnlyDictionary<string, string>? environment = null)
    {
        var info = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            WorkingDirectory = workingDirectory,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
        {
            info.Environment[name] = value;
        }

        Process? process;
        try
        {
            process = Process.Start(info);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }

        using (process)
        {
            // Nothing is typed to a script; closing the input keeps one that reads it from waiting.
            process!.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(_limit);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail($"{program} did not finish within {_limit.TotalMinutes:0} minutes.");
            }

            return new Result(process.ExitCode, await output + await error);
        }
    }
}
