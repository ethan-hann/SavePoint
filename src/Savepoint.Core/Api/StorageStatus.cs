using Savepoint.Core.Errors;

namespace Savepoint.Core.Api;

/// <summary>
/// Whether the folder where SavePoint keeps its saved history can be reached. A folder that is gone for good (a drive that died or
/// got a new letter) is a finding the startup screen offers a way out of, like an unavailable git, not a failed check (D37).
/// </summary>
/// <param name="Reachable">True when the storage folder is there, or was made because this is the first run.</param>
/// <param name="Root">The storage folder SavePoint tried to reach, so the screen can name it.</param>
/// <param name="Error">Why it cannot be reached, normally <c>error.storage.unreachable</c>; null when <paramref name="Reachable"/> is true.</param>
/// <param name="InInstallerFolder">True when the storage folder lies inside the folder the Windows installer owns, which installing or removing SavePoint empties; always false elsewhere.</param>
public sealed record StorageStatus(bool Reachable, string Root, ErrorInfo? Error, bool InInstallerFolder = false);
