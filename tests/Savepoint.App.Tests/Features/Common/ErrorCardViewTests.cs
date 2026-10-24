using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Common.Samples;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Common;

public class ErrorCardViewTests
{
    private static readonly UiFormatter _text = new(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: "windows", timeZone: TimeZoneInfo.Utc);

    [AvaloniaFact]
    public void The_card_shows_the_headline_the_body_and_the_path_on_its_own_surface()
    {
        using var shown = Show(new ErrorCardViewModel(MockupScenario.FileInUseError, _text, operation: ErrorOperation.Switch));

        Assert.Equal("A file is in use, so the switch stopped partway", shown.Text("TitleText"));
        Assert.StartsWith("Something else on your computer", shown.Text("BodyText"));
        Assert.True(shown.Find<Border>("PathSurface").IsEffectivelyVisible);
        Assert.Contains(@"C:\Games\SkyrimSE\Data\Skyrim.esm", shown.Card.GetVisualDescendants().OfType<SelectableTextBlock>().Select(block => block.Text));
    }

    [AvaloniaFact]
    public void An_error_without_a_path_shows_no_path_surface()
    {
        using var shown = Show(new ErrorCardViewModel(ErrorInfo.From(new AmendNotAvailableException()), _text));

        Assert.False(shown.Find<Border>("PathSurface").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void The_details_panel_stays_hidden_until_the_toggle_is_pressed_then_explains_the_technical_text_and_offers_to_copy_it()
    {
        var card = new ErrorCardViewModel(MockupScenario.FileInUseError, _text);
        using var shown = Show(card);
        var toggle = shown.Find<ToggleButton>("DetailsToggle");

        Assert.True(toggle.IsEffectivelyVisible);
        Assert.Equal("Show details", toggle.Content);
        Assert.False(shown.Find<Border>("DetailsSurface").IsEffectivelyVisible);

        toggle.IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(card.ShowDetails);
        Assert.Equal("Hide details", toggle.Content);
        Assert.True(shown.Find<Border>("DetailsSurface").IsEffectivelyVisible);
        Assert.Contains("technical details for a bug report", shown.Text("DetailsExplanation"), StringComparison.Ordinal);
        Assert.Equal("Copy details for support", shown.Find<Button>("CopyDetails").Content);

        toggle.IsChecked = false;
        Dispatcher.UIThread.RunJobs();

        Assert.False(shown.Find<Border>("DetailsSurface").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void No_exception_or_tool_text_is_rendered_anywhere_on_the_card_even_with_the_details_open()
    {
        var error = ErrorInfo.From(new GitOperationException(@"C:\Games\SkyrimSE\Data\Skyrim.esm", "fatal: unable to unlink 'Data/Skyrim.esm': Permission denied"));
        var card = new ErrorCardViewModel(error, _text) { ShowDetails = true };
        using var shown = Show(card);

        var rendered = shown.Card.GetVisualDescendants()
            .Select(visual => visual switch
            {
                SelectableTextBlock selectable => selectable.Text,
                TextBlock block => block.Text,
                _ => null,
            })
            .Where(text => !string.IsNullOrEmpty(text))
            .ToList();

        Assert.NotEmpty(rendered);
        Assert.DoesNotContain(rendered, text => text!.Contains("unable to unlink", StringComparison.Ordinal));
        Assert.DoesNotContain(rendered, text => text!.Contains("GitOperationException", StringComparison.Ordinal));
        Assert.DoesNotContain(rendered, text => text!.Contains("fatal:", StringComparison.Ordinal));
        Assert.DoesNotContain(rendered, text => text!.Contains(" at Savepoint.", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Copy_details_puts_the_full_technical_text_on_the_clipboard_and_says_so()
    {
        var error = ErrorInfo.From(new GitOperationException(@"C:\Games\SkyrimSE\Data\Skyrim.esm", "fatal: unable to unlink 'Data/Skyrim.esm': Permission denied"));
        var card = new ErrorCardViewModel(error, _text) { ShowDetails = true };
        using var shown = Show(card);
        var clipboard = TopLevel.GetTopLevel(shown.View)?.Clipboard;
        Assert.NotNull(clipboard);
        await clipboard.ClearAsync();

        shown.Find<Button>("CopyDetails").Command!.Execute(null);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        var copied = await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(clipboard);
        Assert.Equal(error.Detail, copied);
        Assert.Contains("unable to unlink", copied, StringComparison.Ordinal);
        Assert.Equal("Copied. Paste it into your bug report.", card.CopyNote);
    }

    [AvaloniaFact]
    public void An_error_without_detail_offers_no_toggle()
    {
        using var shown = Show(new ErrorCardViewModel(new ErrorInfo("error.unknown", new Dictionary<string, object?>(), string.Empty), _text));

        Assert.False(shown.Find<ToggleButton>("DetailsToggle").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Actions_become_buttons_the_primary_one_filled_and_each_runs_its_command()
    {
        var retried = 0;
        var chosen = 0;
        var actions = new[]
        {
            new ErrorCardAction("Try again", new RelayCommand(() => retried++), IsPrimary: true),
            new ErrorCardAction("Choose the folder", new RelayCommand(() => chosen++)),
        };
        using var shown = Show(new ErrorCardViewModel(MockupScenario.FileInUseError, _text, actions));

        var buttons = shown.Card.GetVisualDescendants().OfType<Button>().Where(button => button is not ToggleButton && button.Name != "CopyDetails").ToList();

        Assert.Equal(["Try again", "Choose the folder"], buttons.Select(button => button.Content));
        Assert.Contains("filled", buttons[0].Classes);
        Assert.DoesNotContain("filled", buttons[1].Classes);
        buttons[0].Command!.Execute(null);
        buttons[1].Command!.Execute(null);
        Assert.Equal((1, 1), (retried, chosen));
    }

    [AvaloniaFact]
    public void The_icon_takes_the_color_class_of_the_tone()
    {
        using var error = Show(new ErrorCardViewModel(MockupScenario.FileInUseError, _text));
        using var warning = Show(ErrorCardViewModel.Custom(_text, Savepoint.App.Theme.Icons.Warning, ErrorTone.Warning, "t", "b"));
        using var neutral = Show(new ErrorCardViewModel(MockupScenario.FolderGoneError, _text));

        Assert.Contains("error", error.Find<TextBlock>("IconGlyph").Classes);
        Assert.Contains("warning", warning.Find<TextBlock>("IconGlyph").Classes);
        Assert.Contains("muted", neutral.Find<TextBlock>("IconGlyph").Classes);
        Assert.DoesNotContain("error", warning.Find<TextBlock>("IconGlyph").Classes);
    }

    [AvaloniaFact]
    public void The_supplement_is_shown_through_the_view_locator_and_hidden_when_there_is_none()
    {
        using var with = Show(new ErrorCardViewModel(MockupScenario.FolderTooBroadError, _text, supplement: new SampleDialogViewModel()));
        using var without = Show(new ErrorCardViewModel(MockupScenario.FolderTooBroadError, _text));

        Assert.True(with.Find<ContentControl>("SupplementHost").IsEffectivelyVisible);
        Assert.NotEmpty(with.Card.GetVisualDescendants().OfType<SampleDialogView>());
        Assert.False(without.Find<ContentControl>("SupplementHost").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void The_card_is_named_after_its_headline_and_announced_politely_and_its_icon_is_decorative()
    {
        using var shown = Show(new ErrorCardViewModel(MockupScenario.FileInUseError, _text, operation: ErrorOperation.Switch));

        Assert.Equal("A file is in use, so the switch stopped partway", AutomationProperties.GetName(shown.Find<Border>("Card")));
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(shown.Find<Border>("Card")));
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(shown.Find<TextBlock>("IconGlyph")));
        Assert.Empty(AutomationAudit.IconButtonsWithoutNames(shown.Card));
    }

    private static Shown Show(ErrorCardViewModel card) => new(card);

    private sealed class Shown : IDisposable
    {
        private readonly ThemeHost _host;

        public Shown(ErrorCardViewModel card)
        {
            View = new ErrorCardView { DataContext = card };
            _host = new ThemeHost(View, width: 520, height: 420);
            Dispatcher.UIThread.RunJobs();
        }

        public ErrorCardView View { get; }

        public Border Card => Find<Border>("Card");

        public T Find<T>(string name)
            where T : Control => View.FindControl<T>(name) ?? throw new InvalidOperationException($"No control named {name}.");

        public string? Text(string name) => Find<TextBlock>(name).Text;

        public void Dispose() => _host.Dispose();
    }
}
