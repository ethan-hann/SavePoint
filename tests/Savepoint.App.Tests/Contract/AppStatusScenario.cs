using System.Text.RegularExpressions;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Contract;

// The startup checks and the choices the recovery screens offer, as lines of text so the fake and the real backend can be compared.
// Each part starts from a fresh harness, since a part changes the catalog or moves the storage folder.
internal static class AppStatusScenario
{
    public static async Task<IReadOnlyList<string>> RunAsync(Func<ContractHarness> create, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        await using (var healthy = create())
        {
            await RunCatalogAsync(healthy, lines, cancellationToken);
        }

        await using (var lost = create())
        {
            await RunLostStorageAsync(lost, lines, cancellationToken);
        }

        await using (var startedEmpty = create())
        {
            await RunEmptyStartAsync(startedEmpty, lines, cancellationToken);
        }

        return lines;
    }

    private static async Task RunCatalogAsync(ContractHarness backend, List<string> lines, CancellationToken cancellationToken)
    {
        Add(lines, "first check", Show(await backend.AppStatus.CheckAsync(cancellationToken), status => Describe(backend, status)));
        Add(lines, "second check", Show(await backend.AppStatus.CheckAsync(cancellationToken), status => Describe(backend, status)));
        Add(lines, "check with a cancelled token", Show(await backend.AppStatus.CheckAsync(new CancellationToken(canceled: true)), _ => "unexpected success"));

        var games = backend.MakeCatalogFileCorrupt("games.json", withBackup: true);
        Add(lines, "check with a games file that has a backup", Show(await backend.AppStatus.CheckAsync(cancellationToken), status => Describe(backend, status)));
        Add(lines, "recover a file that is not corrupt", Show(await backend.AppStatus.RecoverCatalogFileAsync(Path.Combine(backend.CatalogDirectory, "settings.json"), CatalogRecovery.StartEmpty), Moved));
        Add(lines, "recover a file that is not a catalog file", Show(await backend.AppStatus.RecoverCatalogFileAsync(Path.Combine(backend.CatalogDirectory, "notes.txt"), CatalogRecovery.StartEmpty), Moved));
        Add(lines, "restore the games file from its backup", Show(await backend.AppStatus.RecoverCatalogFileAsync(games, CatalogRecovery.RestoreBackup), Moved));
        Add(lines, "check after the restore", Show(await backend.AppStatus.CheckAsync(cancellationToken), status => Describe(backend, status)));

        var profiles = backend.MakeCatalogFileCorrupt("profiles.json", withBackup: false);
        Add(lines, "check with a profiles file that has no backup", Show(await backend.AppStatus.CheckAsync(cancellationToken), status => Describe(backend, status)));
        Add(lines, "restore the profiles file without a backup", Show(await backend.AppStatus.RecoverCatalogFileAsync(profiles, CatalogRecovery.RestoreBackup), Moved));
        Add(lines, "start the profiles file empty", Show(await backend.AppStatus.RecoverCatalogFileAsync(profiles, CatalogRecovery.StartEmpty), Moved));
        Add(lines, "check after starting empty", Show(await backend.AppStatus.CheckAsync(cancellationToken), status => Describe(backend, status)));
    }

    // The history is in the folder that was moved, so that folder is the one the app can take back.
    private static async Task RunLostStorageAsync(ContractHarness backend, List<string> lines, CancellationToken cancellationToken)
    {
        var moved = Path.Combine(backend.Root, "drive-d", "SavePoint");
        await HaveHistoryAsync(backend, lines, cancellationToken);

        Add(lines, "use a folder while the storage is reachable", Show(await backend.AppStatus.UseStorageFolderAsync(Path.Combine(backend.Root, "anywhere")), Root(backend, moved)));
        Add(lines, "start empty while the storage is reachable", Show(await backend.AppStatus.StartWithEmptyStorageAsync(), Root(backend, moved)));

        await backend.LoseStorageAsync(moved, cancellationToken);
        Add(lines, "check after the storage folder was moved", Show(await backend.AppStatus.CheckAsync(cancellationToken), status => Describe(backend, status, moved)));

        var empty = Directory.CreateDirectory(Path.Combine(backend.Root, "drive-e", "SavePoint")).FullName;
        Add(lines, "use a folder that holds no history", Show(await backend.AppStatus.UseStorageFolderAsync(empty), Root(backend, moved)));
        Add(lines, "use a folder that is not there", Show(await backend.AppStatus.UseStorageFolderAsync(Path.Combine(backend.Root, "drive-f", "SavePoint")), Root(backend, moved)));
        Add(lines, "check after the refusals", Show(await backend.AppStatus.CheckAsync(cancellationToken), status => Describe(backend, status, moved)));

        Add(lines, "use the folder that holds the history", Show(await backend.AppStatus.UseStorageFolderAsync(moved), Root(backend, moved)));
        Add(lines, "check after taking the history back", Show(await backend.AppStatus.CheckAsync(cancellationToken), status => Describe(backend, status, moved)));
        Add(lines, "settings name the folder that was taken", Show(await backend.Settings.GetAsync(cancellationToken), settings => Where(backend, settings.StorageRoot, moved)));
    }

