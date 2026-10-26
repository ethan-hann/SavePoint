namespace Savepoint.Core.Paths;

/// <summary>
/// A folder as it was when a check approved it: where the path led once every link was followed, and which folder that is by identity
/// where the OS reports one. <see cref="PathSafety.IsStillPinned"/> tells whether a path still leads to the same folder.
/// </summary>
/// <param name="RealPath">The path with every symlink and junction followed.</param>
/// <param name="Identity">The folder's identity; null where the OS reports none (only Windows does) or the folder cannot be opened.</param>
public sealed record FolderPin(string RealPath, FileIdentity? Identity);
