using Savepoint.Core.Diagnostics;
using Savepoint.Core.Tests.Backend;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Diagnostics;

public class DiagnosticLogPlumbingTests
{
    [Fact]
    public void A_backend_hands_its_services_the_log_it_was_given()
    {
        var log = new RecordingDiagnosticLog { CurrentFile = "current.log" };
        using var workspace = new BackendWorkspace(options => options with { Log = log });

        workspace.Backend.Services.Log.Warning("hello", new InvalidOperationException("why"));

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("hello", entry.Message);
        Assert.Equal("why", entry.Exception?.Message);
        Assert.Equal("current.log", workspace.Backend.Services.Log.CurrentFile);
    }

    [Fact]
    public void A_log_that_throws_cannot_break_the_backend_and_is_still_tried_on_the_next_line()
    {
        var log = new ThrowingLog();
        using var workspace = new BackendWorkspace(options => options with { Log = log });

        var first = Record.Exception(() => workspace.Backend.Services.Log.Error("first"));
        var second = Record.Exception(() => workspace.Backend.Services.Log.Information("second"));

        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(["first", "second"], log.Attempts);
        Assert.Null(Record.Exception(() => _ = workspace.Backend.Services.Log.CurrentFile));
    }

    private sealed class ThrowingLog : IDiagnosticLog
    {
        public List<string> Attempts { get; } = [];

        public string? CurrentFile => throw new IOException("the disk went away");

        public void Information(string message, Exception? exception = null) => Fail(message);

        public void Warning(string message, Exception? exception = null) => Fail(message);

        public void Error(string message, Exception? exception = null) => Fail(message);

        private void Fail(string message)
        {
            Attempts.Add(message);
            throw new IOException("the disk went away");
        }
    }
}
