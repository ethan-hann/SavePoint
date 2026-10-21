namespace Savepoint.Core.Launch;

/// <summary>The file-system questions a launcher asks before it starts anything; a seam so every OS's rules can be tested anywhere.</summary>
internal interface ILaunchFiles
{
    /// <summary>True when a regular file exists at <paramref name="path"/>.</summary>
    /// <param name="path">An absolute path.</param>
    bool FileExists(string path);

    /// <summary>True when a folder exists at <paramref name="path"/>.</summary>
    /// <param name="path">An absolute path.</param>
    bool DirectoryExists(string path);

    /// <summary>True when the file carries an execute permission for anyone; false when none is set.</summary>
    /// <param name="path">An absolute path to an existing file.</param>
    bool HasExecuteBit(string path);
}
