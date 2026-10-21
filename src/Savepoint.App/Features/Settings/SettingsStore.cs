using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Preferences;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Shell;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Features.Settings;

// The one way the screen changes settings: the shared writer reads what is stored now, applies one change and saves it; the shell is told, so nothing saved elsewhere is overwritten.
internal sealed class SettingsStore
{
    private readonly ISettingsApi _api;
    private readonly SettingsWriter _writer;
    private readonly IShellContext _shell;
    private readonly ToastHostViewModel _toasts;
    private readonly UiFormatter _text;

    public SettingsStore(ISettingsApi api, SettingsWriter writer, IShellContext shell, ToastHostViewModel toasts, UiFormatter text)
    {
        _api = api;
        _writer = writer;
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
        var saved = false;
        var result = await _writer.UpdateAsync(change, settings =>
        {
            saved = true;
            Publish(settings, tellShell: true);
        });

        if (!result.Ok)
        {
            Fail(result.Error);
            return false;
        }

        if (!saved)
        {
            Publish(result.Value, tellShell: false);
        }

        return true;
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