    private static async Task RunEmptyStartAsync(ContractHarness backend, List<string> lines, CancellationToken cancellationToken)
    {
        var moved = Path.Combine(backend.Root, "drive-d", "SavePoint");
        await HaveHistoryAsync(backend, lines, cancellationToken);
        await backend.LoseStorageAsync(moved, cancellationToken);

        Add(lines, "start empty", Show(await backend.AppStatus.StartWithEmptyStorageAsync(), Root(backend, moved)));
        Add(lines, "check after starting empty", Show(await backend.AppStatus.CheckAsync(cancellationToken), status => Describe(backend, status, moved)));
        Add(lines, "use the old history folder after the empty start", Show(await backend.AppStatus.UseStorageFolderAsync(moved), Root(backend, moved)));
    }

    // One game with a saved profile, so the storage folder holds history worth finding again.
    private static async Task HaveHistoryAsync(ContractHarness backend, List<string> lines, CancellationToken cancellationToken)
    {
        Add(lines, "check before the first game", Show(await backend.AppStatus.CheckAsync(cancellationToken), status => Describe(backend, status)));
        var game = backend.NewGame("Alpha");
        await backend.Games.AddAsync(game);
        var created = await backend.Profiles.CreateAsync(game.Id, "Vanilla", null, "Fresh install");
        if (created.Ok)
        {
            await backend.FinishAsync(created.Value.JobId, cancellationToken);
        }

        Add(lines, "a profile was saved", Show(created, _ => "capture started"));
    }

    private static string Describe(ContractHarness backend, StartupStatus status, string? moved = null)
    {
        var corrupt = string.Join(", ", status.Catalog.CorruptFiles.Select(file =>
            $"{Path.GetFileName(file.Path)} backup={(file.BackupPath is null ? "none" : Path.GetFileName(file.BackupPath))} {file.Error.MessageId}"));
        var storage = status.Storage is null
            ? "not checked"
            : $"reachable={status.Storage.Reachable} at {Where(backend, status.Storage.Root, moved)} error={status.Storage.Error?.MessageId ?? "none"} in installer folder={status.Storage.InInstallerFolder}";
        var git = status.Git.Available ? "git ready" : "git missing";
        return $"{git}; catalog in the catalog folder={Same(backend.CatalogDirectory, status.Catalog.Directory)}, corrupt=[{corrupt}]; storage {storage}";
    }

    private static Func<string, string> Root(ContractHarness backend, string moved) => root => "taken: " + Where(backend, root, moved);

    // The file name ends in the time it was moved, which differs from run to run.
    private static string Moved(string path) =>
        "moved aside as " + Regex.Replace(Path.GetFileName(path), @"\d{8}T\d{9}Z", "<time>");

    private static string Where(ContractHarness backend, string path, string? moved) =>
        Same(path, backend.DefaultStorageRoot) ? "the default folder" : moved is not null && Same(path, moved) ? "the moved folder" : "another folder";

    private static bool Same(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);

    private static void Add(List<string> lines, string step, string seen) => lines.Add($"{step}: {seen}");

    private static string Show<T>(OperationResult<T> result, Func<T, string> describe) =>
        result.Ok ? describe(result.Value) : "error " + result.Error.MessageId;
}
