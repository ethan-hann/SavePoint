using CommunityToolkit.Mvvm.ComponentModel;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.Editor;

public sealed partial class GameEditorViewModel
{
    /// <summary>What the cloud-save note says; empty when there is nothing to warn about, or the store has not been asked yet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCloudSavesNote))]
    private string _cloudSavesNote = string.Empty;

    /// <summary>True when <see cref="CloudSavesNote"/> has text.</summary>
    public bool HasCloudSavesNote => CloudSavesNote.Length > 0;

    // A game that is not saved yet has no store to ask, so it gets the softer advice at once.
    private void InitializeCloudSaves()
    {
        if (_saved is null)
        {
            ShowCloudSaves(new CloudSaveInfo(CloudSaveState.Unknown, CloudStore.None));
        }
    }

    // Asked in the background with the rest of the load; the note stays hidden until the answer is in, so an "off" game never flashes it.
    private async Task LoadCloudSavesAsync()
    {
        if (_saved is null)
        {
            return;
        }

        var read = await _games.GetCloudSavesAsync(_saved.Id);
        ShowCloudSaves(read.Ok ? read.Value : new CloudSaveInfo(CloudSaveState.Unknown, CloudStore.None));
    }

    private void ShowCloudSaves(CloudSaveInfo info) =>
        CloudSavesNote = (info.State, info.Store) switch
        {
            (CloudSaveState.On, CloudStore.Steam) => _text.Message("gameEditor.cloudSaves.on.steam"),
            (CloudSaveState.On, CloudStore.Gog) => _text.Message("gameEditor.cloudSaves.on.gog"),
            (CloudSaveState.Off, _) => string.Empty,
            _ => _text.Message("gameEditor.cloudSaves.unknown"),
        };
}
