namespace Savepoint.Core.Library;

/// <summary>What GOG's installer recorded about one installed product.</summary>
/// <param name="Id">The GOG product id, as text.</param>
/// <param name="Name">The product's name.</param>
/// <param name="Path">The install folder.</param>
/// <param name="Exe">The full path of the program that starts the game, or null when none was recorded.</param>
/// <param name="ExeFile">The program's file name, or null when none was recorded.</param>
/// <param name="DependsOn">The product id of the game this one is an add-on to, or null for a game of its own.</param>
public sealed record GogInstallRecord(string Id, string Name, string Path, string? Exe = null, string? ExeFile = null, string? DependsOn = null);

/// <summary>Reads what GOG recorded about installed products on this computer.</summary>
public interface IGogInstalls
{
    /// <summary>Reads the install records; never throws.</summary>
    /// <returns>The records, or null when GOG's records are not available on this computer.</returns>
    IReadOnlyList<GogInstallRecord>? ReadInstalls();
}
