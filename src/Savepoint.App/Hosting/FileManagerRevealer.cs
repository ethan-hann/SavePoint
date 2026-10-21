using Savepoint.App.Features.Settings;
using Savepoint.App.Platform;

namespace Savepoint.App.Hosting;

/// <summary>Lets Settings reveal a file through the platform's file manager, which passes every path as data and never through a command line.</summary>
public sealed class FileManagerRevealer : IFileRevealer
{
    private readonly IFileManager _fileManager;

    /// <summary>Creates the revealer.</summary>
    /// <param name="fileManager">The file manager of the running system, or a stand-in in tests.</param>
    public FileManagerRevealer(IFileManager fileManager)
    {
        _fileManager = fileManager;
    }

    /// <inheritdoc />
    public bool Reveal(string path) => _fileManager.Reveal(path);
}
