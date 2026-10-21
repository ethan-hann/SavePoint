using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

/// <summary>
/// Starts the bundled git for one <see cref="GitInvocation"/> and returns what it printed. It never goes through a shell,
/// reads output in one UTF-8 stream per pipe (so a name is never split across reads), and on cancellation kills git and
/// every process it started. It is safe to call from many threads at once.
/// </summary>
public sealed class GitRunner
{
    // The end of stderr is what explains a failure; a long progress meter before it is not worth keeping.
    private const int _standardErrorLimit = 1024 * 1024;

    private static readonly UTF8Encoding _utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly GitBinary _binary;
    private readonly GitRunnerOptions _options;

    /// <summary>Builds a runner for a located git bundle.</summary>
    /// <param name="binary">The bundle to run, with the environment every call starts from.</param>
    /// <param name="options">Tuning; null uses the defaults.</param>
    /// <exception cref="ArgumentOutOfRangeException">The read buffer size is not positive.</exception>
    public GitRunner(GitBinary binary, GitRunnerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(binary);
        _binary = binary;
        _options = options ?? new GitRunnerOptions();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_options.ReadBufferSize);
    }

    /// <summary>The bundle this runner starts.</summary>
    public GitBinary Binary => _binary;

    /// <summary>The tuning this runner was built with.</summary>
    public GitRunnerOptions Options => _options;

    /// <summary>
    /// Runs one git command and waits for it. The process gets the bundle's base environment (no inherited <c>GIT_*</c>
    /// variable, no user or system git configuration) plus what the invocation adds. Output arrives as text, as bytes or
    /// line by line, as the invocation asks.
    /// </summary>
    /// <param name="invocation">The command and how to run it.</param>
    /// <param name="ct">Cancels the call: git and its child processes are killed and the call throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>The output and the exit code, which is one of the invocation's allowed codes.</returns>
    /// <exception cref="ArgumentException">The invocation is not consistent: a work tree without a git directory, two stdin sources, or binary output with a line callback.</exception>
    /// <exception cref="DirectoryNotFoundException">The working directory does not exist.</exception>
    /// <exception cref="Savepoint.Core.Errors.GitUnavailableException">git could not be started.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    /// <remarks>
    /// An exit code that the invocation does not allow fails the call with an internal exception carrying git's
    /// standard error; <see cref="GitEngine"/> turns it into a typed error. An exception thrown by a line callback stops
    /// git and is rethrown by the call.
    /// </remarks>
    public async Task<GitResult> RunAsync(GitInvocation invocation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        Validate(invocation);
        ct.ThrowIfCancellationRequested();

        var startInfo = CreateStartInfo(invocation);
        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception e)
        {
            throw new GitUnavailableException(_binary.ExecutablePath, $"git could not be started: {e.Message}", e);
        }

        // Cancelled by the caller, or by a callback that threw.
        using var abort = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var stdinStop = CancellationTokenSource.CreateLinkedTokenSource(abort.Token);
        using var pumpStop = new CancellationTokenSource();
        var failure = new CallbackFailure();
        var stdoutSink = new GitOutputSink(!invocation.BinaryStdout && invocation.OnStdoutLine is null, int.MaxValue, invocation.OnStdoutLine, failure, () => Abort(abort));
        var stderrSink = new GitOutputSink(true, _standardErrorLimit, invocation.OnStderrLine, failure, () => Abort(abort));
        var stdoutBytes = invocation.BinaryStdout ? new GitByteSink() : null;

        // Readers first: a big stdin and a chatty git would otherwise wait on each other.
        var stdout = stdoutBytes is not null
            ? StreamPump.ReadBytesAsync(process.StandardOutput.BaseStream, _options.ReadBufferSize, stdoutBytes.Append, pumpStop.Token)
            : PumpTextAsync(process.StandardOutput.BaseStream, stdoutSink, pumpStop.Token);
        var stderr = PumpTextAsync(process.StandardError.BaseStream, stderrSink, pumpStop.Token);
        var readers = Task.WhenAll(stdout, stderr);
        var stdin = WriteStdinAsync(process, invocation, stdinStop.Token);

        try
        {
            try
            {
                await process.WaitForExitAsync(abort.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (abort.IsCancellationRequested)
            {
                await KillTreeAsync(process, readers, pumpStop).ConfigureAwait(false);
                throw;
            }

            // Nothing reads stdin any more; a source that never ends must not keep the call alive.
            await stdinStop.CancelAsync().ConfigureAwait(false);
            if (!await CompletesAsync(readers, _options.DrainTimeout, abort.Token).ConfigureAwait(false))
            {
                // A process git left behind still holds a pipe; what was read so far is all there is.
                await pumpStop.CancelAsync().ConfigureAwait(false);
            }

            failure.ThrowIfFailed();
            await ObserveStdinAsync(stdin, abort).ConfigureAwait(false);
            DetachAll(stdoutSink, stderrSink, stdoutBytes);
            return BuildResult(invocation, process.ExitCode, stdoutSink, stderrSink, stdoutBytes);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || failure.HasFailed)
        {
            await pumpStop.CancelAsync().ConfigureAwait(false);
            failure.ThrowIfFailed();
            throw new OperationCanceledException(ct);
        }
        finally
        {
            // Nothing is delivered or collected once the call is over, even from a reader that was given up on.
            DetachAll(stdoutSink, stderrSink, stdoutBytes);

            // Tasks that were given up on end (or fail) by themselves; nobody is left to look at their result.
            _ = readers.ContinueWith(static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            _ = stdin.ContinueWith(static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
    }

    internal IReadOnlyDictionary<string, string> BuildEnvironment(GitInvocation invocation)
    {
        if (invocation.WorkTree is not null && invocation.GitDir is null)
        {
            throw new ArgumentException("A work tree needs a git dir.", nameof(invocation));
        }

        var environment = new Dictionary<string, string>(_binary.BaseEnvironment, _binary.EnvironmentComparer);
        if (invocation.GitDir is { } gitDir)
        {
            environment["GIT_DIR"] = Path.GetFullPath(gitDir);
        }

        if (invocation.WorkTree is { } workTree)
        {
            environment["GIT_WORK_TREE"] = Path.GetFullPath(workTree);
        }

        if (invocation.ReadOnly)
        {
            environment["GIT_OPTIONAL_LOCKS"] = "0";
        }

        if (invocation.ExtraEnvironment is { } extra)
        {
            foreach (var (name, value) in extra)
            {
                if (value is null)
                {
                    environment.Remove(name);
                }
                else
                {
                    environment[name] = value;
                }
            }
        }

        return environment;
    }

    private static void Validate(GitInvocation invocation)
    {
        var stdinSources = (invocation.Stdin is null ? 0 : 1) + (invocation.StdinBytes is null ? 0 : 1) + (invocation.StdinStream is null ? 0 : 1);
        if (stdinSources > 1)
        {
            throw new ArgumentException("Give at most one of Stdin, StdinBytes and StdinStream.", nameof(invocation));
        }

        if (invocation.BinaryStdout && invocation.OnStdoutLine is not null)
        {
            throw new ArgumentException("Binary stdout has no lines to hand to a callback.", nameof(invocation));
        }
    }

    private ProcessStartInfo CreateStartInfo(GitInvocation invocation)
    {
        var startInfo = new ProcessStartInfo(_binary.ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = _utf8,
            StandardOutputEncoding = _utf8,
            StandardErrorEncoding = _utf8,
        };

        foreach (var argument in invocation.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment.Clear();
        foreach (var (name, value) in BuildEnvironment(invocation))
        {
            startInfo.Environment[name] = value;
        }

        if ((invocation.WorkingDirectory ?? invocation.WorkTree) is { } workingDirectory)
        {
            var full = Path.GetFullPath(workingDirectory);
            if (!Directory.Exists(full))
            {
                throw new DirectoryNotFoundException($"The working directory for git does not exist: {full}");
            }

            startInfo.WorkingDirectory = full;
        }

        return startInfo;
    }

    private async Task PumpTextAsync(Stream stream, GitOutputSink sink, CancellationToken ct)
    {
        await StreamPump.ReadTextAsync(stream, _options.ReadBufferSize, sink.Append, ct).ConfigureAwait(false);
        sink.Complete();
    }

    private async Task WriteStdinAsync(Process process, GitInvocation invocation, CancellationToken ct)
    {
        var stdin = process.StandardInput.BaseStream;
        try
        {
            if (invocation.StdinStream is { } source)
            {
                await CopyStdinAsync(source, stdin, ct).ConfigureAwait(false);
            }
            else if (invocation.StdinBytes is { } bytes)
            {
                await WriteStdinChunkAsync(stdin, bytes, ct).ConfigureAwait(false);
            }
            else if (invocation.Stdin is { } text)
            {
                await WriteStdinChunkAsync(stdin, _utf8.GetBytes(text), ct).ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                await stdin.DisposeAsync().ConfigureAwait(false);
            }
            catch (IOException)
            {
                // git is already gone.
            }
        }
    }

    private async Task CopyStdinAsync(Stream source, Stream stdin, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(_options.ReadBufferSize);
        try
        {
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, _options.ReadBufferSize), ct).ConfigureAwait(false)) > 0)
            {
                if (!await WriteStdinChunkAsync(stdin, buffer.AsMemory(0, read), ct).ConfigureAwait(false))
                {
                    return;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // False when git closed its end first: it has all the input it wanted and its exit code says how that went.
    private static async Task<bool> WriteStdinChunkAsync(Stream stdin, ReadOnlyMemory<byte> chunk, CancellationToken ct)
    {
        try
        {
            await stdin.WriteAsync(chunk, ct).ConfigureAwait(false);
            await stdin.FlushAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    // The source of a StdinStream can fail on its own; that failure belongs to the caller, not to git.
    private async Task ObserveStdinAsync(Task stdin, CancellationTokenSource abort)
    {
        try
        {
            await CompletesAsync(stdin, _options.DrainTimeout, abort.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!abort.IsCancellationRequested)
        {
            // Stopped on purpose once git exited.
        }
    }

    private async Task KillTreeAsync(Process process, Task readers, CancellationTokenSource pumpStop)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Already exited, or part of the tree could not be reached; take at least the process itself.
            try
            {
                process.Kill();
            }
            catch (Exception)
            {
                // Nothing more can be done from here.
            }
        }

        // Bounded: a process that survives the kill must not hold the caller's cancellation hostage.
        using var gaveUp = new CancellationTokenSource(_options.KillWaitTimeout);
        try
        {
            await process.WaitForExitAsync(gaveUp.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Left running; the caller is told the call was cancelled all the same.
        }

        try
        {
            await CompletesAsync(readers, _options.DrainTimeout, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A pipe that broke on the way out says nothing the caller needs: the call is being cancelled.
        }

        await pumpStop.CancelAsync().ConfigureAwait(false);
    }

    private static async Task<bool> CompletesAsync(Task task, TimeSpan timeout, CancellationToken ct)
    {
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var delay = Task.Delay(timeout, timer.Token);
        var first = await Task.WhenAny(task, delay).ConfigureAwait(false);
        await timer.CancelAsync().ConfigureAwait(false);
        if (first == task)
        {
            await task.ConfigureAwait(false);
            return true;
        }

        ct.ThrowIfCancellationRequested();
        return false;
    }

    private static void DetachAll(GitOutputSink stdout, GitOutputSink stderr, GitByteSink? stdoutBytes)
    {
        stdout.Detach();
        stderr.Detach();
        stdoutBytes?.Detach();
    }

    private static GitResult BuildResult(GitInvocation invocation, int exitCode, GitOutputSink stdout, GitOutputSink stderr, GitByteSink? stdoutBytes)
    {
        if (!invocation.AllowedExitCodes.Contains(exitCode))
        {
            throw new GitCommandException(invocation.Arguments, exitCode, stderr.Text);
        }

        return new GitResult(exitCode, stdout.Text, stderr.Text, stdoutBytes?.ToArray() ?? []);
    }

    private static void Abort(CancellationTokenSource abort)
    {
        try
        {
            abort.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The call already finished.
        }
    }
}
