using Savepoint.App.Platform.Processes;

namespace Savepoint.App.Tests.Platform.Fakes;

// Records every command and answers from a script, so no test starts a real program.
internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Func<ProcessCommand, ProcessOutput?> _answer;

    public FakeProcessRunner(Func<ProcessCommand, ProcessOutput?>? answer = null)
    {
        _answer = answer ?? (_ => new ProcessOutput(0, string.Empty));
    }

    public List<ProcessCommand> Started { get; } = [];

    public List<ProcessCommand> Run { get; } = [];

    public bool CanStart { get; set; } = true;

    /// <summary>What a watched start answers: false stands for a program that exited with an error at once.</summary>
    public bool ConfirmedStart { get; set; } = true;

    public List<ProcessCommand> Confirmed { get; } = [];

    public bool TryStart(ProcessCommand command)
    {
        Started.Add(command);
        return CanStart;
    }

    public Task<bool> TryStartAndConfirmAsync(ProcessCommand command, TimeSpan grace, CancellationToken cancellationToken = default)
    {
        Confirmed.Add(command);
        return Task.FromResult(CanStart && ConfirmedStart);
    }

    public Task<ProcessOutput?> RunAsync(ProcessCommand command, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Run.Add(command);
        return Task.FromResult(_answer(command));
    }

    public static FakeProcessRunner Printing(string output, int exitCode = 0) => new(_ => new ProcessOutput(exitCode, output));

    public static FakeProcessRunner Unavailable() => new(_ => null) { CanStart = false };
}
