using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Theme;

namespace Savepoint.App.Tests.Features.Common.Samples;

public partial class ErrorCardGallery : UserControl
{
    public ErrorCardGallery()
    {
        InitializeComponent();
    }
}

// The same four cards the mockup draws; the half-saved one is custom because it is not a single error.
public sealed class ErrorCardGalleryViewModel
{
    public ErrorCardGalleryViewModel(UiFormatter text)
    {
        var noop = new RelayCommand(() => { });
        FileInUse = new ErrorCardViewModel(MockupScenario.FileInUseError, text, [new ErrorCardAction("Try again", noop, IsPrimary: true)]);
        TooBroad = new ErrorCardViewModel(
            MockupScenario.FolderTooBroadError,
            text,
            supplement: new SuggestionNoteViewModel(@"Try something like C:\Program Files (x86)\Steam\steamapps\common\Skyrim Special Edition"));
        HalfSaved = ErrorCardViewModel.Custom(
            text,
            Icons.Warning,
            ErrorTone.Warning,
            "Half of that save worked",
            "Your game version and mods were saved. Your save files weren't: the drive ran out of room with 1.2 GB still to write. Free up some space and retry just that part; nothing you already saved is affected.",
            detail: "no space left on device",
            actions: [new ErrorCardAction("Retry the saves", noop, IsPrimary: true)],
            supplement: new SaveStatusViewModel(
                [
                    new SaveStatusRow(Icons.CheckCircle, true, "Game version and mods \u00b7 saved"),
                    new SaveStatusRow(Icons.Cancel, false, "Saves \u00b7 not saved"),
                ]));
        FolderGone = new ErrorCardViewModel(MockupScenario.FolderGoneError, text, [new ErrorCardAction("Choose the folder", noop, IsPrimary: true)]);
    }

    public ErrorCardViewModel FileInUse { get; }

    public ErrorCardViewModel TooBroad { get; }

    public ErrorCardViewModel HalfSaved { get; }

    public ErrorCardViewModel FolderGone { get; }
}

public sealed record SuggestionNoteViewModel(string Text);

public sealed record SaveStatusRow(string Icon, bool Succeeded, string Text);

public sealed record SaveStatusViewModel(IReadOnlyList<SaveStatusRow> Rows);
