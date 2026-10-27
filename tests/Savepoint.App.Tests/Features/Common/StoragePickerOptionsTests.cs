using Savepoint.App.Features.Common.Dialogs;

namespace Savepoint.App.Tests.Features.Common;

public class StoragePickerOptionsTests
{
    private static readonly FileTypeFilter _json = new("Game settings file", ["*.json"]);
    private static readonly FileTypeFilter _any = new("All files", ["*"]);

    [Fact]
    public void A_folder_pick_is_single_with_the_requested_title_and_start()
    {
        var options = StoragePickerOptions.Folder(new FolderPickRequest("Choose the install folder"), null);

        Assert.Equal("Choose the install folder", options.Title);
        Assert.False(options.AllowMultiple);
        Assert.Null(options.SuggestedStartLocation);
    }

    [Fact]
    public void A_folder_pick_can_ask_for_several_folders()
    {
        var options = StoragePickerOptions.Folder(new FolderPickRequest("Choose the mod folders"), null, allowMultiple: true);

        Assert.True(options.AllowMultiple);
    }

    [Fact]
    public void An_open_pick_can_ask_for_several_files()
    {
        var options = StoragePickerOptions.OpenFile(new OpenFilePickRequest("Choose the mod files"), null, allowMultiple: true);

        Assert.True(options.AllowMultiple);
    }

    [Fact]
    public void An_open_pick_offers_each_filter_with_its_patterns()
    {
        var options = StoragePickerOptions.OpenFile(new OpenFilePickRequest("Open a definition", [_json, _any]), null);

        Assert.Equal("Open a definition", options.Title);
        Assert.False(options.AllowMultiple);
        Assert.NotNull(options.FileTypeFilter);
        Assert.Equal(["Game settings file", "All files"], options.FileTypeFilter.Select(type => type.Name));
        Assert.Equal(["*.json"], options.FileTypeFilter[0].Patterns);
    }

    [Fact]
    public void An_open_pick_without_filters_accepts_any_file()
    {
        Assert.Null(StoragePickerOptions.OpenFile(new OpenFilePickRequest("Open"), null).FileTypeFilter);
        Assert.Null(StoragePickerOptions.OpenFile(new OpenFilePickRequest("Open", []), null).FileTypeFilter);
    }

    [Fact]
    public void A_save_pick_suggests_the_name_and_takes_its_default_extension_from_the_first_filter()
    {
        var options = StoragePickerOptions.SaveFile(new SaveFilePickRequest("Export", "skyrim.json", [_json]), null);

        Assert.Equal("Export", options.Title);
        Assert.Equal("skyrim.json", options.SuggestedFileName);
        Assert.Equal("json", options.DefaultExtension);
        Assert.True(options.ShowOverwritePrompt);
        Assert.Equal(["Game settings file"], options.FileTypeChoices!.Select(type => type.Name));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("*.*")]
    [InlineData("game*.json")]
    public void A_save_pick_whose_first_pattern_is_not_a_plain_extension_has_no_default_extension(string pattern)
    {
        var options = StoragePickerOptions.SaveFile(new SaveFilePickRequest("Export", "x", [new FileTypeFilter("Any", [pattern])]), null);

        Assert.Null(options.DefaultExtension);
    }

    [Fact]
    public void A_save_pick_without_filters_has_no_choices_and_no_default_extension()
    {
        var options = StoragePickerOptions.SaveFile(new SaveFilePickRequest("Export", "x"), null);

        Assert.Null(options.FileTypeChoices);
        Assert.Null(options.DefaultExtension);
    }
}
