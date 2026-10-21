using CommunityToolkit.Mvvm.ComponentModel;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Features.Settings;

/// <summary>
/// The "When you finish playing" section: whether SavePoint asks about saving the session, and whether it skips the
/// question when only the save changed. A switch is saved when it is flipped; one that fails to save flips back.
/// </summary>
public sealed partial class SessionSectionViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private bool _currentAsk = true;
    private bool _currentSkip;
    private bool _restoring;

    /// <summary>Whether SavePoint asks whether to save the session when the game stops.</summary>
    [ObservableProperty]
    private bool _askToSave = true;

    /// <summary>Whether the question is skipped when only the save changed; a change to the version or mods always asks.</summary>
    [ObservableProperty]
    private bool _skipWhenOnlySaveChanges;

    internal SessionSectionViewModel(SettingsStore store)
    {
        _store = store;
        store.Changed += (_, settings) => Show(settings);
    }

    partial void OnAskToSaveChanged(bool value)
    {
        if (!_restoring && value != _currentAsk)
        {
            _currentAsk = value;
            _ = SaveAsync(settings => settings with { ConfirmSessionCapture = value });
        }
    }

    partial void OnSkipWhenOnlySaveChangesChanged(bool value)
    {
        if (!_restoring && value != _currentSkip)
        {
            _currentSkip = value;
            _ = SaveAsync(settings => settings with { SkipConfirmWhenOnlySaveChanges = value });
        }
    }

    private async Task SaveAsync(Func<AppSettings, AppSettings> change)
    {
        // A failed save leaves the stored values as they were, and the switches go back to them.
        if (!await _store.UpdateAsync(change) && _store.Current is { } stored)
        {
            Show(stored);
        }
    }

    private void Show(AppSettings settings)
    {
        _restoring = true;
        try
        {
            _currentAsk = settings.ConfirmSessionCapture;
            _currentSkip = settings.SkipConfirmWhenOnlySaveChanges;
            AskToSave = _currentAsk;
            SkipWhenOnlySaveChanges = _currentSkip;
        }
        finally
        {
            _restoring = false;
        }
    }
}
