using Savepoint.App.Features.Common.Preferences;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Common;

public class SettingsWriterTests
{
    private static ErrorInfo Boom() => ErrorInfo.From(new InvalidOperationException("boom"));

    [Fact]
    public async Task A_change_is_applied_to_what_is_stored_and_the_stored_result_comes_back()
    {
        var backend = FakeBackend.Mockup();
        var writer = new SettingsWriter(backend.Settings);

        var result = await writer.UpdateAsync(settings => settings with { Theme = ThemePreference.Light });

        Assert.Equal(MockupScenario.Settings with { Theme = ThemePreference.Light }, FakeAssert.Ok(result));
        Assert.Equal(ThemePreference.Light, FakeAssert.Ok(await backend.Settings.GetAsync(TestContext.Current.CancellationToken)).Theme);
    }

    [Fact]
    public async Task Each_change_starts_from_what_is_stored_when_it_runs()
    {
        var backend = FakeBackend.Mockup();
        var writer = new SettingsWriter(backend.Settings);
        await writer.UpdateAsync(settings => settings with { Theme = ThemePreference.Light });
        FakeAssert.Ok(await backend.Settings.SaveAsync((await backend.Settings.GetAsync(TestContext.Current.CancellationToken)).Value! with { AccentColorId = AccentColorId.Violet }));

        var result = await writer.UpdateAsync(settings => settings with { Layout = ShellLayout.Library });

        var saved = FakeAssert.Ok(result);
        Assert.Equal((ThemePreference.Light, AccentColorId.Violet, ShellLayout.Library), (saved.Theme, saved.AccentColorId, saved.Layout));
    }

    [Fact]
    public async Task A_change_that_changes_nothing_saves_nothing_and_does_not_call_back()
    {
        var backend = FakeBackend.Mockup();
        var writer = new SettingsWriter(backend.Settings);
        var heard = new List<AppSettings>();

        var result = await writer.UpdateAsync(settings => settings, heard.Add);

        Assert.True(result.Ok);
        Assert.Empty(backend.Calls.To<ISettingsApi>(nameof(ISettingsApi.SaveAsync)));
        Assert.Empty(heard);
    }

    [Fact]
    public async Task A_saved_change_calls_back_with_the_settings_as_stored()
    {
        var backend = FakeBackend.Mockup();
        var writer = new SettingsWriter(backend.Settings);
        var heard = new List<AppSettings>();

        await writer.UpdateAsync(settings => settings with { ConfirmSessionCapture = false }, heard.Add);

        Assert.Equal([MockupScenario.Settings with { ConfirmSessionCapture = false }], heard);
    }

    [Fact]
    public async Task A_read_that_fails_is_reported_and_nothing_is_saved()
    {
        var backend = FakeBackend.Mockup();
        var writer = new SettingsWriter(backend.Settings);
        backend.FailNext<ISettingsApi>(nameof(ISettingsApi.GetAsync), Boom());
        var heard = new List<AppSettings>();

        var result = await writer.UpdateAsync(settings => settings with { Theme = ThemePreference.Light }, heard.Add);

        Assert.False(result.Ok);
        Assert.Empty(backend.Calls.To<ISettingsApi>(nameof(ISettingsApi.SaveAsync)));
        Assert.Empty(heard);
    }

    [Fact]
    public async Task A_save_that_fails_is_reported_and_does_not_call_back()
    {
        var backend = FakeBackend.Mockup();
        var writer = new SettingsWriter(backend.Settings);
        backend.FailNext<ISettingsApi>(nameof(ISettingsApi.SaveAsync), Boom());
        var heard = new List<AppSettings>();

        var result = await writer.UpdateAsync(settings => settings with { Theme = ThemePreference.Light }, heard.Add);

        Assert.False(result.Ok);
        Assert.Empty(heard);
    }

    [Fact]
    public async Task A_failure_does_not_block_the_next_change()
    {
        var backend = FakeBackend.Mockup();
        var writer = new SettingsWriter(backend.Settings);
        backend.FailNext<ISettingsApi>(nameof(ISettingsApi.SaveAsync), Boom());
        await writer.UpdateAsync(settings => settings with { Theme = ThemePreference.Light });

        var result = await writer.UpdateAsync(settings => settings with { Theme = ThemePreference.Light });

        Assert.True(result.Ok);
        Assert.Equal(ThemePreference.Light, result.Value.Theme);
    }
}
