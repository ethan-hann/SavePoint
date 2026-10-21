using System.Runtime.InteropServices;

namespace Savepoint.App.Platform.Notifications;

/// <summary>What a call to <c>Shell_NotifyIconW</c> does to the notification area icon.</summary>
internal enum NotifyIconCommand : uint
{
    /// <summary>Creates the icon (<c>NIM_ADD</c>).</summary>
    Add = 0,

    /// <summary>Changes the icon, or shows its balloon (<c>NIM_MODIFY</c>).</summary>
    Modify = 1,

    /// <summary>Removes the icon (<c>NIM_DELETE</c>).</summary>
    Delete = 2,
}

/// <summary>One request to the shell about the notification area icon, before it is turned into the native structure.</summary>
/// <param name="Command">What to do with the icon.</param>
/// <param name="WindowHandle">The window the icon belongs to; with <paramref name="Id"/> it names the icon.</param>
/// <param name="Id">The icon's id within that window.</param>
/// <param name="Icon">The icon handle to show; zero for none.</param>
/// <param name="Tip">The hover text, or null to leave it alone.</param>
/// <param name="InfoTitle">The notification heading, or null when the request carries no notification.</param>
/// <param name="InfoBody">The notification message, or null when the request carries no notification.</param>
internal sealed record NotifyIconRequest(
    NotifyIconCommand Command,
    nint WindowHandle,
    uint Id,
    nint Icon,
    string? Tip,
    string? InfoTitle,
    string? InfoBody);

/// <summary>The Windows <c>NOTIFYICONDATAW</c> structure, in the Vista and later layout.</summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct NotifyIconData
{
    public const int TipCapacity = 128;
    public const int InfoCapacity = 256;
    public const int InfoTitleCapacity = 64;

    public const uint IconFlag = 0x2;
    public const uint TipFlag = 0x4;
    public const uint InfoFlag = 0x10;
    public const uint UserIconInfoFlag = 0x4;

    public uint Size;
    public nint WindowHandle;
    public uint Id;
    public uint Flags;
    public uint CallbackMessage;
    public nint Icon;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = TipCapacity)]
    public string Tip;
    public uint State;
    public uint StateMask;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = InfoCapacity)]
    public string Info;
    public uint Version;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = InfoTitleCapacity)]
    public string InfoTitle;
    public uint InfoFlags;
    public Guid ItemGuid;
    public nint BalloonIcon;

    /// <summary>Fills the structure for a request; text longer than its field is cut so the native call is never handed too much.</summary>
    public static NotifyIconData Create(NotifyIconRequest request)
    {
        var hasInfo = request.InfoTitle is not null || request.InfoBody is not null;
        var flags = 0u;
        if (request.Command != NotifyIconCommand.Delete)
        {
            flags |= (request.Icon != 0 ? IconFlag : 0) | (request.Tip is not null ? TipFlag : 0) | (hasInfo ? InfoFlag : 0);
        }

        return new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            WindowHandle = request.WindowHandle,
            Id = request.Id,
            Flags = flags,
            Icon = request.Icon,
            Tip = Fit(request.Tip ?? string.Empty, TipCapacity),
            Info = Fit(request.InfoBody ?? string.Empty, InfoCapacity),
            InfoTitle = Fit(request.InfoTitle ?? string.Empty, InfoTitleCapacity),
            // The icon the shell shows beside the balloon text is the one given for the tray icon.
            InfoFlags = hasInfo && request.Icon != 0 ? UserIconInfoFlag : 0,
        };
    }

    /// <summary>Cuts text to what fits in a field of <paramref name="capacity"/> characters including the terminator, never splitting a surrogate pair.</summary>
    public static string Fit(string text, int capacity)
    {
        var room = capacity - 1;
        if (text.Length <= room)
        {
            return text;
        }

        return text[..(char.IsHighSurrogate(text[room - 1]) ? room - 1 : room)];
    }
}
