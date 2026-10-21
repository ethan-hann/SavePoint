using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace Savepoint.App.Tests.Localization;

public class LocExtensionTests
{
    [AvaloniaFact]
    public void Resolves_for_text_content_tooltip_and_automation_name()
    {
        var host = new LocHostView();

        Assert.Equal("SavePoint", host.NameText.Text);
        Assert.Equal("SavePoint", host.NameButton.Content);
        Assert.Equal("SavePoint", ToolTip.GetTip(host.NameButton));
        Assert.Equal("SavePoint", AutomationProperties.GetName(host.NameButton));
    }

    [AvaloniaFact]
    public void Key_can_be_given_by_name_instead_of_position()
    {
        var host = new LocHostView();

        Assert.Equal("Keep the setup that works", host.NamedKeyText.Text);
    }
}
