namespace Savepoint.Core.Git;

/// <summary>
/// Answers how many bytes are free to an unprivileged user on the volume that holds a folder. The engine always
/// passes a folder that exists, so an implementation can hand it straight to the operating system. A probe that
/// throws is treated as a failed operation, not as "plenty of space".
/// </summary>
/// <param name="existingDirectory">A folder on the volume to measure.</param>
/// <returns>Free bytes on that volume.</returns>
public delegate long FreeSpaceProbe(string existingDirectory);
