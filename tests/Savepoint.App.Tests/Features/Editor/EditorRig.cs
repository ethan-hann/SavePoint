using System.ComponentModel;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Art;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Editor;
using Savepoint.App.Features.Help;
using Savepoint.App.Features.Shell;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.History;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Editor;

// A shell that only records what the editor asks of it, in order.
internal sealed class FakeShellContext : IShellContext
{
    private readonly List<GameDefinition> _games;

    public FakeShellContext(IEnumerable<GameDefinition> games, UiFormatter text)
    {
        _games = [.. games];
        LayoutToggle = new LayoutToggleViewModel(text, _ => Task.CompletedTask, ShellLayout.Sidebar);
    }

    public event PropertyChangedEventHandler? PropertyChanged
    {
        add { }
        remove { }
    }

    public event EventHandler? LibraryRefreshed
    {
        add { }
        remove { }
    }

    public event EventHandler<AddGameRequest>? AddGameRequested
    {
        add { }
        remove { }
    }

    public event EventHandler<ActivityRequest>? ActivityRequested
    {
        add { }
        remove { }
    }

    // "pane", "refresh", "select:<id>" and "close", in the order the editor made them.
    public List<string> Calls { get; } = [];

    public object? Pane { get; private set; }

    public GameDefinition? SelectedGame => null;

    public IReadOnlyList<GameDefinition> Games => _games;

    public ShellLayout Layout => ShellLayout.Sidebar;

    public int LargestFittingUiScale => 200;

    public NavDestination Destination => NavDestination.Games;

    public LayoutToggleViewModel LayoutToggle { get; }

    public ILaunchFlowHost LaunchFlow { get; } = new ScriptedLaunchFlow();

    public IArtworkProvider Artwork { get; set; } = null!;

    public void Navigate(NavDestination destination)
    {
    }

    public void SelectGame(string gameId) => Calls.Add($"select:{gameId}");

    public void ShowActivity(string? gameId) => Calls.Add($"activity:{gameId ?? "all"}");

    public Task RefreshLibraryAsync()
    {
        Calls.Add("refresh");
        return Task.CompletedTask;
    }

    public Task SetLayoutAsync(ShellLayout layout) => Task.CompletedTask;

    public void SettingsChanged(AppSettings settings)
    {
    }

    public void RequestAddGame(GameDefinition? prefill = null)
    {
    }

    public void ShowPane(object paneViewModel)
    {
        Pane = paneViewModel;
        Calls.Add("pane");
    }

    public void ClosePane()
    {
        Pane = null;
        Calls.Add("close");
    }

    public void ShowHelp() => Calls.Add("help");

    public void ShowHelp(HowItWorksSection section) => Calls.Add($"help:{section}");

    public bool CanShowTour => true;

    public void StartTour() => Calls.Add("tour");
}

// The editor wired to the fake backend with every collaborator recorded.
internal sealed class EditorRig
{
    private EditorRig(FakeBackend backend, string os, LocalizationCatalog? catalog, IUiDispatcher? dispatcher)
    {
        Backend = backend;
        Text = new UiFormatter(catalog ?? LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), backend.Clock, os, TimeZoneInfo.Utc);
        Dialogs = new FakeDialogs();
        dispatcher ??= new ImmediateUiDispatcher();
        Toasts = new ToastHostViewModel(new ManualTimeProvider(), dispatcher);
        Shell = new FakeShellContext(FakeAssert.Ok(backend.Games.ListAsync().GetAwaiter().GetResult()), Text);
        Shell.Artwork = new ArtworkProvider(backend.Artwork, dispatcher);
        Factory = new GameEditorFactory(backend.Games, Dialogs, Text, Toasts, backend.Artwork);
    }

    public FakeBackend Backend { get; }

    public UiFormatter Text { get; }

    public FakeDialogs Dialogs { get; }

    public ToastHostViewModel Toasts { get; }

    public FakeShellContext Shell { get; }

    public GameEditorFactory Factory { get; }

    // The dispatcher stands in for the UI thread; pass a held or real one to see the editor under it.
    public static EditorRig Create(FakeBackend? backend = null, string os = "windows", LocalizationCatalog? catalog = null, IUiDispatcher? dispatcher = null) => new(backend ?? FakeBackend.Sample(), os, catalog, dispatcher);

    public GameEditorViewModel Edit(string gameId = SampleScenario.SkyrimId) =>
        Assert.IsType<GameEditorViewModel>(Factory.CreateForExisting(Shell, gameId));

    public GameEditorViewModel New(GameDefinition? prefill = null) =>
        Assert.IsType<GameEditorViewModel>(Factory.CreateForNew(Shell, prefill));

    // A dialog's answer resumes the save on another thread, so the next thing the save shows is waited for, not assumed.
    public static async Task<T> Eventually<T>(Func<T?> find)
        where T : class
    {
        // Generous: it returns as soon as the condition holds, and a loaded machine can take seconds to get there.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (find() is { } found)
                {
                    return found;
                }
            }
            catch (InvalidOperationException)
            {
                // The list was being appended to by the save as it was read; the next look sees it whole.
            }

            await Task.Delay(5);
        }

        throw new Xunit.Sdk.XunitException("The editor never got that far.");
    }

    public IReadOnlyList<ApiCall> Calls(string member) => Backend.Calls.To<IGamesApi>(member);

    // The definition AddAsync or UpdateAsync was asked to save: the only call, or the one at the given position.
    public GameDefinition Saved(string member, int? call = null) =>
        Assert.IsType<GameDefinition>((call is { } index ? Calls(member)[index] : Assert.Single(Calls(member))).Args[0]);
}
