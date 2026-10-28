using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Fakes;

// Launch previews the real backend gave for a game on disk, kept as JSON in Fakes/Previews and written back into the fake by
// ExpectLaunch-style scripts, so a preview is not worked out by hand in a test. The names and paths of the run they were taken in are
// swapped for tokens ({game}, {profile}, {other}, {install}, {saves}, {point:1}) and swapped back for the names of the run that plays them.
// BackendContractTests fails when the real backend stops giving what a file holds; SAVEPOINT_CAPTURE_PREVIEWS=1 writes the files again.
internal static class LaunchPreviewFixtures
{
    public const string CaptureVariable = "SAVEPOINT_CAPTURE_PREVIEWS";

    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        Converters = { new PointIdConverter(), new TargetConverter(), new JsonStringEnumConverter() },

        // The install folder's state is read from Folders, so writing it twice would only say it twice.
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { DropInstall },
        },
    };

    // The fixture folder in the source tree, found from the build output so a capture writes where the files are checked in.
    public static string Folder => Path.Combine(FindSourceRoot(), "Fakes", "Previews");

    public static string Dump(LaunchPreview preview, PreviewNames names)
    {
        // The time a point was saved is the real clock's, so it is the same instant in every fixture.
        var normalized = preview with { AvailableSaves = [.. preview.AvailableSaves.Select(save => save with { CapturedAt = DateTimeOffset.UnixEpoch })] };
        var text = JsonSerializer.Serialize(normalized, _options);
        foreach (var (literal, token) in names.Tokens(preview))
        {
            text = text.Replace(Escaped(literal), token, StringComparison.Ordinal);
        }

        return text;
    }

    public static LaunchPreview Load(string name, PreviewNames names)
    {
        var text = File.ReadAllText(Path.Combine(Folder, name + ".json"));
        text = text.Replace("{game}", Escaped(names.GameId), StringComparison.Ordinal)
            .Replace("{profile}", Escaped(names.ProfileId), StringComparison.Ordinal)
            .Replace("{other}", Escaped(names.OtherProfileId ?? string.Empty), StringComparison.Ordinal)
            .Replace("{install}", Escaped(names.InstallPath), StringComparison.Ordinal)
            .Replace("{saves}", Escaped(names.SavesPath ?? string.Empty), StringComparison.Ordinal);
        for (var i = 1; text.Contains($"{{point:{i}}}", StringComparison.Ordinal); i++)
        {
            text = text.Replace($"{{point:{i}}}", $"point-{i}", StringComparison.Ordinal);
        }

        return JsonSerializer.Deserialize<LaunchPreview>(text, _options) ?? throw new InvalidDataException($"Preview '{name}' is empty.");
    }

    private static void DropInstall(JsonTypeInfo info)
    {
        if (info.Type == typeof(LaunchPreview))
        {
            info.Properties.Remove(info.Properties.Single(property => property.Name == nameof(LaunchPreview.Install)));
        }
    }

    private static string Escaped(string literal) => JsonSerializer.Serialize(literal)[1..^1];

    private static string FindSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Savepoint.App.Tests.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("The test project folder was not found above the build output.");
    }

    private sealed class PointIdConverter : JsonConverter<HistoryPointId>
    {
        public override HistoryPointId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(reader.GetString()!);

        public override void Write(Utf8JsonWriter writer, HistoryPointId value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
    }

    private sealed class TargetConverter : JsonConverter<LaunchTarget>
    {
        public override LaunchTarget? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            return root.GetProperty("kind").GetString() switch
            {
                "tip" => new LaunchTarget.ProfileTip(root.GetProperty("profile").GetString()!),
                "save" => new LaunchTarget.Save(root.GetProperty("profile").GetString()!, new HistoryPointId(root.GetProperty("point").GetString()!)),
                "point" => new LaunchTarget.Point(new HistoryPointId(root.GetProperty("point").GetString()!)),
                var other => throw new JsonException($"Unknown launch target kind '{other}'."),
            };
        }

        public override void Write(Utf8JsonWriter writer, LaunchTarget value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            switch (value)
            {
                case LaunchTarget.ProfileTip tip:
                    writer.WriteString("kind", "tip");
                    writer.WriteString("profile", tip.ProfileId);
                    break;
                case LaunchTarget.Save save:
                    writer.WriteString("kind", "save");
                    writer.WriteString("profile", save.ProfileId);
                    writer.WriteString("point", save.SaveId.Value);
                    break;
                case LaunchTarget.Point point:
                    writer.WriteString("kind", "point");
                    writer.WriteString("point", point.PointId.Value);
                    break;
                default:
                    throw new JsonException($"Unknown launch target {value}.");
            }

            writer.WriteEndObject();
        }
    }
}

// What a run calls the things a preview names, so the real run's names can be swapped for tokens and the fake's swapped back in.
internal sealed record PreviewNames(string GameId, string ProfileId, string InstallPath, string? SavesPath, string? OtherProfileId = null)
{
    // Longest first, so a path that holds another name is replaced whole.
    public IEnumerable<(string Literal, string Token)> Tokens(LaunchPreview preview)
    {
        var pairs = new List<(string, string)> { (InstallPath, "{install}"), (GameId, "{game}"), (ProfileId, "{profile}") };
        if (SavesPath is not null)
        {
            pairs.Add((SavesPath, "{saves}"));
        }

        if (OtherProfileId is not null)
        {
            pairs.Add((OtherProfileId, "{other}"));
        }

        var points = preview.AvailableSaves.Select(save => save.Id.Value).Distinct().Index().Select(entry => (entry.Item, $"{{point:{entry.Index + 1}}}"));
        return pairs.Concat(points).OrderByDescending(pair => pair.Item1.Length);
    }
}
