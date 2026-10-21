using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Common.Accessibility;

namespace Savepoint.App.Tests.Features.Common;

public class AnnouncementRegionTests
{
    [AvaloniaFact]
    public void A_region_is_a_polite_live_region_that_takes_no_space_focus_or_clicks()
    {
        var region = new AnnouncementRegion();

        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(region));
        Assert.Equal(0, region.Opacity);
        Assert.False(region.IsHitTestVisible);
        Assert.False(region.Focusable);
        Assert.Equal(1, region.Width);
        Assert.Equal(1, region.Height);
    }

    [AvaloniaFact]
    public void The_message_becomes_the_text_and_the_accessible_name()
    {
        var region = new AnnouncementRegion { Message = "Getting Skyrim ready: 50 percent" };

        Assert.Equal("Getting Skyrim ready: 50 percent", region.Text);
        Assert.Equal("Getting Skyrim ready: 50 percent", AutomationProperties.GetName(region));
    }

    [AvaloniaFact]
    public void Changing_the_message_updates_what_is_announced()
    {
        var region = new AnnouncementRegion { Message = "first" };

        region.Message = "second";

        Assert.Equal("second", AutomationProperties.GetName(region));
    }

    [AvaloniaFact]
    public void Announcing_the_same_message_again_is_a_change_assistive_technology_can_see()
    {
        var region = new AnnouncementRegion();
        var names = new List<string?>();
        region.PropertyChanged += (_, e) =>
        {
            if (e.Property == AutomationProperties.NameProperty)
            {
                names.Add(e.NewValue as string);
            }
        };

        region.Announce("Saved.");
        region.Announce("Saved.");

        Assert.Equal(["Saved.", string.Empty, "Saved."], names.SkipWhile(string.IsNullOrEmpty).Take(3).ToList());
        Assert.Equal(2, names.Count(name => name == "Saved."));
    }

    [AvaloniaFact]
    public void A_region_still_uses_the_text_block_styles()
    {
        var region = new AnnouncementRegion();

        Assert.IsAssignableFrom<TextBlock>(region);
    }

    [AvaloniaTheory]
    [InlineData(AutomationLiveSetting.Polite)]
    [InlineData(AutomationLiveSetting.Assertive)]
    [InlineData(AutomationLiveSetting.Off)]
    public void Mark_turns_visible_text_into_a_live_region(AutomationLiveSetting setting)
    {
        var banner = new TextBlock { Text = "Everything was already in place" };

        AnnouncementRegion.Mark(banner, setting);

        Assert.Equal(setting, AutomationProperties.GetLiveSetting(banner));
    }

    [AvaloniaFact]
    public void Mark_defaults_to_polite()
    {
        var banner = new Border();

        AnnouncementRegion.Mark(banner);

        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(banner));
    }
}
