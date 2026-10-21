using System.Runtime.InteropServices;

namespace Savepoint.App.Platform;

/// <summary>Selects a file in Explorer with <c>SHOpenFolderAndSelectItems</c>, which takes the path as data instead of a command line.</summary>
internal sealed class NativeShellItemSelector : IShellItemSelector
{
    private const uint _apartmentThreaded = 0x2;
    private const uint _disableOle1Dde = 0x4;

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(15);

    /// <inheritdoc />
    public bool Select(string fullPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        // COM wants an apartment thread, and the caller's may be neither set up for it nor free to wait on the shell.
        var succeeded = false;
        var thread = new Thread(() => succeeded = SelectOnThisThread(fullPath)) { IsBackground = true, Name = "SavePoint reveal" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread.Join(_timeout) && succeeded;
    }

    /// <summary>Turns a path into the shell's item id, which the caller frees with <see cref="Free"/>; zero when the shell does not know the path.</summary>
    internal static nint Parse(string fullPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return 0;
        }

        return SHParseDisplayName(fullPath, 0, out var itemId, 0, out _) >= 0 ? itemId : 0;
    }

    internal static void Free(nint itemId)
    {
        if (itemId != 0)
        {
            CoTaskMemFree(itemId);
        }
    }

    private static bool SelectOnThisThread(string fullPath)
    {
        var com = CoInitializeEx(0, _apartmentThreaded | _disableOle1Dde);
        try
        {
            var itemId = Parse(fullPath);
            if (itemId == 0)
            {
                return false;
            }

            try
            {
                // With no children listed, the item itself is the one to select in its parent folder.
                return SHOpenFolderAndSelectItems(itemId, 0, 0, 0) >= 0;
            }
            finally
            {
                Free(itemId);
            }
        }
        finally
        {
            // Both S_OK and S_FALSE count as an initialization that has to be undone.
            if (com >= 0)
            {
                CoUninitialize();
            }
        }
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(nint reserved, uint options);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoUninitialize();

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoTaskMemFree(nint memory);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHParseDisplayName(string name, nint bindContext, out nint itemId, uint attributesIn, out uint attributesOut);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHOpenFolderAndSelectItems(nint folderItemId, uint count, nint childItemIds, uint flags);
}
