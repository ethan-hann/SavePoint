using System.ComponentModel;
using System.Diagnostics;

namespace Savepoint.App.Platform.Processes;

/// <summary>Starts real processes through the .NET process API, with an argument list and never a shell.</summary>
internal sealed class SystemProcessRunner : IProcessRunner
{
    /// <inheritdoc />
    public bool TryStart(ProcessCommand command)
    {
        try
        {
            using var process = Process.Start(CreateStartInfo(command, captureOutput: false));
            return process is not null;
        }
        catch (Exception ex) when (IsStartFailure(ex))
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> TryStartAndConfirmAsync(ProcessCommand command, TimeSpan grace, CancellationToken cancellationToken = default)
    {
        Process? process;
        try
        {
            process = Process.Start(CreateStartInfo(command, captureOutput: false));
        }
        catch (Exception ex) when (IsStartFailure(ex))
        {
            return false;
        }

        if (process is null)
        {
            return false;
        }

        // Disposing the handle leaves the program running, which is what a file manager that took longer than the grace needs.
        using (process)
        using (var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            limit.CancelAfter(grace);
            try
            {
                await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return true;
            }

            return process.ExitCode == 0;
        }
    }

    /// <inheritdoc />
    public async Task<ProcessOutput?> RunAsync(ProcessCommand command, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Process? process;
        try
        {
            process = Process.Start(CreateStartInfo(command, captureOutput: true));
        }
        catch (Exception ex) when (IsStartFailure(ex))
        {
            return null;
        }

        if (process is null)
        {
            return null;
        }

        using (process)
        using (var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            limit.CancelAfter(timeout);
            var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            // Read and drop stderr so a chatty program cannot fill its pipe and stall.
            var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
            try
            {
                await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Kill(process);
                return null;
            }

            await Task.WhenAll(output, errors).ConfigureAwait(false);
            return new ProcessOutput(process.ExitCode, output.Result);
        }
    }

    private static ProcessStartInfo CreateStartInfo(ProcessCommand command, bool captureOutput)
    {
        var info = new ProcessStartInfo(command.FileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = captureOutput,
            RedirectStandardError = captureOutput,
        };
        foreach (var argument in command.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

    private static bool IsStartFailure(Exception ex) =>
        ex is Win32Exception or InvalidOperationException or FileNotFoundException or PlatformNotSupportedException;

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Already gone.
        }
    }
}
