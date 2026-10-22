using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;
using Savepoint.Core.Paths;

namespace Savepoint.Core.Services;

/// <summary>One folder of a game with its path resolved for this machine.</summary>
/// <param name="Role">Which of the game's folders this is.</param>
/// <param name="Raw">The path as stored in the definition, tokens included.</param>
/// <param name="Path">The absolute path, or null when a token in <paramref name="Raw"/> does not exist on this OS.</param>
/// <param name="Field">The definition field the path came from, for validation issues.</param>
internal sealed record GameFolder(RepoRole Role, string Raw, string? Path, string Field);

/// <summary>Turns a game's definition into folders and repository references; the one place the storage layout is applied.</summary>
internal static class GameFolders
{
    /// <summary>The folders a definition names, install first.</summary>
    public static IReadOnlyList<(RepoRole Role, string Raw, string Field)> Named(GameDefinition game) =>
        game.External is { } external
            ? [(RepoRole.Install, game.Install.Path, "install.path"), (RepoRole.External, external.Path, "external.path")]
            : [(RepoRole.Install, game.Install.Path, "install.path")];

    /// <summary>How the log names a folder: "install" or "saves".</summary>
    public static string LogName(RepoRole role) => role == RepoRole.Install ? "install" : "saves";

    /// <summary>The roles a definition has folders for.</summary>
    public static IReadOnlyList<RepoRole> RolesOf(GameDefinition game) =>
        game.External is null ? [RepoRole.Install] : [RepoRole.Install, RepoRole.External];

    /// <summary>Resolves every folder, leaving a folder whose token this OS lacks with a null path.</summary>
    public static IReadOnlyList<GameFolder> ResolveLoosely(PathTokenResolver resolver, GameDefinition game) =>
        [.. Named(game).Select(folder => new GameFolder(folder.Role, folder.Raw, TryResolve(resolver, folder.Raw, folder.Field), folder.Field))];

    /// <summary>Resolves every folder or throws one <see cref="ValidationException"/> naming every field that cannot be resolved.</summary>
    public static IReadOnlyList<GameFolder> ResolveStrictly(PathTokenResolver resolver, GameDefinition game)
    {
        var issues = new List<ValidationIssue>();
        var folders = new List<GameFolder>();
        foreach (var (role, raw, field) in Named(game))
        {
            try
            {
                folders.Add(new GameFolder(role, raw, resolver.Resolve(raw, field), field));
            }
            catch (ValidationException invalid)
            {
                issues.AddRange((IReadOnlyList<ValidationIssue>)invalid.Args["issues"]!);
            }
        }

        return issues.Count == 0 ? folders : throw new ValidationException(issues);
    }

    /// <summary>The repository reference for a resolved folder; the git directory lives under the storage root, keyed by game and role.</summary>
    public static RepoRef Repo(StorageSnapshot storage, string gameId, GameFolder folder) =>
        new(storage.GitDirectory(gameId, folder.Role), folder.Path ?? Unresolved(storage, gameId, folder.Role), gameId, folder.Role);

    /// <summary>
    /// The repository references for reading history or optimizing storage, which need only the git directory. A folder whose
    /// token cannot be resolved still gets a reference, with a work tree inside the game's storage folder that nothing reads.
    /// </summary>
    public static IReadOnlyList<RepoRef> ReposForGitDirOnly(BackendServices services, StorageSnapshot storage, GameDefinition game) =>
        [.. ResolveLoosely(services.PathTokens, game).Select(folder => Repo(storage, game.Id, folder) with { IgnorePatterns = game.IgnorePaths })];

    /// <summary>The repository references of a game whose folders must all resolve, install first. Each carries the game's current ignore patterns.</summary>
    public static IReadOnlyList<RepoRef> Repos(BackendServices services, StorageSnapshot storage, GameDefinition game) =>
        [.. ResolveStrictly(services.PathTokens, game).Select(folder => Repo(storage, game.Id, folder) with { IgnorePatterns = game.IgnorePaths })];

    private static string Unresolved(StorageSnapshot storage, string gameId, RepoRole role) =>
        Path.Combine(storage.GameDirectory(gameId), role == RepoRole.Install ? "unresolved-install" : "unresolved-external");

    private static string? TryResolve(PathTokenResolver resolver, string raw, string field)
    {
        try
        {
            return resolver.Resolve(raw, field);
        }
        catch (ValidationException)
        {
            return null;
        }
    }
}

/// <summary>
/// Wraps an error that belongs to one folder of a game so the editor can place it under that folder: the same message id and
/// arguments as the error inside, plus <c>role</c>.
/// </summary>
internal sealed class FolderErrorException : SavepointException
{
    public FolderErrorException(SavepointException inner, RepoRole role)
        : base(inner.MessageId, inner.Message, WithRole(inner.Args, role), inner)
    {
    }

    private static Dictionary<string, object?> WithRole(IReadOnlyDictionary<string, object?> args, RepoRole role)
    {
        var all = new Dictionary<string, object?>(args) { ["role"] = role };
        return all;
    }
}
