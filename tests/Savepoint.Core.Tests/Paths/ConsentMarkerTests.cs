using System.Text;
using Savepoint.Core.Errors;
using Savepoint.Core.Paths;

namespace Savepoint.Core.Tests.Paths;

public class ConsentMarkerTests
{
    private static readonly DateTimeOffset _confirmedAt = new(2026, 10, 20, 9, 3, 11, 402, TimeSpan.FromHours(-5));

    public static TheoryData<string> CorruptContents => new()
    {
        "",
        "not json",
        "{\"gameId\": \"a\"",
        "5",
        "[]",
        "null",
        "\"text\"",
        "{}",
        "{\"role\":\"install\",\"confirmedAt\":\"2026-10-20T14:03:11.402Z\"}",
        "{\"gameId\":\"a\",\"confirmedAt\":\"2026-10-20T14:03:11.402Z\"}",
        "{\"gameId\":\"a\",\"role\":\"install\"}",
        "{\"gameId\":\"\",\"role\":\"install\",\"confirmedAt\":\"2026-10-20T14:03:11.402Z\"}",
        "{\"gameId\":1,\"role\":\"install\",\"confirmedAt\":\"2026-10-20T14:03:11.402Z\"}",
        "{\"gameId\":\"a\",\"role\":\"saves\",\"confirmedAt\":\"2026-10-20T14:03:11.402Z\"}",
        "{\"gameId\":\"a\",\"role\":\"Install\",\"confirmedAt\":\"2026-10-20T14:03:11.402Z\"}",
        "{\"gameId\":\"a\",\"role\":2,\"confirmedAt\":\"2026-10-20T14:03:11.402Z\"}",
        "{\"gameId\":\"a\",\"role\":\"install\",\"confirmedAt\":\"yesterday\"}",
        "{\"gameId\":\"a\",\"role\":\"install\",\"confirmedAt\":12345}",
    };

    [Fact]
    public void The_file_names_and_exclude_pattern_are_the_documented_ones()
    {
        Assert.Equal(".savepoint-managed.json", ConsentMarker.FileName);
        Assert.Equal(".savepoint-managed.json.tmp", ConsentMarker.TempFileName);
        Assert.Equal(".savepoint-managed.json*", ConsentMarker.ExcludePattern);
    }

    [Fact]
    public void Read_reports_missing_when_there_is_no_marker_or_no_folder()
    {
        using var tree = new TempTree();

        Assert.IsType<ConsentMarkerState.Missing>(ConsentMarker.Read(tree.Dir("work")));
        Assert.IsType<ConsentMarkerState.Missing>(ConsentMarker.Read(Path.Combine(tree.Root, "nowhere")));
    }

    [Fact]
    public void Write_then_Read_returns_the_same_game_role_and_time()
    {
        using var tree = new TempTree();
        var work = tree.Dir("work");

        ConsentMarker.Write(work, "skyrim-se", ConsentRole.External, _confirmedAt);

        var state = Assert.IsType<ConsentMarkerState.Valid>(ConsentMarker.Read(work));
        Assert.Equal("skyrim-se", state.Marker.GameId);
        Assert.Equal(ConsentRole.External, state.Marker.Role);
        Assert.Equal(_confirmedAt.ToUniversalTime(), state.Marker.ConfirmedAt.ToUniversalTime());
    }

