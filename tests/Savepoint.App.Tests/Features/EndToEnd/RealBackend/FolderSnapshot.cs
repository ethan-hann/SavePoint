using System.Security.Cryptography;
using Savepoint.Core.Paths;

namespace Savepoint.App.Tests.Features.EndToEnd.RealBackend;

// What a folder holds, file by file: the relative path and a hash of the content, so a test can say a switch rewrote a folder byte for
// byte. SavePoint's own marker file is left out, since it is the one thing SavePoint adds to a game's folders.
internal static class FolderSnapshot
{
    public static SortedDictionary<string, string> Of(string folder)
    {
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(folder))
        {
            return files;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
            if (relative.StartsWith(ConsentMarker.FileName, StringComparison.Ordinal))
            {
                continue;
            }

            files[relative] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        }

        return files;
    }
}
