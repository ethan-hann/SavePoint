using System.Runtime.Versioning;
using System.Security;
using Savepoint.Core.Library;

namespace Savepoint.Core.Platform.Windows;

/// <summary>Reads what GOG's installer recorded for each installed product, under <c>HKLM\SOFTWARE\WOW6432Node\GOG.com\Games</c>. Read-only.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsGogRegistry : IGogInstalls
{
    private const string _gamesKey = @"SOFTWARE\WOW6432Node\GOG.com\Games";

    /// <inheritdoc />
    public IReadOnlyList<GogInstallRecord>? ReadInstalls()
    {
        try
        {
            using var games = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(_gamesKey);
            if (games is null)
            {
                return null;
            }

            var records = new List<GogInstallRecord>();
            foreach (var name in games.GetSubKeyNames())
            {
                using var product = games.OpenSubKey(name);
                if (product is null)
                {
                    continue;
                }

                var path = Text(product, "path");
                if (path is null)
                {
                    continue;
                }

                records.Add(new GogInstallRecord(Text(product, "gameID") ?? name, Text(product, "gameName") ?? string.Empty, path, Text(product, "exe"), Text(product, "exeFile"), Text(product, "dependsOn")));
            }

            return records;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }

    private static string? Text(Microsoft.Win32.RegistryKey key, string name) =>
        key.GetValue(name) is string { Length: > 0 } value ? value : null;
}
