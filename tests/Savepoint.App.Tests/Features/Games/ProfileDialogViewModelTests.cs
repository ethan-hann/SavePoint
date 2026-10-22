using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Games;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Games;

public class ProfileDialogViewModelTests
{
    private static readonly UiFormatter _text = new(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: "windows", timeZone: TimeZoneInfo.Utc);

    private static readonly ErrorInfo _refused = ErrorInfo.From(new ValidationException([new ValidationIssue("name", "validation.profile.nameRequired")]));

    private static Task<OperationResult<Profile>> Saved(string name) =>
        Task.FromResult(OperationResult.Success(new Profile("id", "game", name, "profile/id")));

    private static EditProfileDialogViewModel Edit(
        Func<ProfileChanges, Task<OperationResult<Profile>>>? save = null,
        string name = "Modded playthrough",
        string? notes = "Nolvus v6",
        string? versionOverride = null,
        string? detected = "1.6.640") =>
        new(name, notes, versionOverride, detected, save ?? (changes => Saved(changes.Name)), _text);

    private static DuplicateProfileDialogViewModel Duplicate(Func<string, Task<OperationResult<Profile>>>? duplicate = null) =>
        new("Modded playthrough", duplicate ?? Saved, _text);

    // ---- Edit

    [Fact]
    public void Editing_starts_from_the_profiles_own_values_and_is_named_after_it()
    {
        var dialog = Edit(versionOverride: "1.7.0");

        Assert.Equal("Edit Modded playthrough", dialog.Title);
        Assert.Equal("Modded playthrough", dialog.Name);
        Assert.Equal("Nolvus v6", dialog.Notes);
        Assert.Equal("1.7.0", dialog.VersionOverride);
        Assert.False(dialog.HasError);
    }

    [Fact]
    public void A_profile_without_notes_or_override_starts_with_empty_fields()
    {
        var dialog = Edit(notes: null, versionOverride: null);

        Assert.Equal(string.Empty, dialog.Notes);
        Assert.Equal(string.Empty, dialog.VersionOverride);
    }

    [Fact]
    public void The_version_helper_names_the_detected_version_when_there_is_one()
    {
        var dialog = Edit(detected: "1.6.640");

        Assert.Equal("Leave blank to use the detected version, 1.6.640.", dialog.VersionHelper);
        Assert.Equal("1.6.640", dialog.VersionPlaceholder);
    }

    [Fact]
    public void The_version_helper_says_so_when_nothing_could_be_detected()
    {
        var dialog = Edit(detected: null);

        Assert.Equal("No version could be detected — enter one to show it in the profile list.", dialog.VersionHelper);
        Assert.Null(dialog.VersionPlaceholder);
    }

    [Fact]
    public async Task Saving_sends_the_trimmed_changes_and_closes_with_true()
    {
        ProfileChanges? sent = null;
        var dialog = Edit(save: changes =>
        {
            sent = changes;
            return Saved(changes.Name);
        });
        dialog.Name = "  Tidied up  ";
        dialog.Notes = "  Before the patch ";
        dialog.VersionOverride = " 1.7.0 ";

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.Equal(new ProfileChanges("Tidied up", "Before the patch", "1.7.0"), sent);
        Assert.True(dialog.IsClosed);
        Assert.True(await dialog.Result.Soon());
    }

