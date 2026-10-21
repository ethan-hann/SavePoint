using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Editor;
using Savepoint.App.Localization;

namespace Savepoint.App.Tests.Features.Editor;

public class ConsentDialogViewModelTests
{
    private static readonly UiFormatter _text = new(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: "windows", timeZone: TimeZoneInfo.Utc);

    private static ConsentDialogViewModel Dialog(params ConsentFolder[] folders) => new(folders, _text);

    private static ConsentFolder Install => new("Game install folder", @"C:\Games\SkyrimSE");

    private static ConsentFolder Saves => new("Saves folder", @"C:\Users\you\Documents\My Games\Skyrim Special Edition\Saves");

    // A dialog that never answers fails here instead of hanging the run.
    private static bool Answer(ConsentDialogViewModel dialog)
    {
        Assert.True(dialog.Result.IsCompleted, "The dialog did not answer.");
        return dialog.Result.Result;
    }

    [Fact]
    public void It_names_every_folder_with_its_resolved_path()
    {
        var dialog = Dialog(Install, Saves);

        Assert.Equal([Install, Saves], dialog.Folders);
        Assert.Equal("Confirm these folders", dialog.Title);
        Assert.Contains("these folders", dialog.Message);
    }

    [Fact]
    public void One_folder_reads_in_the_singular()
    {
        var dialog = Dialog(Install);

        Assert.Equal("Confirm this folder", dialog.Title);
        Assert.Contains("this folder", dialog.Message);
        Assert.DoesNotContain("these", dialog.Message);
    }

    [Fact]
    public void Confirming_answers_true()
    {
        var dialog = Dialog(Install);

        dialog.ConfirmCommand.Execute(null);

        Assert.True(Answer(dialog));
    }

    [Fact]
    public void Cancel_answers_false()
    {
        var dialog = Dialog(Install);

        dialog.CancelCommand.Execute(null);

        Assert.False(Answer(dialog));
    }

    [Fact]
    public void Escape_and_the_scrim_answer_false_never_true()
    {
        var dialog = Dialog(Install);

        dialog.RequestDismiss();

        Assert.False(Answer(dialog));
    }
}