    [Fact]
    public void Write_produces_the_documented_file_exactly()
    {
        using var tree = new TempTree();
        var work = tree.Dir("work");

        ConsentMarker.Write(work, "skyrim-se", ConsentRole.Install, _confirmedAt);

        var file = Path.Combine(work, ConsentMarker.FileName);
        Assert.True(File.Exists(file), "Write should create the marker file");
        var bytes = File.ReadAllBytes(file);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()), "the file must not start with a byte order mark");
        Assert.Equal(
            "{\n  \"gameId\": \"skyrim-se\",\n  \"role\": \"install\",\n  \"confirmedAt\": \"2026-10-20T14:03:11.402Z\"\n}",
            Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Write_replaces_an_existing_marker_and_leaves_no_temp_file()
    {
        using var tree = new TempTree();
        var work = tree.Dir("work");
        ConsentMarker.Write(work, "first", ConsentRole.Install, _confirmedAt);

        ConsentMarker.Write(work, "second", ConsentRole.Install, _confirmedAt);

        var state = Assert.IsType<ConsentMarkerState.Valid>(ConsentMarker.Read(work));
        Assert.Equal("second", state.Marker.GameId);
        Assert.False(File.Exists(Path.Combine(work, ConsentMarker.TempFileName)));
    }

    [Fact]
    public void Write_creates_the_folder_when_it_does_not_exist_yet()
    {
        using var tree = new TempTree();
        var work = Path.Combine(tree.Root, "new", "folder");

        ConsentMarker.Write(work, "game", ConsentRole.Install, _confirmedAt);

        Assert.IsType<ConsentMarkerState.Valid>(ConsentMarker.Read(work));
    }

    [Fact]
    public void A_temp_file_left_by_a_crash_does_not_get_in_the_way_of_the_next_write()
    {
        using var tree = new TempTree();
        var work = tree.Dir("work");
        File.WriteAllText(Path.Combine(work, ConsentMarker.TempFileName), "half written");

        ConsentMarker.Write(work, "game", ConsentRole.Install, _confirmedAt);

        Assert.IsType<ConsentMarkerState.Valid>(ConsentMarker.Read(work));
        Assert.False(File.Exists(Path.Combine(work, ConsentMarker.TempFileName)));
    }

    [Fact]
    public void A_failed_write_does_not_leave_its_temp_file_behind()
    {
        using var tree = new TempTree();
        var work = tree.Dir("work");
        Directory.CreateDirectory(Path.Combine(work, ConsentMarker.FileName));

        // Moving a file over a folder fails as an IO error on Unix and as access denied on Windows.
        var error = Record.Exception(() => ConsentMarker.Write(work, "game", ConsentRole.Install, _confirmedAt));

        Assert.True(error is IOException or UnauthorizedAccessException, $"expected the write to fail, got {error?.GetType().Name ?? "no exception"}");
        Assert.False(File.Exists(Path.Combine(work, ConsentMarker.TempFileName)));
    }

    [Theory]
    [MemberData(nameof(CorruptContents))]
    public void Read_reports_corrupt_for_bad_json_or_bad_fields(string content)
    {
        using var tree = new TempTree();
        var work = tree.Dir("work");
        File.WriteAllText(Path.Combine(work, ConsentMarker.FileName), content);

        Assert.IsType<ConsentMarkerState.Corrupt>(ConsentMarker.Read(work));
    }

    [Fact]
    public void Read_accepts_a_byte_order_mark_and_unknown_extra_fields()
    {
        using var tree = new TempTree();
        var work = tree.Dir("work");
        var json = "{\"gameId\":\"a\",\"role\":\"external\",\"confirmedAt\":\"2026-10-20T14:03:11.402Z\",\"note\":\"hi\"}";
        File.WriteAllBytes(Path.Combine(work, ConsentMarker.FileName), [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(json)]);

        var state = Assert.IsType<ConsentMarkerState.Valid>(ConsentMarker.Read(work));
        Assert.Equal(("a", ConsentRole.External), (state.Marker.GameId, state.Marker.Role));
    }

    [Fact]
    public void Verify_passes_for_the_same_game_and_role()
    {
        using var tree = new TempTree();
        var work = tree.Dir("work");
        ConsentMarker.Write(work, "game", ConsentRole.Install, _confirmedAt);

        ConsentMarker.Verify(work, "game", ConsentRole.Install);
    }

    [Fact]
    public void Verify_reports_missing_when_there_is_no_marker()
    {
        using var tree = new TempTree();
        var work = tree.Dir("work");

        var error = Assert.Throws<ConsentMarkerException>(() => ConsentMarker.Verify(work, "game", ConsentRole.External));

        Assert.Equal("error.consentMarker.missing", error.MessageId);
        Assert.Equal(work, error.Args["path"]);
        Assert.Equal("external", error.Args["role"]);
    }

    [Theory]
    [InlineData("other-game", ConsentRole.Install, "other-game", "install")]
    [InlineData("game", ConsentRole.External, "game", "external")]
    public void Verify_reports_a_conflict_with_the_existing_owner(string existingGame, ConsentRole existingRole, string expectedGame, string expectedRole)
    {
        using var tree = new TempTree();
        var work = tree.Dir("work");
        ConsentMarker.Write(work, existingGame, existingRole, _confirmedAt);

        var error = Assert.Throws<ConsentMarkerException>(() => ConsentMarker.Verify(work, "game", ConsentRole.Install));

        Assert.Equal("error.consentMarker.conflict", error.MessageId);
        Assert.Equal(expectedGame, error.Args["existingGameId"]);
        Assert.Equal(expectedRole, error.Args["existingRole"]);
        Assert.Equal("install", error.Args["role"]);
    }

    [Fact]
    public void Verify_treats_a_corrupt_marker_as_a_conflict_with_no_known_owner()
    {
        using var tree = new TempTree();
        var work = tree.Dir("work");
        File.WriteAllText(Path.Combine(work, ConsentMarker.FileName), "{ broken");

        var error = Assert.Throws<ConsentMarkerException>(() => ConsentMarker.Verify(work, "game", ConsentRole.Install));

        Assert.Equal("error.consentMarker.conflict", error.MessageId);
        Assert.Null(error.Args["existingGameId"]);
        Assert.Null(error.Args["existingRole"]);
    }

    [Fact]
    public void Remove_deletes_a_matching_marker()
    {
        using var tree = new TempTree();
        var work = tree.Dir("work");
        ConsentMarker.Write(work, "game", ConsentRole.Install, _confirmedAt);

        ConsentMarker.Remove(work, "game", ConsentRole.Install);

        Assert.False(File.Exists(Path.Combine(work, ConsentMarker.FileName)));
    }

    [Fact]
    public void Remove_does_nothing_when_there_is_no_marker()
    {
        using var tree = new TempTree();
        var work = tree.Dir("work");

        ConsentMarker.Remove(work, "game", ConsentRole.Install);
        ConsentMarker.Remove(Path.Combine(tree.Root, "nowhere"), "game", ConsentRole.Install);

        Assert.Empty(Directory.GetFileSystemEntries(work));
    }

    [Theory]
    [InlineData("other-game", ConsentRole.Install)]
    [InlineData("game", ConsentRole.External)]
    public void Remove_refuses_a_marker_that_belongs_to_someone_else_and_keeps_it(string existingGame, ConsentRole existingRole)
    {
        using var tree = new TempTree();
        var work = tree.Dir("work");
        ConsentMarker.Write(work, existingGame, existingRole, _confirmedAt);

        var error = Assert.Throws<ConsentMarkerException>(() => ConsentMarker.Remove(work, "game", ConsentRole.Install));

        Assert.Equal("error.consentMarker.conflict", error.MessageId);
        Assert.IsType<ConsentMarkerState.Valid>(ConsentMarker.Read(work));
    }

    [Fact]
    public void Remove_refuses_a_corrupt_marker_and_keeps_it()
    {
        using var tree = new TempTree();
        var work = tree.Dir("work");
        var file = Path.Combine(work, ConsentMarker.FileName);
        File.WriteAllText(file, "{ broken");

        Assert.Throws<ConsentMarkerException>(() => ConsentMarker.Remove(work, "game", ConsentRole.Install));

        Assert.True(File.Exists(file));
    }
}
