using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Theme;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Common;

public class ErrorCardViewModelTests
{
    private static readonly UiFormatter _text = new(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: "windows", timeZone: TimeZoneInfo.Utc);

    // Core names a clash's folder by the last part of its path, split the way the running system splits one, so the paths are built for that system.
    private static string PathOnThisSystem(params string[] parts) => Path.Combine([OperatingSystem.IsWindows() ? @"C:\" : "/", .. parts]);

    [Fact]
    public void A_file_in_use_card_has_the_headline_the_lock_icon_the_body_and_the_file()
    {
        var card = new ErrorCardViewModel(MockupScenario.FileInUseError, _text);

        Assert.Equal("A file is in use, so the switch stopped partway", card.Title);
        Assert.StartsWith("Something else on your computer is holding one of the game's files open.", card.Body);
        Assert.Equal(Icons.Lock, card.Icon);
        Assert.Equal(ErrorTone.Error, card.Tone);
        Assert.True(card.IsError);
        Assert.False(card.IsWarning);
        Assert.Equal(@"C:\Games\SkyrimSE\Data\Skyrim.esm", card.Path);
        Assert.True(card.HasPath);
    }

    [Fact]
    public void A_paused_free_up_promises_no_rerun_that_nothing_schedules()
    {
        var card = new ErrorCardViewModel(ErrorInfo.From(new CompactionPreemptedException("skyrim")), _text);

        Assert.Equal("Freeing up space paused so something else on this game could go first. Nothing was lost. Free up space again any time to finish the job.", card.Body);
        Assert.DoesNotContain("by itself", card.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_folder_too_broad_card_names_the_refused_path_and_the_operating_system()
    {
        var card = new ErrorCardViewModel(MockupScenario.FolderTooBroadError, _text);

        Assert.Equal("That folder is too broad to use", card.Title);
        Assert.Equal(Icons.Block, card.Icon);
        Assert.Equal(@"C:\Program Files (x86)", card.Path);
        Assert.Contains("a Windows folder", card.Body);
    }

    [Fact]
    public void A_folder_that_holds_files_where_a_file_is_needed_says_which_file_to_move()
    {
        var folder = PathOnThisSystem("Games", "Alpha", "Mods", "Foo");
        var file = Path.Combine(folder, "data.bin");
        var error = ErrorInfo.From(new FileFolderClashException(RepoRole.Install, FileFolderClashKind.FolderHoldsFiles, folder, file));

        var card = new ErrorCardViewModel(error, _text);

        Assert.Equal("A file and a folder share a name", card.Title);
        Assert.Equal(Icons.Folder, card.Icon);
        Assert.Equal(ErrorTone.Warning, card.Tone);
        Assert.Equal(file, card.Path);
        Assert.Contains("a file named Foo in your install folder", card.Body);
        Assert.Contains("already a folder with that name", card.Body);
        Assert.Contains("won't delete your files", card.Body);
    }

    [Fact]
    public void A_file_where_a_folder_is_needed_says_which_file_to_move()
    {
        var slots = PathOnThisSystem("Saves", "Alpha", "Slots");
        var error = ErrorInfo.From(new FileFolderClashException(RepoRole.External, FileFolderClashKind.FileInTheWay, slots, slots));

        var card = new ErrorCardViewModel(error, _text);

        Assert.Equal("A file and a folder share a name", card.Title);
        Assert.Equal(slots, card.Path);
        Assert.Contains("a folder named Slots in your saves folder", card.Body);
        Assert.Contains("already a file with that name", card.Body);
        Assert.Contains("won't delete your file", card.Body);
    }

    [Fact]
    public void A_folder_gone_card_is_neutral_and_shows_the_path_of_the_missing_folder()
    {
        var card = new ErrorCardViewModel(MockupScenario.FolderGoneError, _text);

        Assert.Equal("This game's folder has moved or is gone", card.Title);
        Assert.Equal(Icons.FolderOff, card.Icon);
        Assert.Equal(ErrorTone.Neutral, card.Tone);
        Assert.True(card.IsNeutral);
        Assert.Contains("install folder", card.Body);
        Assert.Equal(@"C:\Games\The Last Caretaker", card.Path);
    }

    [Fact]
    public void An_error_without_a_path_has_no_path_surface()
    {
        var card = new ErrorCardViewModel(ErrorInfo.From(new AmendNotAvailableException()), _text);

        Assert.Null(card.Path);
        Assert.False(card.HasPath);
    }

    [Fact]
    public void A_path_argument_that_is_not_text_is_ignored()
    {
        var error = new ErrorInfo("error.unknown", new Dictionary<string, object?> { ["path"] = 42 }, string.Empty);

        Assert.Null(new ErrorCardViewModel(error, _text).Path);
    }

    [Fact]
    public void An_error_id_the_app_does_not_know_gets_the_generic_card_with_the_unknown_error_message()
    {
        var card = new ErrorCardViewModel(new ErrorInfo("error.somethingNew.happened", new Dictionary<string, object?>(), "dev"), _text);

        Assert.Equal("Something went wrong", card.Title);
        Assert.Equal("Something went wrong. Try again.", card.Body);
        Assert.Equal(ErrorTone.Error, card.Tone);
    }

    [Fact]
    public void Details_are_hidden_until_the_toggle_is_used_and_the_label_follows()
    {
        var card = new ErrorCardViewModel(MockupScenario.FileInUseError, _text);

        Assert.True(card.HasDetail);
        Assert.False(card.ShowDetails);
        Assert.Equal("Show details", card.DetailsToggleLabel);
        Assert.Contains("unable to unlink", card.Detail);

        card.ToggleDetailsCommand.Execute(null);

        Assert.True(card.ShowDetails);
        Assert.Equal("Hide details", card.DetailsToggleLabel);

        card.ToggleDetailsCommand.Execute(null);

        Assert.False(card.ShowDetails);
        Assert.Equal("Show details", card.DetailsToggleLabel);
    }

    [Fact]
    public void The_toggle_label_change_is_announced_to_bindings()
    {
        var card = new ErrorCardViewModel(MockupScenario.FileInUseError, _text);
        var changed = new List<string?>();
        card.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        card.ToggleDetailsCommand.Execute(null);

        Assert.Contains(nameof(ErrorCardViewModel.DetailsToggleLabel), changed);
        Assert.Contains(nameof(ErrorCardViewModel.ShowDetails), changed);
    }

    [Fact]
    public void An_error_with_no_detail_offers_no_toggle()
    {
        var card = new ErrorCardViewModel(new ErrorInfo("error.unknown", new Dictionary<string, object?>(), "  "), _text);

        Assert.False(card.HasDetail);
    }

    [Fact]
    public void Actions_and_supplement_are_kept_in_order()
    {
        var retry = new RelayCommand(() => { });
        var actions = new[] { new ErrorCardAction("Try again", retry, IsPrimary: true), new ErrorCardAction("Choose another", new RelayCommand(() => { })) };
        var supplement = new object();

        var card = new ErrorCardViewModel(MockupScenario.FileInUseError, _text, actions, supplement);

        Assert.Equal(["Try again", "Choose another"], card.Actions.Select(action => action.Label));
        Assert.True(card.Actions[0].IsPrimary);
        Assert.False(card.Actions[1].IsPrimary);
        Assert.Same(supplement, card.Supplement);
    }

    [Fact]
    public void A_custom_card_carries_exactly_what_it_was_given()
    {
        ICommand retry = new RelayCommand(() => { });

        var card = ErrorCardViewModel.Custom(
            _text,
            Icons.Warning,
            ErrorTone.Warning,
            "Only part of the snapshot was taken",
            "Your game version and mods are in the snapshot.",
            path: @"D:\SavePoint",
            detail: "no space left",
            actions: [new ErrorCardAction("Retry the saves", retry, true)]);

        Assert.Equal("Only part of the snapshot was taken", card.Title);
        Assert.Equal("Your game version and mods are in the snapshot.", card.Body);
        Assert.Equal(Icons.Warning, card.Icon);
        Assert.True(card.IsWarning);
        Assert.Equal(@"D:\SavePoint", card.Path);
        Assert.Equal("no space left", card.Detail);
        Assert.True(card.HasDetail);
        Assert.Single(card.Actions);
    }

    [Fact]
    public void A_full_disk_shows_the_drive_to_clear_on_a_windows_card_and_the_folder_elsewhere()
    {
        var error = ErrorInfo.From(new DiskSpaceException(@"D:\SavePoint\Storage", 1_200_000_000, 0));
        var linux = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: "linux", timeZone: TimeZoneInfo.Utc);

        Assert.Equal("D:", new ErrorCardViewModel(error, _text).Path);
        Assert.Equal(@"D:\SavePoint\Storage", new ErrorCardViewModel(error, linux).Path);
    }

    [Fact]
    public void A_custom_card_without_detail_or_path_offers_neither()
    {
        var card = ErrorCardViewModel.Custom(_text, Icons.Info, ErrorTone.Neutral, "Title", "Body");

        Assert.False(card.HasDetail);
        Assert.False(card.HasPath);
    }

    [Fact]
    public void Every_error_message_in_the_catalog_has_an_entry_in_the_presentation_table()
    {
        var missing = CatalogLintKeys()
            .Where(key => key.StartsWith("error.", StringComparison.Ordinal) && !ErrorPresentation.Table.ContainsKey(key))
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Every_entry_in_the_presentation_table_is_a_real_message_with_a_real_headline_and_an_icon()
    {
        var keys = CatalogLintKeys().ToHashSet();

        foreach (var (id, kind) in ErrorPresentation.Table)
        {
            Assert.Contains(id, keys);
            Assert.Contains(kind.TitleKey, keys);
            Assert.False(string.IsNullOrEmpty(kind.Icon));
        }
    }

    [Fact]
    public void An_unknown_id_is_presented_as_a_generic_error()
    {
        var kind = ErrorPresentation.For("error.never.heard.of.it");

        Assert.Equal(ErrorTone.Error, kind.Tone);
        Assert.Equal("errorCard.title.generic", kind.TitleKey);
    }

    [Fact]
    public void A_known_id_is_presented_by_its_table_entry()
    {
        Assert.Equal(Icons.Lock, ErrorPresentation.For("error.gitOperation.checkoutFailed").Icon);
        Assert.Equal(ErrorTone.Warning, ErrorPresentation.For("error.diskSpace.low").Tone);
    }

    [Fact]
    public void The_mockup_half_saved_result_uses_the_disk_space_error_for_its_unsaved_folder()
    {
        var failed = MockupScenario.HalfSaved.Folders.Single(folder => !folder.Ok).Error!;

        Assert.Equal(RepoRole.External, MockupScenario.HalfSaved.Folders.Single(folder => !folder.Ok).Role);
        Assert.Equal("error.diskSpace.low", failed.MessageId);
        Assert.Equal(ErrorTone.Warning, ErrorPresentation.For(failed.MessageId).Tone);
    }

    private static IReadOnlyCollection<string> CatalogLintKeys() => LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true).Keys;
}
