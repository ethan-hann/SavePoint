using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Theme;

namespace Savepoint.App.Tests.Features.Common;

public class AvaloniaDialogsTests
{
    private static readonly ConfirmRequest _delete = new("Delete Skyrim?", "This can't be undone.", "Delete game", Destructive: true);

    [AvaloniaFact]
    public async Task A_confirmation_shows_the_dialog_and_answers_true_when_the_confirm_button_is_pressed()
    {
        using var harness = new Harness();

        var pending = harness.Dialogs.ConfirmAsync(_delete);
        harness.Settle();

        Assert.True(harness.Host.IsOpen);
        Assert.False(pending.IsCompleted);
        harness.Button("ConfirmButton").Command!.Execute(null);
        harness.Settle();

        Assert.True(await pending);
        Assert.False(harness.Host.IsOpen);
    }

    [AvaloniaFact]
    public async Task A_confirmation_answers_false_when_cancelled_with_the_catalog_cancel_label()
    {
        using var harness = new Harness();

        var pending = harness.Dialogs.ConfirmAsync(_delete);
        harness.Settle();
        var cancel = harness.Chrome.GetVisualDescendants().OfType<Button>().First(button => "Cancel".Equals(button.Content));
        cancel.Command!.Execute(null);
        harness.Settle();

        Assert.False(await pending);
    }

    [AvaloniaFact]
    public void A_destructive_confirmation_draws_its_confirm_button_in_the_error_style_and_a_plain_one_in_the_filled_style()
    {
        using var harness = new Harness();

        _ = harness.Dialogs.ConfirmAsync(_delete);
        harness.Settle();
        var destructive = harness.Button("ConfirmButton");

        Assert.Contains("error", destructive.Classes);
        Assert.DoesNotContain("filled", destructive.Classes);
        Assert.False(destructive.IsDefault);

        harness.Host.Current!.RequestDismiss();
        _ = harness.Dialogs.ConfirmAsync(new ConfirmRequest("Switch?", "Fine.", "Switch"));
        harness.Settle();
        var plain = harness.Button("ConfirmButton");

        Assert.Contains("filled", plain.Classes);
        Assert.DoesNotContain("error", plain.Classes);
        Assert.True(plain.IsDefault);
    }

    [AvaloniaFact]
    public async Task A_typed_word_keeps_the_confirm_button_disabled_until_it_is_typed()
    {
        using var harness = new Harness();
        var request = new ConfirmRequest("Clear all data?", "Everything goes.", "Clear everything", true, Typed: new TypedConfirmation("clear all data", "Type clear all data to continue."));

        var pending = harness.Dialogs.ConfirmAsync(request);
        harness.Settle();
        var confirm = harness.Button("ConfirmButton");
        var field = harness.Chrome.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "TypedField");

        Assert.True(field.IsEffectivelyVisible);
        Assert.False(confirm.IsEffectivelyEnabled);

        field.Text = "clear all data";
        harness.Settle();

        Assert.True(confirm.IsEffectivelyEnabled);
        confirm.Command!.Execute(null);
        Assert.True(await pending);
    }

    [AvaloniaFact]
    public void Without_a_typed_word_the_field_is_not_shown()
    {
        using var harness = new Harness();

        _ = harness.Dialogs.ConfirmAsync(_delete);
        harness.Settle();

        Assert.False(harness.Chrome.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "TypedField").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task Picks_answer_null_while_no_window_offers_a_storage_provider()
    {
        using var harness = new Harness();

        Assert.Null(await harness.Dialogs.PickFolderAsync(new FolderPickRequest("Pick")));
        Assert.Null(await harness.Dialogs.PickOpenFileAsync(new OpenFilePickRequest("Pick")));
        Assert.Null(await harness.Dialogs.PickSaveFileAsync(new SaveFilePickRequest("Pick", "x.json")));
    }

    [AvaloniaFact]
    public async Task A_custom_dialog_is_shown_on_the_same_stack()
    {
        using var harness = new Harness();
        var dialog = new Samples.SampleDialogViewModel();

        var pending = harness.Dialogs.ShowAsync(dialog);
        harness.Settle();

        Assert.Same(dialog, harness.Host.Current);
        dialog.RequestDismiss();
        Assert.Equal("dismissed", await pending);
    }

    private sealed class Harness : IDisposable
    {
        private readonly ThemeHost _host;

        public Harness()
        {
            Host = new DialogHostViewModel();
            var text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: "windows", timeZone: TimeZoneInfo.Utc);
            Dialogs = new AvaloniaDialogs(Host, text, () => null);
            View = new DialogHostView { DataContext = Host };
            _host = new ThemeHost(View, width: 700, height: 500);
            Settle();
        }

        public DialogHostViewModel Host { get; }

        public AvaloniaDialogs Dialogs { get; }

        public DialogHostView View { get; }

        public Border Chrome => View.FindControl<Border>("Chrome")!;

        public Button Button(string name) => Chrome.GetVisualDescendants().OfType<Button>().Single(button => button.Name == name);

        public void Settle() => Dispatcher.UIThread.RunJobs();

        public void Dispose() => _host.Dispose();
    }
}
