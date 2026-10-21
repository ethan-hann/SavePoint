using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Features.Common.Preferences;

/// <summary>
/// The one way the app changes settings: read what is stored now, change one thing, save it. Writers never keep a copy
/// to save from, so a change made elsewhere is never overwritten, and every writer shares one instance, so two changes
/// that overlap in time are applied one after the other instead of the later one saving over the earlier one's field.
/// </summary>
public sealed class SettingsWriter
{
    private readonly ISettingsApi _api;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <param name="api">The settings Api every change goes through.</param>
    public SettingsWriter(ISettingsApi api)
    {
        _api = api;
    }

    /// <summary>
    /// Applies <paramref name="change"/> to the stored settings and saves the result; a change that leaves the settings as
    /// they are saves nothing. Answers with the settings as stored afterwards, or the error of the read or the save, in
    /// which case nothing was changed. Calls queue, and each sees what the one before it saved.
    /// </summary>
    /// <param name="change">Takes the stored settings and returns them with one thing different.</param>
    /// <param name="onSaved">
    /// Called with the saved settings after a save that took effect, before the next queued change starts, so listeners
    /// hear about changes in the order they were stored. It does not run when nothing was saved or the save failed, and
    /// it must not call back into this writer.
    /// </param>
    public async Task<OperationResult<AppSettings>> UpdateAsync(Func<AppSettings, AppSettings> change, Action<AppSettings>? onSaved = null)
    {
        await _gate.WaitAsync();
        try
        {
            var read = await _api.GetAsync();
            if (!read.Ok)
            {
                return read;
            }

            var changed = change(read.Value);
            if (changed == read.Value)
            {
                return read;
            }

            var saved = await _api.SaveAsync(changed);
            if (saved.Ok)
            {
                onSaved?.Invoke(saved.Value);
            }

            return saved;
        }
        finally
        {
            _gate.Release();
        }
    }
}
