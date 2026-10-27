using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Toasts;

namespace Savepoint.App.Features.Settings;

/// <summary>The About section: the version, a way to see the notices and license files when they were shipped beside the app, and a way to open the log folder.</summary>
public sealed partial class AboutSectionViewModel : ObservableObject
{
    private readonly SettingsEnvironment _environment;
    private readonly UiFormatter _text;
    private readonly ToastHostViewModel _toasts;

    /// <summary>Where <c>THIRD-PARTY-NOTICES.md</c> is; null when it was not shipped, which hides the action.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotices))]
    [NotifyCanExecuteChangedFor(nameof(ShowNoticesCommand))]
    private string? _noticesPath;

    /// <summary>Where <c>LICENSE</c> is; null when it was not shipped, which hides the action.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLicense))]
    [NotifyCanExecuteChangedFor(nameof(ShowLicenseCommand))]
    private string? _licensePath;

    internal AboutSectionViewModel(SettingsEnvironment environment, UiFormatter text, ToastHostViewModel toasts)
    {
        _environment = environment;
        _text = text;
        _toasts = toasts;
        VersionText = text.Message("settings.about.version", ("version", environment.AppVersion));
    }

    /// <summary>"Version 1.2.3".</summary>
    public string VersionText { get; }

    /// <summary>True when the notices action shows.</summary>
    public bool HasNotices => NoticesPath is not null;

    /// <summary>True when the license action shows.</summary>
    public bool HasLicense => LicensePath is not null;

    /// <summary>True when the action that opens the log folder shows.</summary>
    public bool HasLogFolder => _environment.LogFolder is not null;

    // Looked up each time the screen shows, so a file added beside the app shows without a restart.
    internal void Refresh()
    {
        NoticesPath = _environment.FindNotices();
        LicensePath = _environment.FindLicense?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(HasLogFolder))]
    private async Task OpenLogFolderAsync()
    {
        if (_environment.LogFolder is { } folder && !await _environment.Revealer.RevealAsync(folder))
        {
            _toasts.Show(new ToastMessage(_text.Message("settings.about.logFolderFailed"), ToastKind.Error));
        }
    }

    [RelayCommand(CanExecute = nameof(HasLicense))]
    private async Task ShowLicenseAsync()
    {
        if (LicensePath is { } path && !await _environment.Revealer.RevealAsync(path))
        {
            _toasts.Show(new ToastMessage(_text.Message("settings.about.licenseFailed"), ToastKind.Error));
        }
    }

    [RelayCommand(CanExecute = nameof(HasNotices))]
    private async Task ShowNoticesAsync()
    {
        if (NoticesPath is { } path && !await _environment.Revealer.RevealAsync(path))
        {
            _toasts.Show(new ToastMessage(_text.Message("settings.about.noticesFailed"), ToastKind.Error));
        }
    }
}
