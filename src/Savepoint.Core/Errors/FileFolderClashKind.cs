namespace Savepoint.Core.Errors;

/// <summary>Which way a file and a folder share one path in a <see cref="FileFolderClashException"/>.</summary>
public enum FileFolderClashKind
{
    /// <summary>The point holds a file where the folder on disk still holds files that would be deleted with it.</summary>
    FolderHoldsFiles,

    /// <summary>The point holds a folder where the player has a file of their own that would be deleted to make room for it.</summary>
    FileInTheWay,
}
