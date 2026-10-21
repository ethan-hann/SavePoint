using System.Runtime.InteropServices;

namespace Savepoint.App.Platform.Motion;

/// <summary>Reads Windows' "Show animations in Windows" switch.</summary>
internal static class WindowsAnimationSetting
{
    private const uint _getClientAreaAnimation = 0x1042;

    /// <summary>True when animations are on, false when the user turned them off, null when the call fails or this is not Windows.</summary>
    public static bool? Read()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var enabled = 0;
        return SystemParametersInfoW(_getClientAreaAnimation, 0, ref enabled, 0) ? enabled != 0 : null;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(uint action, uint parameter, ref int value, uint flags);
}
