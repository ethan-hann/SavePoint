using CommunityToolkit.Mvvm.ComponentModel;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.Editor;

public sealed partial class GameEditorViewModel
{
    /// <summary>The warning shown when the store keeps this game's saves in the cloud; empty when it does not, when it cannot be told, or when the store has not been asked yet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCloudSavesNote))]
    private string _cloudSavesNote = string.Empty;

    /// <summary>True when <see cref="CloudSavesNote"/> has text.</summary>
    public bool HasCloudSavesNote => CloudSavesNote.Length > 0;

    /// <summary>How long the Steam app id or install folder must stay unchanged before the store is asked again. Tests replace it to control time.</summary>
    internal Func<TimeSpan, CancellationToken, Task> CloudCheckDelay { get; set; } = (time, token) => Task.Delay(time, token);

    private static readonly TimeSpan _cloudCheckPause = TimeSpan.FromMilliseconds(400);

    // Whether edits to the form start a new check; false while the form is being filled in.
    private bool _cloudWatching;

    // Counts the questions asked; only the latest one's answer is shown. Closing the editor counts as a newer question.
    private int _cloudVersion;
    private int _firstLoadVersion;
    private CancellationTokenSource? _cloudCheck;

    // A game that is not saved yet has no store to ask, so it gets the softer advice at once. A saved game's first question is numbered
    // here, before any edit, so an edit made while the folder status loads is a newer question than the load's.
    private void InitializeCloudSaves()
    {
        if (_saved is null)
        {
            ShowCloudSaves(new CloudSaveInfo(CloudSaveState.Unknown, CloudStore.None));
        }
        else
        {
            _firstLoadVersion = ++_cloudVersion;
        }

        _cloudWatching = true;
    }

    // Asked in the background with the rest of the load; the note stays hidden until the answer is in, so an "off" game never flashes it.
    private async Task LoadCloudSavesAsync()
    {
        if (_saved is null)
        {
            return;
        }

        var version = _firstLoadVersion;
        var read = await _games.GetCloudSavesAsync(_saved.Id);
        if (version == _cloudVersion)
        {
            ShowCloudSaves(read.Ok ? read.Value : new CloudSaveInfo(CloudSaveState.Unknown, CloudStore.None));
        }
    }

    // Called when the Steam app id or install folder changes. Typing restarts the pause, so the store is asked once the value settles.
    private void ScheduleCloudCheck()
    {
        if (!_cloudWatching)
        {
            return;
        }

        _cloudCheck?.Cancel();
        _cloudCheck?.Dispose();
        var check = _cloudCheck = new CancellationTokenSource();
        _ = CheckCloudSavesAsync(++_cloudVersion, check.Token);
    }

    private async Task CheckCloudSavesAsync(int version, CancellationToken token)
    {
        try
        {
            await CloudCheckDelay(_cloudCheckPause, token);
            var query = new CloudSaveQuery(
                InstallPath.Trim(),
                ParseSteamAppId() is > 0 and var id ? id : null,
                LaunchChoice == 1 && Uri.Trim().Length > 0 ? Uri.Trim() : null);
            var read = await _games.GetCloudSavesAsync(query, token);

            // An older question can still answer; its answer is dropped.
            if (version == _cloudVersion)
            {
                ShowCloudSaves(read.Ok ? read.Value : new CloudSaveInfo(CloudSaveState.Unknown, CloudStore.None));
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer edit, or the editor was closed.
        }
    }

    private void StopCloudChecks()
    {
        _cloudWatching = false;
        _cloudVersion++;
        _cloudCheck?.Cancel();
        _cloudCheck?.Dispose();
        _cloudCheck = null;
    }

    // On gets the warning; Off gets nothing; anything else gets only the question button, so the editor does not scold about what it cannot see.
    private void ShowCloudSaves(CloudSaveInfo info)
    {
        CloudSavesNote = (info.State, info.Store) switch
        {
            (CloudSaveState.On, CloudStore.Steam) => _text.Message("gameEditor.cloudSaves.on.steam"),
            (CloudSaveState.On, CloudStore.Gog) => _text.Message("gameEditor.cloudSaves.on.gog"),
            _ => string.Empty,
        };
        HasCloudSavesUnknown = CloudSavesNote.Length == 0 && info.State != CloudSaveState.Off;
        OnPropertyChanged(nameof(HasCloudSavesUnknown));
        if (!HasCloudSavesUnknown)
        {
            CloudSavesHelp.IsOpen = false;
        }
    }
}
