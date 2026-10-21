using System.Text;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git;

public sealed class GitOutputTests : IDisposable
{
    private static readonly string[] _unicodeNames =
    [
        "caf\u00e9.esp",
        "\u65e5\u672c\u8a9e\u306e\u30bb\u30fc\u30d6.sav",
        "\u00dcn\u00efc\u00f6d\u00e9 \u2713.txt",
        "na\u00efve/\u03a9mega.dat",
    ];

    private readonly GitWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    [Theory]
    [InlineData("", new string[0])]
    [InlineData("a", new[] { "a" })]
    [InlineData("a\0", new[] { "a" })]
    [InlineData("a\0b\0", new[] { "a", "b" })]
    [InlineData("a\0b", new[] { "a", "b" })]
    [InlineData("a\0\0b\0", new[] { "a", "", "b" })]
    public void Nul_separated_text_splits_into_its_entries(string text, string[] expected)
    {
        Assert.Equal<IEnumerable<string>>(expected, GitOutput.SplitNul(text));
    }

    [Fact]
    public void A_result_splits_its_own_stdout_on_nul()
    {
        var result = new GitResult(0, "x/y.txt\0z.txt\0", string.Empty, []);

        Assert.Equal<IEnumerable<string>>(new[] { "x/y.txt", "z.txt" }, result.SplitStandardOutputOnNul());
    }

    [Fact]
    public async Task Non_ascii_file_names_round_trip_through_ls_files_z()
    {
        await StageUnicodeFilesAsync(_ws);

        var result = await _ws.RunAsync("ls-files", "-z");

        Assert.Equal(_unicodeNames.Order(StringComparer.Ordinal), result.SplitStandardOutputOnNul().Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Non_ascii_file_names_survive_reads_that_cut_through_a_character()
    {
        using var small = new GitWorkspace(new GitRunnerOptions { ReadBufferSize = 5 });
        await StageUnicodeFilesAsync(small);

        var result = await small.RunAsync("ls-files", "-z");

        Assert.Equal(_unicodeNames.Order(StringComparer.Ordinal), result.SplitStandardOutputOnNul().Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Progress_meters_arrive_as_separate_lines_even_though_git_ends_them_with_carriage_returns()
    {
        await _ws.InitAsync();
        var lines = new List<string>();

        var result = await _ws.RunAsync(new GitInvocation(["fsck", "--progress"]) { OnStderrLine = lines.Add });

        Assert.Contains('\r', result.StandardError);
        Assert.All(lines, line => Assert.DoesNotContain('\r', line));
        Assert.Equal<IEnumerable<string>>(result.StandardError.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries), lines);
        Assert.Contains("Checking object directories: 100% (256/256)", lines);
        Assert.Contains("Checking object directories: 100% (256/256), done.", lines);
    }

    private static async Task StageUnicodeFilesAsync(GitWorkspace ws)
    {
        await ws.InitAsync();
        foreach (var name in _unicodeNames)
        {
            ws.Write(name, "content");
        }

        await ws.RunAsync("add", "-A");
    }
}

public sealed class LineSplitterTests
{
    [Theory]
    [InlineData(new[] { "a\nb\n" }, new[] { "a", "b" })]
    [InlineData(new[] { "10%\r20%\r30%\n" }, new[] { "10%", "20%", "30%" })]
    [InlineData(new[] { "a\r\nb\r\n" }, new[] { "a", "b" })]
    [InlineData(new[] { "a\r", "\nb\n" }, new[] { "a", "b" })]
    [InlineData(new[] { "x", "\r", "\n", "y", "\n" }, new[] { "x", "y" })]
    [InlineData(new[] { "\n\n" }, new[] { "", "" })]
    [InlineData(new[] { "a\r\rb\n" }, new[] { "a", "", "b" })]
    [InlineData(new[] { "par", "tial li", "ne\n" }, new[] { "partial line" })]
    [InlineData(new[] { "no terminator" }, new[] { "no terminator" })]
    [InlineData(new[] { "ends with cr\r" }, new[] { "ends with cr" })]
    [InlineData(new string[0], new string[0])]
    public void Lines_end_at_newline_carriage_return_or_both(string[] chunks, string[] expected)
    {
        var lines = new List<string>();
        var splitter = new LineSplitter(lines.Add);

        foreach (var chunk in chunks)
        {
            splitter.Append(chunk);
        }

        splitter.Flush();

        Assert.Equal<IEnumerable<string>>(expected, lines);
    }

    [Fact]
    public void Flushing_twice_does_not_repeat_a_line()
    {
        var lines = new List<string>();
        var splitter = new LineSplitter(lines.Add);
        splitter.Append("only");

        splitter.Flush();
        splitter.Flush();

        Assert.Equal<IEnumerable<string>>(new[] { "only" }, lines);
    }
}

public sealed class StreamPumpTests
{
    [Fact]
    public async Task Text_is_decoded_whole_when_the_stream_hands_over_one_byte_at_a_time()
    {
        const string text = "caf\u00e9 \u65e5\u672c\u8a9e \U0001F4BE end";
        var collected = new StringBuilder();

        await StreamPump.ReadTextAsync(new OneByteStream(Encoding.UTF8.GetBytes(text)), 64, chunk => collected.Append(chunk), TestContext.Current.CancellationToken);

        Assert.Equal(text, collected.ToString());
    }

    [Fact]
    public async Task Text_is_decoded_whole_when_the_buffer_is_smaller_than_a_character()
    {
        const string text = "\u65e5\u672c\u8a9e\u306e\u30bb\u30fc\u30d6\U0001F4BE";
        var collected = new StringBuilder();

        await StreamPump.ReadTextAsync(new MemoryStream(Encoding.UTF8.GetBytes(text)), 2, chunk => collected.Append(chunk), TestContext.Current.CancellationToken);

        Assert.Equal(text, collected.ToString());
    }

    [Fact]
    public async Task A_truncated_character_at_the_end_becomes_a_replacement_character()
    {
        var bytes = Encoding.UTF8.GetBytes("ab\u65e5").Take(4).ToArray();
        var collected = new StringBuilder();

        await StreamPump.ReadTextAsync(new MemoryStream(bytes), 16, chunk => collected.Append(chunk), TestContext.Current.CancellationToken);

        Assert.Equal("ab\ufffd", collected.ToString());
    }

    [Fact]
    public async Task Bytes_are_copied_exactly()
    {
        var data = new byte[100_000];
        new Random(3).NextBytes(data);
        using var destination = new MemoryStream();

        await StreamPump.ReadBytesAsync(new OneByteStream(data), 4096, destination, TestContext.Current.CancellationToken);

        Assert.Equal(data, destination.ToArray());
    }

    private sealed class OneByteStream(byte[] data) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => data.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= data.Length || count == 0)
            {
                return 0;
            }

            buffer[offset] = data[_position++];
            return 1;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= data.Length || buffer.Length == 0)
            {
                return ValueTask.FromResult(0);
            }

            buffer.Span[0] = data[_position++];
            return ValueTask.FromResult(1);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
