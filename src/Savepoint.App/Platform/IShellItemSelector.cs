namespace Savepoint.App.Platform;

/// <summary>Opens an Explorer window with one file selected, behind a seam so tests never open a window.</summary>
internal interface IShellItemSelector
{
    /// <summary>Opens the file's folder in Explorer with the file selected, without going through a command line.</summary>
    /// <param name="fullPath">The absolute path of an existing file.</param>
    /// <returns>False when the shell could not do it, so the caller can fall back to starting Explorer.</returns>
    bool Select(string fullPath);
}
