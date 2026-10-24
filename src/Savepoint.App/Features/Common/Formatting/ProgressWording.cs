using Savepoint.Core.Api;

namespace Savepoint.App.Features.Common.Formatting;

/// <summary>The words every screen uses for how far a job has got, so a switch, a save, Activity and the wizard say the same thing.</summary>
public static class ProgressWording
{
    /// <summary>What to say while the job has not said how much there is to do.</summary>
    /// <param name="text">The formatter.</param>
    public static string Counting(this UiFormatter text) => text.Message("common.progress.counting");

    /// <summary>"1,200 of 3,430 files", with "file" when the total is one.</summary>
    /// <param name="text">The formatter.</param>
    /// <param name="done">Files finished.</param>
    /// <param name="total">Files in all.</param>
    public static string FilesOf(this UiFormatter text, int done, int total) =>
        text.Message("common.progress.filesOf", ("done", text.Number(done)), ("total", total), ("totalText", text.Number(total)));

    /// <summary>"3.1 GB of 70 GB".</summary>
    /// <param name="text">The formatter.</param>
    /// <param name="done">Bytes finished.</param>
    /// <param name="total">Bytes in all.</param>
    public static string BytesOf(this UiFormatter text, long done, long total) =>
        text.Message("common.progress.bytesOf", ("done", text.FileSize(done)), ("total", text.FileSize(total)));

    /// <summary>The bytes done of the total when the job reports bytes, otherwise the files done of the total; null while neither total is known.</summary>
    /// <param name="text">The formatter.</param>
    /// <param name="progress">What the job reported.</param>
    public static string? Describe(this UiFormatter text, JobProgress progress)
    {
        if (progress is { BytesTotal: > 0, BytesDone: { } bytesDone, BytesTotal: { } bytesTotal })
        {
            // A figure that has stopped looks like a hang; the job is only wrapping up.
            return progress.Finishing ? text.Message("common.progress.finishing") : text.BytesOf(bytesDone, bytesTotal);
        }

        return progress is { FilesTotal: > 0, FilesDone: { } filesDone, FilesTotal: { } filesTotal } ? text.FilesOf(filesDone, filesTotal) : null;
    }

    /// <summary>
    /// How far the job is, from 0 to 1: by bytes when it reports a byte total, otherwise by files; null while neither total is known.
    /// </summary>
    /// <param name="progress">What the job reported.</param>
    public static double? Fraction(JobProgress progress)
    {
        if (progress is { BytesTotal: > 0, BytesDone: { } bytesDone, BytesTotal: { } bytesTotal })
        {
            return Math.Clamp((double)bytesDone / bytesTotal, 0, 1);
        }

        return progress is { FilesTotal: > 0, FilesDone: { } filesDone, FilesTotal: { } filesTotal } ? Math.Clamp((double)filesDone / filesTotal, 0, 1) : null;
    }
}
