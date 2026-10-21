using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Shell;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Features.Settings;

// The one way the screen changes settings: read what is stored now, apply one change, save, tell the shell.
// Reading first keeps a layout changed on the Games screen, or a date the service stamped, from being overwritten.
internal sealed class SettingsStore
{
    private readonly ISettingsApi _api;
    private readonly IShellContext _shell;
    private readonly ToastHostViewModel _toasts;
    private readonly UiFormatter _text;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SettingsStore(ISettingsApi api, IShellContext shell, ToastHostViewModel toasts, UiFormatter text)
    {
        _api = api;
        _shell = shell;
        _toasts = toasts;
        _text = text;
    }

    // Raised with the settings as stored after every read or save, so the controls show what is on disk.
    public event EventHandler<AppSettings>? Changed;

    // What was stored the last time this store looked; null until the first read.
    public AppSettings? Current { get; private set; }

    // Reads the stored settings; the error when it fails, without telling anyone.
    public async Task<OperationResult<AppSettings>> ReadAsync(bool tellShell = false)
    {
        var read = await _api.GetAsync();
        if (read.Ok)
        {
            Publish(read.Value, tellShell);
        }

        return read;
    }

    // Applies one change to the stored settings. False, with a toast saying why, when nothing was saved.
    public async Task<bool> UpdateAsync(Func<AppSettings, AppSettings> change)
    {
        await _gate.WaitAsync();
        try
        {
            var read = await _api.GetAsync();
            if (!read.Ok)
            {
                Fail(read.Error);
                return false;
            }

            var changed = change(read.Value);
            if (changed == read.Value)
            {
                Publish(read.Value, tellShell: false);
                return true;
            }

            var saved = await _api.SaveAsync(changed);
            if (!saved.Ok)
            {
                Fail(saved.Error);
                return false;
            }

            Publish(saved.Value, tellShell: true);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Show(ErrorInfo error) => Fail(error);

    private void Publish(AppSettings settings, bool tellShell)
    {
        Current = settings;
        if (tellShell)
        {
            _shell.SettingsChanged(settings);
        }

        Changed?.Invoke(this, settings);
    }

    private void Fail(ErrorInfo error) => _toasts.Show(new ToastMessage(_text.Message(error), ToastKind.Error));
}
