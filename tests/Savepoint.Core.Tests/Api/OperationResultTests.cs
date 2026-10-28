using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Tests.Api;

public class OperationResultTests
{
    private static readonly ErrorInfo _sampleError =
        new("error.diskSpace.low", new Dictionary<string, object?> { ["path"] = @"D:\SavePoint" }, "details");

    [Fact]
    public void Success_carries_the_value_and_no_error()
    {
        var result = OperationResult.Success("ready");

        Assert.True(result.Ok);
        Assert.Equal("ready", result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Success_without_a_value_carries_unit()
    {
        var result = OperationResult.Success();

        Assert.True(result.Ok);
        Assert.Equal(Unit.Value, result.Value);
    }

    [Fact]
    public void Success_refuses_a_null_value_so_ok_always_means_a_value_is_present()
    {
        Assert.Throws<ArgumentNullException>(() => OperationResult.Success<string>(null!));
    }

    [Fact]
    public void Failure_carries_the_error_and_no_value()
    {
        var result = OperationResult.Failure<string>(_sampleError);

        Assert.False(result.Ok);
        Assert.Same(_sampleError, result.Error);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Failure_refuses_a_null_error_so_a_failure_always_explains_itself()
    {
        Assert.Throws<ArgumentNullException>(() => OperationResult.Failure<string>((ErrorInfo)null!));
    }

    [Fact]
    public void Failure_from_a_known_exception_keeps_its_message_id_and_args()
    {
        var result = OperationResult.Failure<Unit>(new ProfileActiveException("modded-x8q2"));

        Assert.False(result.Ok);
        Assert.Equal("error.gameService.profileActive", result.Error.MessageId);
        Assert.Equal("modded-x8q2", result.Error.Args["profileId"]);
    }

    [Fact]
    public void Failure_from_an_unexpected_exception_maps_to_the_unknown_id()
    {
        var result = OperationResult.Failure<Unit>(new InvalidOperationException("boom"));

        Assert.Equal("error.unknown", result.Error!.MessageId);
        Assert.Contains("boom", result.Error.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Failure_from_a_null_exception_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => OperationResult.Failure<Unit>((Exception)null!));
    }
}
