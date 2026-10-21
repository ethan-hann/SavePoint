namespace Savepoint.Core.Paths;

/// <summary>Catalog keys for the validation issues <see cref="PathTokenResolver"/> reports; the UI turns each into a sentence.</summary>
public static class PathValidationIds
{
    /// <summary>The path starts with a {token} this OS does not define.</summary>
    public const string UnknownToken = "validation.path.unknownToken";

    /// <summary>The path is relative, or names no drive or share.</summary>
    public const string NotAbsolute = "validation.path.notAbsolute";

    /// <summary>A Windows path has a colon after the drive letter: NTFS stream syntax, which can name a folder under another spelling.</summary>
    public const string InvalidCharacter = "validation.path.invalidCharacter";
}
