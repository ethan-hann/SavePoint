using System.Security.Cryptography;
using Savepoint.Core.Paths;

namespace Savepoint.Core.Tests.Git.Engine;

// The content of a folder as {relative path: sha256}, so two states can be compared byte for byte.
internal static class TreeSnapshot
{
    public static SortedDictionary<string, string> Take(string folder)
    {
        var snapshot = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
            if (relative.StartsWith(ConsentMarker.FileName, StringComparison.Ordinal))
            {
                continue;
            }

            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            snapshot[relative] = Convert.ToHexString(SHA256.HashData(stream));
        }

        return snapshot;
    }
}
