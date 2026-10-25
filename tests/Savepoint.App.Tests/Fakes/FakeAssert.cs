using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Fakes;

// Reads that must succeed fail as assertions with the error id, not as a null dereference later on.
internal static class FakeAssert
{
    public static T Ok<T>(OperationResult<T> result)
    {
        Assert.True(result.Ok, $"Expected success but got {result.Error?.MessageId}.");
        return result.Value;
    }

    public static JobSnapshot Job(FakeBackend backend, Guid jobId)
    {
        var job = backend.Jobs.Get(jobId);
        Assert.NotNull(job);
        return job;
    }

    public static LaunchSessionInfo Session(FakeBackend backend, string gameId = SampleScenario.SkyrimId)
    {
        var session = backend.Launch.GetSession(gameId);
        Assert.NotNull(session);
        return session;
    }
}
