using System.Globalization;
using Savepoint.Core.Api;
using Savepoint.Core.Artwork;
using Savepoint.Core.Platform;

namespace Savepoint.Core.CloudSaves;

/// <summary>
/// Reads Steam's per-game cloud switch from the signed-in account's <c>sharedconfig.vdf</c>. The account is the most recently used one
/// in <c>config/loginusers.vdf</c>; without that, the only account folder under <c>userdata</c>. Only reads Steam's files.
/// </summary>
internal sealed class SteamCloudReader(ISteamFolderLocator steam) : IStoreCloudReader
{
    // The app list of a large library can be bigger than the key-value reader accepts; that reads as unknown, not as off.
    private const long _fileLimit = ValveKeyValues.MaxChars;

    // The low 32 bits of a 64-bit Steam id are the account id that names the userdata folder.
    private const long _accountMask = 0xFFFFFFFFL;

    public CloudSaveState Read(string storeGameId)
    {
        if (storeGameId.Length == 0 || !storeGameId.All(char.IsAsciiDigit))
        {
            return CloudSaveState.Unknown;
        }

        try
        {
            if (steam.FindSteamFolder() is not { } folder || ChooseAccount(folder) is not { } account)
            {
                return CloudSaveState.Unknown;
            }

            var file = Path.Combine(folder, "userdata", account, "7", "remote", "sharedconfig.vdf");
            var root = ValveKeyValues.ParseRoot(ReadLimited(file), "UserRoamingConfigStore");
            var value = root?.Child("Software")?.Child("Valve")?.Child("Steam")?.Child("apps")?.Child(storeGameId)?.TextOf("cloudenabled");
            return value switch
            {
                "1" => CloudSaveState.On,
                "0" => CloudSaveState.Off,
                _ => CloudSaveState.Unknown,
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return CloudSaveState.Unknown;
        }
    }

    private static string? ChooseAccount(string steamFolder)
    {
        if (MostRecentAccount(steamFolder) is { } recent)
        {
            return recent;
        }

        // Account 0 is Steam's placeholder for no sign-in; it never holds a library's settings.
        var accounts = Directory.Exists(Path.Combine(steamFolder, "userdata"))
            ? Directory.EnumerateDirectories(Path.Combine(steamFolder, "userdata"))
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(name => name != "0" && name.Length > 0 && name.All(char.IsAsciiDigit))
                .Take(2)
                .ToList()
            : [];
        return accounts.Count == 1 ? accounts[0] : null;
    }

    private static string? MostRecentAccount(string steamFolder)
    {
        var users = ValveKeyValues.ParseRoot(ReadLimited(Path.Combine(steamFolder, "config", "loginusers.vdf")), "users");
        var recent = users?.Children?.FirstOrDefault(pair => pair.Value.TextOf("MostRecent") == "1").Key;
        return long.TryParse(recent, NumberStyles.None, CultureInfo.InvariantCulture, out var steamId) && steamId > 0
            ? (steamId & _accountMask).ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static string? ReadLimited(string path)
    {
        var info = new FileInfo(path);
        return info.Exists && info.Length <= _fileLimit ? File.ReadAllText(path) : null;
    }
}
