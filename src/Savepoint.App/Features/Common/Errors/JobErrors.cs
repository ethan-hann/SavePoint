using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Features.Common.Errors;

/// <summary>Reads the errors out of a finished job's result, where a job that finished only in part keeps them.</summary>
public static class JobErrors
{
    /// <summary>The errors of the folders (or files) a job could not finish, in the order the result lists them; none for a result that has no failures.</summary>
    /// <param name="result">The job's result, or null.</param>
    public static IEnumerable<ErrorInfo> FolderErrors(JobResult? result) => result switch
    {
        CaptureResult capture => capture.Folders.Where(folder => !folder.Ok).Select(folder => folder.Error!),
        OptimizeResult optimize => optimize.Games.SelectMany(game => game.Folders).Where(folder => !folder.Ok).Select(folder => folder.Error!),
        ClearAllDataResult clear => clear.NotRemoved.Select(failure => failure.Error),
        StorageRelocationResult moved => moved.NotRemoved.Select(failure => failure.Error),
        _ => [],
    };
}
