using System.Text.RegularExpressions;
using Savepoint.App.Hosting;

namespace Savepoint.App.Tests.Hosting;

public sealed partial class SerilogDiagnosticLogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "savepoint-serilog-" + Guid.NewGuid().ToString("N"));
    private SerilogDiagnosticLog? _log;

    private string Logs => Path.Combine(_root, "logs");

    private SerilogDiagnosticLog Open() => _log = SerilogDiagnosticLog.TryCreate(Logs) ?? throw new InvalidOperationException("The log did not open.");

    private string[] Files() => Directory.Exists(Logs) ? [.. Directory.GetFiles(Logs).Order(StringComparer.Ordinal)] : [];

    // Reads through a second handle, the way a person opening the file in an editor would.
    private static string ReadWhileOpen(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return new StreamReader(stream).ReadToEnd();
    }

    // The text of the file the log is writing now; a log that has not opened one fails here, on an assertion.
    private static string Written(SerilogDiagnosticLog log)
    {
        Assert.NotNull(log.CurrentFile);
        return ReadWhileOpen(log.CurrentFile);
    }

    public void Dispose()
    {
        _log?.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [GeneratedRegex(@"^savepoint-\d{8}\.log$")]
    private static partial Regex DatedName();

    [Fact]
    public void A_line_goes_into_a_dated_file_in_the_folder_which_is_made_when_missing()
    {
        var log = Open();

        log.Information("hello");

        var file = Assert.Single(Files());
        Assert.Matches(DatedName(), Path.GetFileName(file));
        Assert.Equal(file, log.CurrentFile);
        Assert.Equal(Logs, log.Folder);
        Assert.Contains("hello", ReadWhileOpen(file), StringComparison.Ordinal);
    }

    [Fact]
    public void Each_line_says_when_how_serious_and_what()
    {
        var log = Open();

        log.Information("one");
        log.Warning("two");
        log.Error("three");

        var lines = Written(log).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2} \[INF\] one$", lines[0]);
        Assert.EndsWith("[WRN] two", lines[1], StringComparison.Ordinal);
        Assert.EndsWith("[ERR] three", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public void An_exception_is_written_with_its_type_message_and_stack()
    {
        var log = Open();
        Exception thrown;
        try
        {
            throw new InvalidOperationException("the disk went away");
        }
        catch (InvalidOperationException e)
        {
            thrown = e;
        }

        log.Error("could not save", thrown);

        var text = Written(log);
        Assert.Contains("could not save", text, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException: the disk went away", text, StringComparison.Ordinal);
        Assert.Contains(nameof(An_exception_is_written_with_its_type_message_and_stack), text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_message_with_braces_is_written_as_it_is_and_never_read_as_a_template()
    {
        var log = Open();
        const string Message = @"Copied C:\Games\{Name}\{0} and {broken, then {{done}}";

        log.Warning(Message);

        Assert.Contains(Message, Written(log), StringComparison.Ordinal);
    }

    [Fact]
    public void An_error_is_in_the_file_by_the_time_the_call_returns()
    {
        var log = Open();
        log.Information("first");

        log.Error("the last thing before a crash");

        Assert.Contains("the last thing before a crash", Written(log), StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_newest_fourteen_files_are_kept()
    {
        Directory.CreateDirectory(Logs);
        for (var day = 1; day <= 20; day++)
        {
            File.WriteAllText(Path.Combine(Logs, $"savepoint-202609{day:00}.log"), "old");
        }

        var log = Open();
        log.Information("today");

        var names = Files().Select(Path.GetFileName).ToList();
        Assert.Equal(14, names.Count);
        Assert.NotNull(log.CurrentFile);
        Assert.Contains(Path.GetFileName(log.CurrentFile), names);
        Assert.Contains("savepoint-20260920.log", names);
        Assert.Contains("savepoint-20260908.log", names);
        Assert.DoesNotContain("savepoint-20260907.log", names);
        Assert.DoesNotContain("savepoint-20260901.log", names);
    }

    [Fact]
    public void A_file_that_reaches_ten_megabytes_rolls_over_to_a_new_one()
    {
        var log = Open();
        var big = new string('x', 4 * 1024 * 1024);
        log.Information(big);
        log.Information(big);
        log.Information(big);
        var first = log.CurrentFile;
        Assert.NotNull(first);

        log.Information("after the roll");

        var rolled = log.CurrentFile;
        Assert.NotNull(rolled);
        Assert.NotEqual(first, rolled);
        Assert.Matches(@"^savepoint-\d{8}_001\.log$", Path.GetFileName(rolled));
        Assert.Equal(2, Files().Length);
        Assert.Contains("after the roll", ReadWhileOpen(rolled), StringComparison.Ordinal);
        Assert.DoesNotContain("after the roll", ReadWhileOpen(first), StringComparison.Ordinal);
    }

    [Fact]
    public void Disposing_closes_the_file_so_it_can_be_deleted()
    {
        var log = Open();
        log.Information("hello");
        var file = log.CurrentFile;
        Assert.NotNull(file);

        log.Dispose();

        File.Delete(file);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void A_folder_that_cannot_be_made_gives_no_log_instead_of_an_exception()
    {
        Directory.CreateDirectory(_root);
        var blocker = Path.Combine(_root, "a-file");
        File.WriteAllText(blocker, "not a folder");

        var log = SerilogDiagnosticLog.TryCreate(Path.Combine(blocker, "logs"));

        Assert.Null(log);
    }
}
