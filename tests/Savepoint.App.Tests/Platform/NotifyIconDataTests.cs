using System.Runtime.InteropServices;
using Savepoint.App.Platform.Notifications;

namespace Savepoint.App.Tests.Platform;

public class NotifyIconDataTests
{
    private const nint _window = 0x1234;
    private const nint _icon = 0x77;

    private static NotifyIconRequest Request(
        NotifyIconCommand command,
        string? tip = null,
        string? title = null,
        string? body = null,
        nint icon = _icon) =>
        new(command, _window, 7, icon, tip, title, body);

    [Fact]
    public void The_structure_is_the_size_the_Windows_SDK_gives_NOTIFYICONDATAW()
    {
        var expected = IntPtr.Size == 8 ? 976 : 956;

        Assert.Equal(expected, Marshal.SizeOf<NotifyIconData>());
    }

    [Fact]
    public void Every_field_sits_at_the_offset_the_Windows_SDK_gives_it()
    {
        Assert.SkipUnless(IntPtr.Size == 8, "The offsets below are the 64-bit ones.");
        (string Field, int Offset)[] expected =
        [
            (nameof(NotifyIconData.Size), 0),
            (nameof(NotifyIconData.WindowHandle), 8),
            (nameof(NotifyIconData.Id), 16),
            (nameof(NotifyIconData.Flags), 20),
            (nameof(NotifyIconData.CallbackMessage), 24),
            (nameof(NotifyIconData.Icon), 32),
            (nameof(NotifyIconData.Tip), 40),
            (nameof(NotifyIconData.State), 296),
            (nameof(NotifyIconData.StateMask), 300),
            (nameof(NotifyIconData.Info), 304),
            (nameof(NotifyIconData.Version), 816),
            (nameof(NotifyIconData.InfoTitle), 820),
            (nameof(NotifyIconData.InfoFlags), 948),
            (nameof(NotifyIconData.ItemGuid), 952),
            (nameof(NotifyIconData.BalloonIcon), 968),
        ];

        var actual = expected.Select(field => (field.Field, (int)Marshal.OffsetOf<NotifyIconData>(field.Field))).ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void An_add_request_names_the_icon_and_gives_it_a_picture_and_hover_text()
    {
        var data = NotifyIconData.Create(Request(NotifyIconCommand.Add, tip: "SavePoint"));

        Assert.Equal((uint)Marshal.SizeOf<NotifyIconData>(), data.Size);
        Assert.Equal(_window, data.WindowHandle);
        Assert.Equal(7u, data.Id);
        Assert.Equal(_icon, data.Icon);
        Assert.Equal("SavePoint", data.Tip);
        Assert.Equal(NotifyIconData.IconFlag | NotifyIconData.TipFlag, data.Flags);
    }

    [Fact]
    public void A_notification_adds_the_info_flag_texts_and_asks_for_the_icon_as_the_balloon_picture()
    {
        var data = NotifyIconData.Create(Request(NotifyIconCommand.Modify, tip: "SavePoint", title: "Capture finished", body: "2 folders saved"));

        Assert.Equal(NotifyIconData.IconFlag | NotifyIconData.TipFlag | NotifyIconData.InfoFlag, data.Flags);
        Assert.Equal("Capture finished", data.InfoTitle);
        Assert.Equal("2 folders saved", data.Info);
        Assert.Equal(NotifyIconData.UserIconInfoFlag, data.InfoFlags);
    }

    [Fact]
    public void Without_an_icon_the_balloon_has_no_picture_flag()
    {
        var data = NotifyIconData.Create(Request(NotifyIconCommand.Modify, title: "Title", body: "Body", icon: 0));

        Assert.Equal(0u, data.InfoFlags);
        Assert.Equal(NotifyIconData.InfoFlag, data.Flags);
    }

    [Fact]
    public void A_request_without_a_notification_leaves_the_info_flag_off()
    {
        var data = NotifyIconData.Create(Request(NotifyIconCommand.Modify, tip: "SavePoint"));

        Assert.Equal(0u, data.Flags & NotifyIconData.InfoFlag);
    }

    [Fact]
    public void A_delete_request_carries_only_the_name_of_the_icon()
    {
        var data = NotifyIconData.Create(Request(NotifyIconCommand.Delete));

        Assert.Equal(_window, data.WindowHandle);
        Assert.Equal(7u, data.Id);
        Assert.Equal(0u, data.Flags);
    }

    [Fact]
    public void No_message_is_ever_posted_back_to_the_window_and_no_state_or_version_is_set()
    {
        var data = NotifyIconData.Create(Request(NotifyIconCommand.Modify, tip: "t", title: "a", body: "b"));

        Assert.Equal(0u, data.CallbackMessage);
        Assert.Equal(0u, data.State);
        Assert.Equal(0u, data.StateMask);
        Assert.Equal(0u, data.Version);
        Assert.Equal(Guid.Empty, data.ItemGuid);
        Assert.Equal(0, data.BalloonIcon);
    }

    [Fact]
    public void Text_longer_than_its_field_is_cut_to_leave_room_for_the_terminator()
    {
        var data = NotifyIconData.Create(Request(NotifyIconCommand.Modify, tip: new string('t', 500), title: new string('a', 500), body: new string('b', 500)));

        Assert.Equal(127, data.Tip.Length);
        Assert.Equal(63, data.InfoTitle.Length);
        Assert.Equal(255, data.Info.Length);
    }

    [Fact]
    public void Text_that_fits_is_kept_whole()
    {
        Assert.Equal(new string('x', 63), NotifyIconData.Fit(new string('x', 63), NotifyIconData.InfoTitleCapacity));
        Assert.Equal(new string('x', 63), NotifyIconData.Fit(new string('x', 64), NotifyIconData.InfoTitleCapacity));
        Assert.Equal(string.Empty, NotifyIconData.Fit(string.Empty, NotifyIconData.InfoTitleCapacity));
    }

    [Fact]
    public void A_surrogate_pair_is_never_split_by_the_cut()
    {
        // Character 63 would be the first half of the pair, which cannot stand alone.
        var text = new string('a', 62) + "\U0001F600" + "tail";

        var fitted = NotifyIconData.Fit(text, NotifyIconData.InfoTitleCapacity);

        Assert.Equal(new string('a', 62), fitted);
    }

    [Fact]
    public void A_pair_that_fits_completely_is_kept()
    {
        var text = new string('a', 61) + "\U0001F600" + "tail";

        var fitted = NotifyIconData.Fit(text, NotifyIconData.InfoTitleCapacity);

        Assert.Equal(new string('a', 61) + "\U0001F600", fitted);
    }

    [Fact]
    public void A_missing_tip_becomes_empty_text_not_a_null_the_marshaler_would_trip_over()
    {
        var data = NotifyIconData.Create(Request(NotifyIconCommand.Delete));

        Assert.Equal(string.Empty, data.Tip);
        Assert.Equal(string.Empty, data.Info);
        Assert.Equal(string.Empty, data.InfoTitle);
    }
}
