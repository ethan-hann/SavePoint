using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;

namespace Savepoint.Core.Services.Profiles;

// Both folders report into one bar: files and bytes add up, and a total stays unknown until every folder has given its own.
internal sealed class CaptureProgressCombiner(IProgress<JobProgress> sink, Action<JobLine>? addLine = null)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<RepoRole, FileProgress> _parts = [];
    private int _expected;

    public Part For(RepoRole role)
    {
        lock (_gate)
        {
            _expected++;
        }

        return new Part(this, role);
    }

    private int DoneOf(RepoRole role)
    {
        lock (_gate)
        {
            return _parts.TryGetValue(role, out var part) ? part.FilesDone : 0;
        }
    }

    private void Update(RepoRole role, FileProgress value, string? path)
    {
        lock (_gate)
        {
            _parts[role] = value;
            AddLines(role, value);
            var parts = _parts.Values;
            var counted = parts.Count == _expected;
            var filesTotal = counted && parts.All(part => part.FilesTotal is not null) ? parts.Sum(part => part.FilesTotal!.Value) : (int?)null;
            var bytesTotal = counted && parts.All(part => part.BytesTotal is not null) ? parts.Sum(part => part.BytesTotal!.Value) : (long?)null;
            var bytesDone = parts.Any(part => part.BytesDone is not null) ? parts.Sum(part => part.BytesDone ?? 0) : (long?)null;

            // Only when no folder is still storing: a folder that is wrapping up must not hide another that has bytes to go.
            var finishing = counted && parts.Any(part => part.Finishing) && parts.All(part => part.Finishing || (part.BytesTotal is { } total && part.BytesDone >= total));
            sink.Report(new JobProgress(parts.Sum(part => part.FilesDone), filesTotal, bytesDone, bytesTotal, path, finishing));
        }
    }

    // The caller holds the gate. The names come first so the progress that follows, which shows the last of them, finds its line already there.
    private void AddLines(RepoRole role, FileProgress value)
    {
        if (addLine is null)
        {
            return;
        }

        foreach (var name in value.NewPaths ?? [])
        {
            addLine(JobLine.ForPath(name));
        }

        switch (value.Copying)
        {
            case { Path: { } path, Starting: true } file:
                addLine(JobLine.ForStep(JobSteps.CaptureFileStart, ("path", path), ("bytesTotal", file.BytesTotal)));
                break;
            case { Path: { } path } file:
                addLine(JobLine.ForStep(JobSteps.CaptureFileProgress, ("path", path), ("bytes", file.BytesDone), ("bytesTotal", file.BytesTotal)));
                break;
            case { } folder:
                addLine(JobLine.ForStep(JobSteps.CaptureFolderProgress, ("folder", role), ("bytes", folder.BytesDone), ("bytesTotal", folder.BytesTotal)));
                break;
        }
    }

    private void Finish(RepoRole role)
    {
        FileProgress last;
        lock (_gate)
        {
            last = _parts.GetValueOrDefault(role);
        }

        // Everything the folder was going to store is stored, whether or not git named each file; a folder with nothing to store is a complete part of zero.
        var files = last.FilesTotal ?? last.FilesDone;
        Update(role, new FileProgress(files, files, null, last.BytesTotal ?? 0, last.BytesTotal ?? 0), null);
    }

    public sealed class Part(CaptureProgressCombiner owner, RepoRole role) : IProgress<FileProgress>
    {
        // The files this folder has handled so far; git reports only the files it adds, changes or removes.
        public int FilesDone => owner.DoneOf(role);

        // The folder is done, however it ended; a folder that never reported no longer holds the total back.
        public void Finish() => owner.Finish(role);

        public void Report(FileProgress value) => owner.Update(role, value, value.CurrentPath);
    }
}
