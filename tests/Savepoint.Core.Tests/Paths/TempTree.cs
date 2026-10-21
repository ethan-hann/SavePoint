using System.Diagnostics;
using Savepoint.Core.Paths;

namespace Savepoint.Core.Tests.Paths;

/// <summary>A unique temp folder for one test; links made through it are removed before the tree is.</summary>
internal sealed class TempTree : IDisposable
{
    private readonly List<string> _links = [];

    public TempTree()
    {
        var raw = Path.Combine(Path.GetTempPath(), "savepoint-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(raw);

        // Canonical so expected paths match even when the temp folder itself sits behind a link.
        Root = RealPath.Resolve(raw);
    }

    public string Root { get; }

    public string Dir(params string[] relative)
    {
        var path = Path.Combine([Root, .. relative]);
        Directory.CreateDirectory(path);
        return path;
    }

    public string File(string relative, string content = "x")
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    /// <summary>A junction on Windows (no privilege needed), a symlink elsewhere. Skips the test when neither can be made.</summary>
    public string DirectoryLink(string relativeLink, string target)
    {
        var link = Path.Combine(Root, relativeLink);
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        if (OperatingSystem.IsWindows())
        {
            MakeJunction(link, target);
        }
        else
        {
            Directory.CreateSymbolicLink(link, target);
        }

        _links.Add(link);
        return link;
    }

    /// <summary>A real symlink with the given target text (may be relative). Skips the test without symlink rights.</summary>
    public string Symlink(string relativeLink, string targetText, bool isDirectory)
    {
        var link = Path.Combine(Root, relativeLink);
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        try
        {
            if (isDirectory)
            {
                Directory.CreateSymbolicLink(link, targetText);
            }
            else
            {
                System.IO.File.CreateSymbolicLink(link, targetText);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"Symbolic links cannot be created here: {ex.Message}");
        }

        _links.Add(link);
        return link;
    }

    public void Dispose()
    {
        foreach (var link in _links)
        {
            try
            {
                // Attributes, not Exists: a dangling directory link reports Exists as false but still needs Directory.Delete.
                if ((System.IO.File.GetAttributes(link) & FileAttributes.Directory) != 0)
                {
                    Directory.Delete(link, recursive: false);
                }
                else
                {
                    System.IO.File.Delete(link);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort; the tree delete below reports anything left behind.
            }
        }

        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A locked temp file must not fail the test that already passed.
        }
    }

    private static void MakeJunction(string link, string target)
    {
        var info = new ProcessStartInfo("cmd.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add("/c");
        info.ArgumentList.Add("mklink");
        info.ArgumentList.Add("/J");
        info.ArgumentList.Add(link);
        info.ArgumentList.Add(target);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            Assert.Skip($"mklink /J failed: {output}");
        }
    }
}
