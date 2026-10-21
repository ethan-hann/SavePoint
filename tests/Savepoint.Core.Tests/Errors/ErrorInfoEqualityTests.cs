using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Tests.Errors;

public class ErrorInfoEqualityTests
{
    private static ErrorInfo Info(string id, string detail, params (string Key, object? Value)[] args) =>
        new(id, args.ToDictionary(arg => arg.Key, arg => arg.Value), detail);

    [Fact]
    public void Two_errors_with_the_same_id_detail_and_arg_values_are_equal_whatever_dictionary_holds_them()
    {
        var first = Info("error.path.missing", "detail", ("path", "/games"), ("role", "install"));
        var second = Info("error.path.missing", "detail", ("role", "install"), ("path", "/games"));

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void A_different_arg_value_makes_them_different()
    {
        Assert.NotEqual(Info("id", "d", ("path", "/a")), Info("id", "d", ("path", "/b")));
    }

    [Fact]
    public void A_missing_arg_makes_them_different()
    {
        Assert.NotEqual(Info("id", "d", ("path", "/a")), Info("id", "d"));
        Assert.NotEqual(Info("id", "d"), Info("id", "d", ("path", "/a")));
    }

    [Fact]
    public void A_different_id_or_detail_makes_them_different()
    {
        Assert.NotEqual(Info("one", "d"), Info("two", "d"));
        Assert.NotEqual(Info("id", "one"), Info("id", "two"));
    }

    [Fact]
    public void List_args_compare_by_their_contents()
    {
        var first = Info("id", "d", ("roles", new List<string> { "install", "external" }));
        var second = Info("id", "d", ("roles", new[] { "install", "external" }));

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, Info("id", "d", ("roles", new[] { "install" })));
    }

    [Fact]
    public void Lists_of_validation_issues_compare_by_value()
    {
        var first = Info("id", "d", ("issues", new List<ValidationIssue> { new("name", ValidationMessageIds.GameNameRequired) }));
        var second = Info("id", "d", ("issues", new[] { new ValidationIssue("name", ValidationMessageIds.GameNameRequired) }));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Enum_args_compare_equal_to_the_names_they_become()
    {
        Assert.Equal(Info("id", "d", ("role", RepoRole.Install)), Info("id", "d", ("role", "install")));
    }

    [Fact]
    public void Null_args_are_values_too()
    {
        Assert.Equal(Info("id", "d", ("existingGameId", null)), Info("id", "d", ("existingGameId", null)));
        Assert.NotEqual(Info("id", "d", ("existingGameId", null)), Info("id", "d", ("existingGameId", "game-2")));
    }

    [Fact]
    public void Records_that_hold_an_error_compare_equal_when_the_errors_are()
    {
        var first = new SessionState.Completed(new SessionCompletion.Skip(), null, Info("error.path.missing", "d", ("path", "/g")));
        var second = new SessionState.Completed(new SessionCompletion.Skip(), null, Info("error.path.missing", "d", ("path", "/g")));

        Assert.Equal(first, second);
    }

    [Fact]
    public void A_failed_operation_result_compares_by_its_error()
    {
        var first = OperationResult.Failure<Unit>(Info("error.unknown", "d"));
        var second = OperationResult.Failure<Unit>(Info("error.unknown", "d"));

        Assert.Equal(first, second);
    }
}
