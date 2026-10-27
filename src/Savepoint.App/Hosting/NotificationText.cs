using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Hosting;

/// <summary>The words of an operating-system notification for a finished job: the app's name, and the same sentence the in-app toast says.</summary>
public static class NotificationText
{
    /// <summary>Formats the notification for a job that has finished.</summary>
    /// <param name="job">The finished job.</param>
    /// <param name="gameName">The name of the job's game, or null when it has none or is no longer in the library.</param>
    /// <param name="text">Formats the catalog messages.</param>
    /// <returns>The title and the body. A failed or partial job adds the first reason it gives.</returns>
    public static (string Title, string Body) For(JobSnapshot job, string? gameName, UiFormatter text)
    {
        var name = ("gameName", (object?)(gameName ?? text.Message("shell.jobStatus.thisGame")));
        var key = (job.Kind, job.Status) switch
        {
            (JobKind.RelocateStorage, JobStatus.Succeeded) => "shell.jobStatus.relocateSuccess",
            (JobKind.RelocateStorage, JobStatus.Cancelled) => "shell.jobStatus.relocateCancelled",
            (JobKind.RelocateStorage, JobStatus.Partial) => "shell.jobStatus.relocatePartial",
            (JobKind.RelocateStorage, _) => "shell.jobStatus.relocateFailure",

            (JobKind.Switch, JobStatus.Succeeded) => "shell.jobStatus.switchSuccess",
            (JobKind.Switch, JobStatus.Cancelled) => "shell.jobStatus.switchCancelled",
            (JobKind.Switch, _) => "shell.jobStatus.switchFailure",

            (JobKind.ClearData, JobStatus.Succeeded) => "shell.jobStatus.clearSuccess",
            (JobKind.ClearData, JobStatus.Partial) => "shell.jobStatus.clearPartial",
            (JobKind.ClearData, _) => "shell.jobStatus.clearFailure",

            (JobKind.Compact, JobStatus.Succeeded) when job.GameId is null => "shell.jobStatus.compactAllSuccess",
            (JobKind.Compact, JobStatus.Partial) when job.GameId is null => "shell.jobStatus.compactAllPartial",
            (JobKind.Compact, JobStatus.Cancelled) when JobErrors.WasStoppedForAnotherTask(job) && job.GameId is null => "shell.jobStatus.compactAllStopped",
            (JobKind.Compact, JobStatus.Cancelled) when JobErrors.WasStoppedForAnotherTask(job) => "shell.jobStatus.compactStopped",
            (JobKind.Compact, JobStatus.Cancelled) when job.GameId is null => "shell.jobStatus.compactAllCancelled",
            (JobKind.Compact, _) when job.GameId is null => "shell.jobStatus.compactAllFailure",
            (JobKind.Compact, JobStatus.Succeeded) => "shell.jobStatus.compactSuccess",
            (JobKind.Compact, JobStatus.Partial) => "shell.jobStatus.compactPartial",
            (JobKind.Compact, JobStatus.Cancelled) => "shell.jobStatus.compactCancelled",
            (JobKind.Compact, _) => "shell.jobStatus.compactFailure",

            (JobKind.PackStorage, JobStatus.Succeeded) => "shell.jobStatus.packSuccess",
            (JobKind.PackStorage, JobStatus.Partial) => "shell.jobStatus.packPartial",
            (JobKind.PackStorage, JobStatus.Cancelled) when JobErrors.WasStoppedForAnotherTask(job) => "shell.jobStatus.packStopped",
            (JobKind.PackStorage, JobStatus.Cancelled) => "shell.jobStatus.packCancelled",
            (JobKind.PackStorage, _) => "shell.jobStatus.packFailure",

            (_, JobStatus.Succeeded) => "shell.jobStatus.success",
            (_, JobStatus.Partial) => "shell.jobStatus.partial",
            (_, JobStatus.Cancelled) => "shell.jobStatus.cancelled",
            _ => "shell.jobStatus.failure",
        };

        var body = text.Message(key, [name]);
        if (job.Status is JobStatus.Failed or JobStatus.Partial && FirstReason(job) is { } reason)
        {
            body = $"{body} {text.Message(reason, ErrorOperations.Of(job.Kind))}";
        }

        return (text.Message("app.name"), body);
    }

    private static ErrorInfo? FirstReason(JobSnapshot job) =>
        job.Error ?? job.Result switch
        {
            CaptureResult capture => capture.Folders.FirstOrDefault(folder => !folder.Ok)?.Error,
            OptimizeResult optimize => optimize.Games.SelectMany(game => game.Folders).FirstOrDefault(folder => !folder.Ok)?.Error,
            ClearAllDataResult clear => clear.NotRemoved.FirstOrDefault()?.Error,
            StorageRelocationResult moved => moved.NotRemoved.FirstOrDefault()?.Error,
            _ => null,
        };
}
