using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Toasts;

namespace Savepoint.App.Features.Settings;

/// <summary>The About section: the version and, when the notices file was shipped beside the app, a way to see it.</summary>
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

    // Looked up each time the screen shows, so a file added beside the app shows without a restart.
    internal void Refresh() => NoticesPath = _environment.FindNotices();

    [RelayCommand(CanExecute = nameof(HasNotices))]
    private void ShowNotices()
    {
        if (NoticesPath is { } path && !_environment.Revealer.Reveal(path))
        {
            _toasts.Show(new ToastMessage(_text.Message("settings.about.noticesFailed"), ToastKind.Error));
        }
    }
}
