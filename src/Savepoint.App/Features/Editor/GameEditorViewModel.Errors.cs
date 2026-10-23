using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Theme;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Features.Editor;

public sealed partial class GameEditorViewModel
{
    private enum Field
    {
        Name,
        Install,
        Saves,
        Launch,
        ExePath,
        Uri,
        SteamAppId,
    }

    // Errors about one folder are drawn under that folder's field with the error card's copy; every other id is a card above the form.
    private static readonly string[] _folderErrorIds =
    [
        "error.pathSafety.tier1Blocked",
        "error.pathSafety.tier2Blocked",
        "error.gameService.pathOverlap",
        "error.consentMarker.missing",
        "error.consentMarker.conflict",
        "error.path.missing",
    ];

    [RelayCommand]
    private void DismissFormError() => FormError = null;

    private void ClearErrors()
    {
        NameError = null;
        InstallError = null;
        SavesError = null;
        LaunchError = null;
        ExePathError = null;
        UriError = null;
        SteamAppIdError = null;
        InstallCard = null;
        SavesCard = null;
        FormError = null;
    }

    private void ShowIssues(IEnumerable<ValidationIssue> issues, Field? forced = null)
    {
        var unplaced = new List<string>();
        foreach (var issue in issues)
        {
            var message = _text.Message(issue.MessageId);
            var field = forced ?? FieldOf(issue.Path);
            if (field is null)
            {
                unplaced.Add(message);
            }
            else
            {
                SetFieldError(field.Value, message);
            }
        }

        if (unplaced.Count > 0)
        {
            FormError = ErrorCardViewModel.Custom(
                _text,
                Icons.Warning,
                ErrorTone.Warning,
                _text.Message("errorCard.title.checkFields"),
                string.Join(' ', unplaced),
                actions: [DismissAction()]);
        }
    }

    private static Field? FieldOf(string issuePath) => issuePath switch
    {
        "name" => Field.Name,
        "install.path" => Field.Install,
        "external.path" or "external.role" => Field.Saves,
        "launch.method" => Field.Launch,
        "launch.exePath" => Field.ExePath,
        "launch.uri" => Field.Uri,
        "steamAppId" => Field.SteamAppId,
        _ => null,
    };

    // The first problem on a field is the one shown.
    private void SetFieldError(Field field, string message)
    {
        switch (field)
        {
            case Field.Name:
                NameError ??= message;
                break;
            case Field.Install:
                InstallError ??= message;
                break;
            case Field.Saves:
                SavesError ??= message;
                break;
            case Field.Launch:
                LaunchError ??= message;
                break;
            case Field.ExePath:
                ExePathError ??= message;
                break;
            case Field.SteamAppId:
                SteamAppIdError ??= message;
                break;
            default:
                UriError ??= message;
                break;
        }
    }

    private void ShowResolveFailure(RepoRole role, ErrorInfo error)
    {
        if (IssuesOf(error) is { } issues)
        {
            ShowIssues(issues, role == RepoRole.Install ? Field.Install : Field.Saves);
        }
        else
        {
            ShowFormError(error);
        }
    }

    private void ShowSaveError(ErrorInfo error, IReadOnlyDictionary<RepoRole, string> resolved)
    {
        if (IssuesOf(error) is { } issues)
        {
            ShowIssues(issues);
        }
        else if (_folderErrorIds.Contains(error.MessageId) && FolderRoleOf(error, resolved) is { } role)
        {
            if (error.MessageId == "error.consentMarker.missing")
            {
                _unconfirmed.Add(role);
            }

            // A folder that is gone can be replaced right from the card.
            var card = new ErrorCardViewModel(error, _text, error.MessageId == "error.path.missing" ? [ChooseFolderAction(role)] : null);
            if (role == RepoRole.Install)
            {
                InstallCard = card;
            }
            else
            {
                SavesCard = card;
            }
        }
        else
        {
            ShowFormError(error);
        }
    }

    private void ShowFormError(ErrorInfo error) =>
        FormError = new ErrorCardViewModel(error, _text, [DismissAction()]);

    private ErrorCardAction DismissAction() => new(_text.Message("common.dismiss"), DismissFormErrorCommand);

    private static IReadOnlyList<ValidationIssue>? IssuesOf(ErrorInfo error) =>
        error.MessageId == "error.validation.invalidPayload" && error.Args.TryGetValue("issues", out var issues) && issues is IEnumerable<ValidationIssue> list
            ? [.. list]
            : null;

    // The Api names the folder by role or by the path it refused; an error naming neither goes above the form, not under a field it may not be about.
    private static RepoRole? FolderRoleOf(ErrorInfo error, IReadOnlyDictionary<RepoRole, string> resolved)
    {
        if (error.Args.TryGetValue("role", out var role) && RoleOf(role) is { } named)
        {
            return named;
        }

        if (error.Args.TryGetValue("path", out var path) && path is string refused)
        {
            foreach (var (candidate, resolvedPath) in resolved)
            {
                if (string.Equals(resolvedPath, refused, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static RepoRole? RoleOf(object? value) => value switch
    {
        RepoRole role => role,
        "install" => RepoRole.Install,
        "external" => RepoRole.External,
        _ => null,
    };
}
