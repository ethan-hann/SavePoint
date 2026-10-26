using System.Globalization;
using Savepoint.Core.Api;
using Savepoint.Core.Artwork;
using Savepoint.Core.Platform;

namespace Savepoint.Core.CloudSaves;

/// <summary>
/// Reads Steam's per-game cloud switch from the signed-in account's <c>sharedconfig.vdf</c>. Steam writes <c>cloudenabled</c> only for
/// games whose switch the user has touched and cloud is on by default, so a game without the key counts as on when Steam tracks cloud
/// files for it (<c>remotecache.vdf</c>). Only reads Steam's files.
/// </summary>
/// <param name="steam">Finds the Steam folder.</param>
/// <param name="activeUser">Reads the account id of the signed-in Steam user, or null for none; null when it cannot be known.</param>
internal sealed class SteamCloudReader(ISteamFolderLocator steam, Func<uint?>? activeUser = null) : IStoreCloudReader
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

            var userFolder = Path.Combine(folder, "userdata", account);
            var file = Path.Combine(userFolder, "7", "remote", "sharedconfig.vdf");
            var text = SharedFile.ReadText(file, _fileLimit);
            if (text is not null)
            {
                var root = ValveKeyValues.ParseRoot(text, "UserRoamingConfigStore");
                if (root is null)
                {
                    return CloudSaveState.Unknown;
                }

                var value = root.Child("Software")?.Child("Valve")?.Child("Steam")?.Child("apps")?.Child(storeGameId)?.TextOf("cloudenabled");
                if (value is "1" or "0")
                {
                    return value == "1" ? CloudSaveState.On : CloudSaveState.Off;
                }
            }

            // No switch was written, so cloud is on if Steam tracks files for the game.
            return File.Exists(Path.Combine(userFolder, storeGameId, "remotecache.vdf")) ? CloudSaveState.On : CloudSaveState.Unknown;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return CloudSaveState.Unknown;
        }
    }

    // The signed-in account, then what loginusers.vdf says (older clients flag MostRecent, newer ones AutoLogin and a timestamp),
    // then the only account folder there is.
    private string? ChooseAccount(string steamFolder)
    {
        var userdata = Path.Combine(steamFolder, "userdata");
        if (activeUser?.Invoke() is { } active and not 0 && Directory.Exists(Path.Combine(userdata, active.ToString(CultureInfo.InvariantCulture))))
        {
            return active.ToString(CultureInfo.InvariantCulture);
        }

        return FromLoginUsers(steamFolder) ?? OnlyAccountFolder(userdata);
    }

    private static string? FromLoginUsers(string steamFolder)
    {
        var users = ValveKeyValues.ParseRoot(SharedFile.ReadText(Path.Combine(steamFolder, "config", "loginusers.vdf"), _fileLimit), "users")
            ?.Children?.Where(pair => pair.Value.Children is not null).ToList() ?? [];

        var chosen = users.FirstOrDefault(pair => pair.Value.TextOf("MostRecent") == "1");
        if (chosen.Key is null)
        {
            chosen = users.FirstOrDefault(pair => pair.Value.TextOf("AutoLogin") == "1");
        }

        if (chosen.Key is null && users.Count == 1)
        {
            chosen = users[0];
        }

        if (chosen.Key is null)
        {
            chosen = LargestTimestamp(users);
        }

        return chosen.Key is not null && long.TryParse(chosen.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var steamId) && steamId > 0
            ? (steamId & _accountMask).ToString(CultureInfo.InvariantCulture)
            : null;
    }

    // Two users with the same newest timestamp cannot be told apart.
    private static KeyValuePair<string, ValveValue> LargestTimestamp(List<KeyValuePair<string, ValveValue>> users)
    {
        var stamped = users
            .Select(pair => (Pair: pair, Stamp: long.TryParse(pair.Value.TextOf("timestamp"), NumberStyles.None, CultureInfo.InvariantCulture, out var stamp) ? stamp : (long?)null))
            .Where(item => item.Stamp is not null)
            .OrderByDescending(item => item.Stamp)
            .ToList();
        return stamped.Count > 0 && (stamped.Count == 1 || stamped[0].Stamp != stamped[1].Stamp) ? stamped[0].Pair : default;
    }

    // Account 0 is Steam's placeholder for no sign-in; it never holds a library's settings.
    private static string? OnlyAccountFolder(string userdata)
    {
        if (!Directory.Exists(userdata))
        {
            return null;
        }

        var accounts = Directory.EnumerateDirectories(userdata)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => name != "0" && name.Length > 0 && name.All(char.IsAsciiDigit))
            .Take(2)
            .ToList();
        return accounts.Count == 1 ? accounts[0] : null;
    }
}