    [Fact]
    public async Task Blank_notes_and_a_blank_version_are_sent_as_nothing()
    {
        ProfileChanges? sent = null;
        var dialog = Edit(
            save: changes =>
            {
                sent = changes;
                return Saved(changes.Name);
            },
            versionOverride: "1.7.0");
        dialog.Notes = "   ";
        dialog.VersionOverride = string.Empty;

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.Equal(new ProfileChanges("Modded playthrough", null, null), sent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_name_cannot_be_saved(string name)
    {
        var dialog = Edit();

        dialog.Name = name;

        Assert.False(dialog.CanSave);
        Assert.False(dialog.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_blank_name_is_not_sent_even_when_the_command_is_run_anyway()
    {
        var sent = false;
        var dialog = Edit(save: changes =>
        {
            sent = true;
            return Saved(changes.Name);
        });
        dialog.Name = " ";

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.False(sent);
        Assert.False(dialog.IsClosed);
    }

    [Fact]
    public async Task A_refused_save_keeps_the_dialog_open_and_says_why_then_a_retry_can_go_through()
    {
        var fail = true;
        var dialog = Edit(save: changes => Task.FromResult(fail ? OperationResult.Failure<Profile>(_refused) : OperationResult.Success(new Profile("id", "game", changes.Name, "profile/id"))));
        dialog.Name = "Another name";

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.False(dialog.IsClosed);
        Assert.True(dialog.HasError);
        Assert.Equal("Give the profile a name.", dialog.Error);
        Assert.False(dialog.IsSaving);
        Assert.Equal("Another name", dialog.Name);

        fail = false;
        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.True(dialog.IsClosed);
        Assert.True(await dialog.Result.Soon());
        Assert.False(dialog.HasError);
    }

    [Fact]
    public async Task Any_other_refusal_shows_the_errors_own_copy()
    {
        var dialog = Edit(save: _ => Task.FromResult(OperationResult.Failure<Profile>(MockupScenario.SavesOutOfRoomError)));

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.Equal("There's not enough free disk space to save this profile right now. Free up about 1.2 GB on drive D: and try again.", dialog.Error);
    }

    [Fact]
    public async Task The_dialog_cannot_be_dismissed_or_saved_twice_while_a_save_runs()
    {
        var release = new TaskCompletionSource<OperationResult<Profile>>();
        var dialog = Edit(save: _ => release.Task);

        var saving = dialog.SaveCommand.ExecuteAsync(null);

        Assert.True(dialog.IsSaving);
        Assert.False(dialog.CanDismiss);
        Assert.False(dialog.CanSave);
        dialog.RequestDismiss();
        Assert.False(dialog.IsClosed);

        release.SetResult(OperationResult.Success(new Profile("id", "game", "Modded playthrough", "profile/id")));
        await saving.Soon();

        Assert.True(dialog.IsClosed);
    }

    [Fact]
    public async Task Cancel_closes_with_false_and_saves_nothing()
    {
        var sent = false;
        var dialog = Edit(save: changes =>
        {
            sent = true;
            return Saved(changes.Name);
        });
        dialog.Name = "Changed my mind";

        dialog.CancelCommand.Execute(null);

        Assert.False(sent);
        Assert.False(await dialog.Result.Soon());
    }

    [Fact]
    public async Task Escape_closes_an_idle_dialog_with_false()
    {
        var dialog = Edit();

        dialog.RequestDismiss();

        Assert.False(await dialog.Result.Soon());
    }

    [Fact]
    public void The_dialog_is_480_wide()
    {
        Assert.Equal(480, Edit().DialogWidth);
    }

    // ---- Duplicate

    [Fact]
    public void Duplicating_is_named_after_the_source_and_offers_a_copy_name()
    {
        var dialog = Duplicate();

        Assert.Equal("Duplicate Modded playthrough", dialog.Title);
        Assert.Equal("Modded playthrough copy", dialog.Name);
    }

    [Fact]
    public void The_duplicate_dialog_says_what_the_copy_keeps_and_what_starts_fresh()
    {
        var dialog = Duplicate();

        Assert.Equal("The copy starts with the same game version and mods. Its save history starts fresh, and Modded playthrough is left as it is.", dialog.Body);
    }

    [Fact]
    public async Task Saving_a_duplicate_sends_the_trimmed_name_and_closes_with_true()
    {
        string? sent = null;
        var dialog = Duplicate(name =>
        {
            sent = name;
            return Saved(name);
        });
        dialog.Name = "  Modded playthrough, v2 ";

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Modded playthrough, v2", sent);
        Assert.True(await dialog.Result.Soon());
    }

    [Fact]
    public async Task A_refused_duplicate_stays_open_with_the_reason()
    {
        var dialog = Duplicate(_ => Task.FromResult(OperationResult.Failure<Profile>(_refused)));

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.False(dialog.IsClosed);
        Assert.NotNull(dialog.Error);
    }

    [Fact]
    public void A_duplicate_needs_a_name()
    {
        var dialog = Duplicate();

        dialog.Name = " ";

        Assert.False(dialog.CanSave);
    }
}
