namespace Savepoint.Core.Catalog;

/// <summary>A game's install folder, whose program files and mods are tracked.</summary>
/// <param name="Path">The folder as the user wrote it; it may start with a path token such as <c>{userHome}</c>. Required.</param>
// Positional records, because source-generated JSON overwrites init-only property defaults with null when a key is absent.
public sealed record InstallLocation(string Path = "");
