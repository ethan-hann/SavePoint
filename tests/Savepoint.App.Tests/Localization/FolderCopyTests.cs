using Savepoint.App.Localization;

namespace Savepoint.App.Tests.Localization;

// A game has up to three tracked folders: the game folder, the mods folder and the saves folder. Copy that picks its wording by
// folder has to know all three, or a mods folder gets the wording of "some other folder".
public class FolderCopyTests
{
    private static readonly string[] _folderArguments = ["role", "folder", "part"];
    private static readonly string[] _knownFolders = ["install", "external", "saves"];

    private static IEnumerable<(string Key, string Message, IcuArgument Argument)> FolderSelects()
    {
        foreach (var (key, message) in CatalogLint.English())
        {
            foreach (var argument in CatalogLint.Arguments(message))
            {
                if (argument.Kind == "select" && _folderArguments.Contains(argument.Name) && argument.Options.Any(_knownFolders.Contains))
                {
                    yield return (key, message, argument);
                }
            }
        }
    }

    [Fact]
    public void Every_message_that_words_itself_by_folder_has_a_mods_branch()
    {
        var missing = FolderSelects()
            .Where(select => !select.Argument.Options.Contains("mods"))
            .Select(select => $"'{select.Key}' picks its wording by {select.Argument.Name} but has no mods branch")
            .ToList();

        LintAssert.NoProblems(missing);
    }

    [Fact]
    public void The_mods_branch_of_each_message_says_mods_and_differs_from_the_wording_for_an_unlisted_folder()
    {
        var catalog = LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true);
        var problems = new List<string>();

        foreach (var (key, message, folder) in FolderSelects().Where(select => select.Argument.Options.Contains("mods")))
        {
            var mods = catalog.Format(key, ArgumentsFor(message, folder.Name, "mods"));
            var unlisted = catalog.Format(key, ArgumentsFor(message, folder.Name, "unlisted"));
            if (!mods.Contains("mods", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"'{key}' does not say \"mods\" for a mods folder: {mods}");
            }

            if (mods == unlisted)
            {
                problems.Add($"'{key}' words a mods folder like any other folder: {mods}");
            }
        }

        LintAssert.NoProblems(problems);
    }

    // Every argument gets a plain sample, and the folder argument the value under test.
    private static Dictionary<string, object?> ArgumentsFor(string message, string folderArgument, string folder)
    {
        var args = new Dictionary<string, object?>();
        foreach (var argument in CatalogLint.Arguments(message))
        {
            args[argument.Name] = argument.Name == folderArgument
                ? folder
                : argument.Kind switch
                {
                    "plural" or "selectordinal" or "number" => 2,
                    "select" => argument.Options.FirstOrDefault(option => option != "other") ?? "other",
                    "date" or "time" => new DateTime(2026, 10, 20, 15, 30, 0, DateTimeKind.Utc),
                    _ => "sample text",
                };
        }

        return args;
    }
}
