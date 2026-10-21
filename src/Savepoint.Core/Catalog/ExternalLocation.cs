namespace Savepoint.Core.Catalog;

/// <summary>A game's second tracked folder, outside the install, such as the save folder under Documents.</summary>
/// <param name="Path">
/// The folder as the user wrote it; it may start with a path token such as <c>{documents}</c>. Required, and it must not be the
/// same as or inside the install folder, or contain it.
/// </param>
/// <param name="Role">What the folder holds; always <see cref="ExternalRole.Saves"/> today.</param>
public sealed record ExternalLocation(string Path = "", ExternalRole Role = ExternalRole.Saves);
