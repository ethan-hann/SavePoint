using System.Runtime.InteropServices;

namespace Savepoint.Core.Platform.Windows;

/// <summary>The shell finds folders OneDrive moved and is the only source for Downloads, Saved Games, and LocalLow.</summary>
public static class WindowsKnownFolders
{
    // Return the path without checking that it exists: a missing Saved Games folder is still one to protect.
    private const uint _dontVerify = 0x00004000;

    private static readonly Guid _downloads = new("374DE290-123F-4565-9164-39C4925E467B");
    private static readonly Guid _savedGames = new("4C5C32FF-BB9D-43B0-B5B4-2D72E54EAAA4");
    private static readonly Guid _localLow = new("A520A1A4-1780-4FF6-BD18-167343C5AF16");

    /// <summary>The folder's path, or null off Windows or when the shell has no answer.</summary>
    public static string? Get(KnownFolder folder)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return folder switch
        {
            KnownFolder.Documents => Special(Environment.SpecialFolder.MyDocuments),
            KnownFolder.Desktop => Special(Environment.SpecialFolder.DesktopDirectory),
            KnownFolder.RoamingAppData => Special(Environment.SpecialFolder.ApplicationData),
            KnownFolder.LocalAppData => Special(Environment.SpecialFolder.LocalApplicationData),
            KnownFolder.ProgramFiles => Special(Environment.SpecialFolder.ProgramFiles),
            KnownFolder.ProgramFilesX86 => Special(Environment.SpecialFolder.ProgramFilesX86),
            KnownFolder.ProgramData => Special(Environment.SpecialFolder.CommonApplicationData),
            KnownFolder.Windows => Special(Environment.SpecialFolder.Windows),
            KnownFolder.Downloads => FromShell(_downloads),
            KnownFolder.SavedGames => FromShell(_savedGames),
            KnownFolder.LocalLow => FromShell(_localLow),
            _ => null,
        };
    }

    private static string? Special(Environment.SpecialFolder folder)
    {
        var path = Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);
        return path.Length == 0 ? null : path;
    }

    private static string? FromShell(Guid id)
    {
        var result = SHGetKnownFolderPath(in id, _dontVerify, IntPtr.Zero, out var buffer);
        try
        {
            return result == 0 && buffer != IntPtr.Zero ? Marshal.PtrToStringUni(buffer) : null;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(buffer);
            }
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(in Guid folderId, uint flags, IntPtr token, out IntPtr path);
}
