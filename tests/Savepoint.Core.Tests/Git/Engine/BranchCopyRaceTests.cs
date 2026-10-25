using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A branch that appears after the copy's up-front check is refused by the ref transaction itself, and the record stays where it was.
[Collection(EngineCollections.E)]
public sealed class BranchCopyRaceTests : IDisposable
{
    private readonly EngineWorkspace _ws;
    private string? _appearingBranchFile;
    private string? _appearingTarget;

    public BranchCopyRaceTests()
    {
        _ws = new(options => options with
        {
            InvocationObserver = invocation =>
            {
                // Another writer creates the branch just before the transaction that would create it.
                if (_appearingBranchFile is not null && invocation.Arguments[0] == "update-ref" && invocation.Arguments.Contains("--stdin"))
                {
                    File.WriteAllText(_appearingBranchFile, _appearingTarget + "\n");
                }
            },
        });
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task A_branch_made_after_the_check_is_refused_by_the_transaction_and_the_record_does_not_move()
    {
        var (repo, points) = await _ws.ThreePointsRepoAsync();
        _ws.Write(repo, "a.txt", "played on");
        await _ws.Engine.CaptureAsync(repo, new CaptureRequest("profile-1", "Third point", Guid.NewGuid().ToString()) { Mode = CaptureMode.Amend }, ct: Ct);
        var record = Assert.Single(await _ws.Engine.ListRemovedAsync(repo, Ct));
        _appearingBranchFile = Path.Combine(repo.GitDir, "refs", "heads", "copied");
        _appearingTarget = points[0];

        var error = await Assert.ThrowsAsync<ProfileAlreadyExistsException>(() => _ws.Engine.CreateBranchAsCopyAsync(repo, "copied", points[2], "new-id", record, Ct));

        Assert.Equal("copied", error.Args["profileId"]);
        Assert.Equal(points[0], await _ws.TipAsync(repo, "copied"));
        Assert.Equal(record.RefName, Assert.Single(await _ws.Engine.ListRemovedAsync(repo, Ct)).RefName);
    }
}
