namespace Savepoint.Core.Catalog;

/// <summary>
/// A game's third tracked folder: the one outside the install that holds its mods, such as a mods folder under AppData. Games that
/// keep their mods inside the install folder do not need one; the install folder's snapshots already cover those.
/// </summary>
/// <param name="Path">
/// The folder as the user wrote it; it may start with a path token such as <c>{appData}</c>. Required, and it must not be the same
/// as or inside the install folder or the saves folder, or contain either of them.
/// </param>
// A positional record, like the other locations, because source-generated JSON overwrites init-only property defaults with null.
public sealed record ModsLocation(string Path = "");
