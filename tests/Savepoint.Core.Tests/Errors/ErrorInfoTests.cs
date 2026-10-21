using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Tests.Errors;

public class ErrorInfoTests
{
    [Fact]
    public void Consent_failure_enum_becomes_its_camel_case_name()
    {
        var missing = ErrorInfo.From(new ConsentMarkerException(ConsentFailure.Missing, "/games/alpha", RepoRole.Install, null, null));
        var conflict = ErrorInfo.From(new ConsentMarkerException(ConsentFailure.Conflict, "/games/alpha", RepoRole.Install, "game-2", RepoRole.External));

        Assert.Equal("missing", missing.Args["reason"]);
        Assert.Equal("conflict", conflict.Args["reason"]);
    }

    [Fact]
    public void Folder_roles_reach_the_ui_as_install_or_external()
    {
        Assert.Equal("external", ErrorInfo.From(new PathMissingException("/saves/alpha", RepoRole.External)).Args["role"]);
        Assert.Equal("install", ErrorInfo.From(new DirtyWorkingTreeException(RepoRole.Install)).Args["role"]);
        Assert.Equal("external", ErrorInfo.From(new DetachedCaptureNotSupportedException(RepoRole.External)).Args["role"]);
    }

    [Fact]
    public void Consent_marker_roles_reach_the_ui_as_install_or_external()
    {
        var conflict = ErrorInfo.From(new ConsentMarkerException(ConsentFailure.Conflict, "/games/alpha", RepoRole.Install, "game-2", RepoRole.External));
        var missing = ErrorInfo.From(new ConsentMarkerException(ConsentFailure.Missing, "/games/alpha", RepoRole.External, null, null));

        Assert.Equal("install", conflict.Args["role"]);
        Assert.Equal("external", conflict.Args["existingRole"]);
        Assert.Equal("external", missing.Args["role"]);
        Assert.Null(missing.Args["existingRole"]);
    }

    [Fact]
    public void Developer_messages_name_the_folder_role_in_lower_case()
    {
        Assert.Contains("install path", new PathMissingException("/games/alpha", RepoRole.Install).Message, StringComparison.Ordinal);
        Assert.Contains("external folder", new DirtyWorkingTreeException(RepoRole.External).Message, StringComparison.Ordinal);
        Assert.Contains("install folder", new DetachedCaptureNotSupportedException(RepoRole.Install).Message, StringComparison.Ordinal);
        Assert.Contains("external folder", new ConsentMarkerException(ConsentFailure.Missing, "/games/alpha", RepoRole.External, null, null).Message, StringComparison.Ordinal);
        Assert.Contains("as its external folder", new ConsentMarkerException(ConsentFailure.Conflict, "/games/alpha", RepoRole.Install, "game-2", RepoRole.External).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_first_letter_is_lowered()
    {
        var info = ErrorInfo.From(new OperationCancelledByUserException(CancelledOperation.StorageRelocation, "game-1"));

        Assert.Equal("storageRelocation", info.Args["op"]);
    }

    [Fact]
    public void Enum_args_given_straight_to_the_constructor_are_normalized_too()
    {
        var args = new Dictionary<string, object?>
        {
            ["role"] = RepoRole.External,
            ["method"] = LaunchMethod.Uri,
        };

        var info = new ErrorInfo("error.sample", args, "detail");

        Assert.Equal("external", info.Args["role"]);
        Assert.Equal("uri", info.Args["method"]);
    }

    [Fact]
    public void Enum_args_replaced_with_a_copy_are_normalized_too()
    {
        var info = new ErrorInfo("error.sample", new Dictionary<string, object?>(), "detail");

        var changed = info with { Args = new Dictionary<string, object?> { ["role"] = RepoRole.Install } };

        Assert.Equal("install", changed.Args["role"]);
    }

    [Fact]
    public void Values_that_are_not_enums_pass_through_unchanged()
    {
        var info = ErrorInfo.From(new DiskSpaceException("/games", 5_000L, 1_000L));

        Assert.Equal("/games", info.Args["path"]);
        Assert.Equal(5_000L, info.Args["requiredBytes"]);
        Assert.Equal(1_000L, info.Args["freeBytes"]);
    }

    [Fact]
    public void Null_values_stay_null()
    {
        var info = ErrorInfo.From(new GitOperationException(null, "checkout failed"));

        Assert.True(info.Args.ContainsKey("path"));
        Assert.Null(info.Args["path"]);
    }

    [Fact]
    public void A_list_stays_the_same_list()
    {
        IReadOnlyList<string> roles = ["install", "saves"];
        IReadOnlyList<ValidationIssue> issues = [new ValidationIssue("name", "validation.game.nameRequired")];

        var reconnect = ErrorInfo.From(new ReconnectableGameException("game-2", roles, ["main"]));
        var invalid = ErrorInfo.From(new ValidationException(issues));

        Assert.Same(roles, reconnect.Args["roles"]);
        Assert.Same(issues, invalid.Args["issues"]);
    }

    [Fact]
    public void Enums_inside_a_list_are_normalized_and_the_value_is_still_a_list()
    {
        var args = new Dictionary<string, object?> { ["roles"] = new[] { RepoRole.Install, RepoRole.External } };

        var info = new ErrorInfo("error.sample", args, "detail");

        var roles = Assert.IsAssignableFrom<IReadOnlyList<object?>>(info.Args["roles"]);
        Assert.Equal(["install", "external"], roles);
    }

    [Fact]
    public void The_exceptions_own_args_keep_their_enum_values()
    {
        var error = new ConsentMarkerException(ConsentFailure.Missing, "/games/alpha", RepoRole.Install, null, null);

        _ = ErrorInfo.From(error);

        Assert.Equal(ConsentFailure.Missing, error.Args["reason"]);
    }

    [Fact]
    public void Changing_the_source_dictionary_afterwards_does_not_change_the_info()
    {
        var source = new Dictionary<string, object?> { ["role"] = RepoRole.Install };
        var info = new ErrorInfo("error.sample", source, "detail");

        source["role"] = RepoRole.External;
        source["extra"] = 1;

        Assert.Equal("install", info.Args["role"]);
        Assert.False(info.Args.ContainsKey("extra"));
    }

    [Fact]
    public void An_exception_without_args_gives_empty_args()
    {
        var info = ErrorInfo.From(new AmendNotAvailableException());

        Assert.Empty(info.Args);
    }
}
