using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Tests.Features.Common.Samples;

namespace Savepoint.App.Tests.Features.Common;

public class ViewLocatorTests
{
    [AvaloniaFact]
    public void A_view_model_gets_the_view_with_the_same_name_minus_model()
    {
        var view = new ViewLocator().Build(new SampleDialogViewModel());

        Assert.IsType<SampleDialogView>(view);
    }

    [Fact]
    public void Matching_needs_a_view_model_that_has_a_view()
    {
        var locator = new ViewLocator();

        Assert.True(locator.Match(new SampleDialogViewModel()));
        Assert.True(locator.Match(new ConfirmDialogViewModel(new ConfirmRequest("t", "m", "c"), "Cancel")));
        Assert.False(locator.Match("a string"));
        Assert.False(locator.Match(null));
        Assert.False(locator.Match(new NoViewViewModel()));
        Assert.False(locator.Match(new NotNamedLikeOne()));
    }

    [Fact]
    public void Building_for_something_without_a_view_gives_nothing()
    {
        Assert.Null(new ViewLocator().Build(new NoViewViewModel()));
        Assert.Null(new ViewLocator().Build(null));
    }

    [Fact]
    public void The_view_type_is_looked_up_in_the_view_models_own_assembly()
    {
        Assert.Equal(typeof(ConfirmDialogView), ViewLocator.ViewTypeFor(typeof(ConfirmDialogViewModel)));
        Assert.Equal(typeof(SampleDialogView), ViewLocator.ViewTypeFor(typeof(SampleDialogViewModel)));
    }

    private sealed class NoViewViewModel;

    private sealed class NotNamedLikeOne;
}
