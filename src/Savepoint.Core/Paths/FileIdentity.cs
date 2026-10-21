namespace Savepoint.Core.Paths;

/// <summary>
/// Which file or folder a path reaches, whatever name led there: the serial number of the volume and the file's id on it.
/// Two paths with equal identities are the same folder (a SUBST drive, a mapped drive, or a junction under another name).
/// </summary>
/// <param name="VolumeSerial">The volume's serial number.</param>
/// <param name="FileId">The 128-bit file id on that volume (a 64-bit index in the high-order zeros on volumes that have no 128-bit id).</param>
public readonly record struct FileIdentity(ulong VolumeSerial, UInt128 FileId);
