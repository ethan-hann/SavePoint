using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Savepoint.App.Features.Common.Formatting;

namespace Savepoint.App.Features.Shell;

/// <summary>
/// The first-run empty state (mockup 2e): shown in place of the Games screen while the library has no games. Its
/// buttons are the shell's own commands; the storage note is split around the path so the path can be drawn in monospace.
/// </summary>
public sealed partial class FirstRunViewModel : ObservableObject
{
    // A private-use character stands in for the path while the message is formatted, so the note can be cut around it in any word order.
    private const string _pathMarker = "";

    private readonly UiFormatter _text;

    [ObservableProperty]
    private string _storagePath = string.Empty;

    [ObservableProperty]
    private string _storageNoteBefore = string.Empty;

    [ObservableProperty]
    private string _storageNoteAfter = string.Empty;

    /// <param name="text">Formats the storage note.</param>
    /// <param name="addFirstGame">Opens the game editor.</param>
    /// <param name="startFromTemplate">Offers the built-in templates and a file.</param>
    public FirstRunViewModel(UiFormatter text, ICommand addFirstGame, ICommand startFromTemplate)
    {
        _text = text;
        AddFirstGameCommand = addFirstGame;
        StartFromTemplateCommand = startFromTemplate;
    }

    /// <summary>Opens the game editor in create mode.</summary>
    public ICommand AddFirstGameCommand { get; }

    /// <summary>Offers the built-in templates and a file to start from.</summary>
    public ICommand StartFromTemplateCommand { get; }

    /// <summary>True once the storage folder is known, which is when the note is shown.</summary>
    public bool HasStorageNote => StoragePath.Length > 0;

    /// <summary>Tells the empty state where SavePoint keeps its data.</summary>
    public void SetStorageRoot(string path)
    {
        StoragePath = path;
        var parts = _text.Message("firstRun.storageNote", ("path", _pathMarker)).Split(_pathMarker, 2);
        StorageNoteBefore = parts[0];
        StorageNoteAfter = parts.Length > 1 ? parts[1] : string.Empty;
        OnPropertyChanged(nameof(HasStorageNote));
    }
}
