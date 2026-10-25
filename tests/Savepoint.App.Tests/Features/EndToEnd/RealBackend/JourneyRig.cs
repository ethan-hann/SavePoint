using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Editor;
using Savepoint.App.Features.Games;
using Savepoint.App.Features.History;
using Savepoint.App.Features.Launch;
using Savepoint.App.Features.Settings;
using Savepoint.App.Features.Shell;
using Savepoint.App.Hosting;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Contract;
using Savepoint.App.Tests.Features.Settings;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Paths;
using Savepoint.Core.Platform;
using Savepoint.Core.Platform.Linux;
using Savepoint.Core.Platform.MacOS;
using Savepoint.Core.Platform.Windows;
using Savepoint.Testing;

namespace Savepoint.App.Tests.Features.EndToEnd.RealBackend;

// The folders of one game a journey sets up: an install folder holding the stand-in game and a little game content, and an empty saves folder.
internal sealed record GameSetup(string Name, string Install, string Saves, string Program)
{
    // The file SavePoint leaves in a folder it was told to manage.
    public string InstallMarker => Path.Combine(Install, ConsentMarker.FileName);

    public string SavesMarker => Path.Combine(Saves, ConsentMarker.FileName);
}

// A game with two profiles and what each one's folders held when it was saved, for the journeys that switch between them.
internal sealed record TwoProfiles(
    GameSetup Game,
    string GameId,
    ProfileRowViewModel Vanilla,
    ProfileRowViewModel Modded,
    SortedDictionary<string, string> VanillaInstall,
    SortedDictionary<string, string> VanillaSaves,
    SortedDictionary<string, string> ModdedInstall,
    SortedDictionary<string, string> ModdedSaves);

// The whole app as the real program builds it, over the real backend: AppBootstrap starts the backend on temp folders and a throwaway home,
// AppComposition builds the shell from it, and a headless window shows the shell. The game is the stand-in game, started as a real process.
// Events arrive on background threads here, so the app runs on Avalonia's own dispatcher and the tests run on its UI thread.
internal sealed class JourneyRig : IAsyncDisposable
{
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(60);

    private readonly ThemeHost _host;
    private readonly List<string> _programs = [];
    private int _games;

    private JourneyRig(string root, BootstrapResult started, RecordingDiagnosticLog log, MainWindowViewModel main, UiFormatter text, JourneyDialogs dialogs, ThemeHost host)
    {
        Root = root;
        Started = started;
        Log = log;
        Main = main;
        Text = text;
        Dialogs = dialogs;
        _host = host;
    }

    public string Root { get; }

    public BootstrapResult Started { get; }

    public AppServices Services => Started.Services;

    public RecordingDiagnosticLog Log { get; }

    public MainWindowViewModel Main { get; }

    public UiFormatter Text { get; }

    public JourneyDialogs Dialogs { get; }

    public ShellViewModel Shell => Main.Shell ?? throw new InvalidOperationException("The shell has not appeared.");

    public GamesScreenViewModel Games { get; private set; } = null!;

    public HistoryScreenViewModel History { get; private set; } = null!;

    public SettingsScreenViewModel Settings { get; private set; } = null!;

    public string StorageRoot => Started.Backend!.Services.Platform.Directories.DefaultStorageRoot;

    public string CatalogFolder => Started.Backend!.Services.Platform.Directories.CatalogDir;

    public IReadOnlyList<string> ToastTexts => [.. Main.Toasts.Toasts.Select(toast => toast.Text)];

    public static async Task<JourneyRig> StartAsync()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "savepoint-journey-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Path.Combine(root, "app"));
        var log = new RecordingDiagnosticLog();
        try
        {
            var started = AppBootstrap.Start(
                options => options with
                {
                    Platform = HostPlatform(Path.Combine(root, "home")),
                    InstallDirectory = Path.Combine(root, "app"),
                    Git = GitBinary.Locate(Path.Combine(root, "gitconfig")),
                    SteamFolder = new FolderLocator(Path.Combine(root, "steam")),
                    GogGalaxyCache = new NoGalaxyCache(),
                    ArtworkHttp = null,
                    LooseObjectThreshold = int.MaxValue,
                },
                log);
            Assert.True(started.Failure is null, started.Failure?.Detail);

            var text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true));
            var dialogs = new JourneyDialogs();
            var application = (App)Application.Current!;
            var main = AppComposition.Create(
                started.Services,
                text,
                new ThemeServiceApplier(application.Theme),
                () => null,
                new AvaloniaUiDispatcher(),
                dialogs: dialogs,
                settingsEnvironment: new SettingsEnvironment(new RecordingFileRevealer(), () => null, "1.0.0"));
            dialogs.Attach(new AvaloniaDialogs(main.Dialogs, text, () => null));
            var host = new ThemeHost(new MainView { DataContext = main }, ThemePreference.Dark, width: 1040, height: 660);
            var rig = new JourneyRig(root, started, log, main, text, dialogs, host);
            try
            {
                await main.InitializeAsync();
                await rig.WaitAsync(() => main.Shell is { IsLoaded: true }, "the shell to appear");
                rig.Games = rig.Show<GamesScreenViewModel>(NavDestination.Games);
                rig.History = rig.Show<HistoryScreenViewModel>(NavDestination.History);
                rig.Settings = rig.Show<SettingsScreenViewModel>(NavDestination.Settings);
                rig.Shell.Navigate(NavDestination.Games);
                return rig;
            }
            catch
            {
                await rig.DisposeAsync();
                throw;
            }
        }
        catch
        {
            DeleteFolder(root);
            throw;
        }
    }

    // A game's folders under the rig's own root: the stand-in game and some content to switch, and a saves folder with nothing in it yet.
    public GameSetup NewGame(string name)
    {
        var folder = Path.Combine(Root, "games", $"{name.ToLowerInvariant().Replace(' ', '-')}-{++_games}");
        var install = Path.Combine(folder, "game");
        var saves = Path.Combine(folder, "saves");
        var program = StandInGame.InstallInto(install);
        _programs.Add(program);
        WriteFile(Path.Combine(install, "data", "base.pak"), "base content");
        Directory.CreateDirectory(saves);
        return new GameSetup(name, install, saves, program);
    }

    public static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    // Adds a game the way a person does: the editor, filled in, then Save and the question about the two folders. The game starts through its
    // program, with the given options, and mods are looked for under "mods".
    public async Task<GameDefinition> AddGameAsync(GameSetup game, IReadOnlyList<string> launchArgs, Action<ConsentDialogViewModel>? consent = null)
    {
        var known = Shell.Games.Count;
        Shell.AddGameCommand.Execute(null);
        var editor = Assert.IsType<GameEditorViewModel>(Shell.Content);
        editor.Name = game.Name;
        editor.InstallPath = game.Install;
        editor.SavesPath = game.Saves;
        editor.LaunchChoice = 0;
        editor.ExePath = game.Program;
        foreach (var option in launchArgs)
        {
            AddChip(editor.LaunchArgs, option);
        }

        AddChip(editor.ModFolders, "mods");

        var save = editor.SaveCommand.ExecuteAsync(null);
        var question = await DialogAsync<ConsentDialogViewModel>();
        consent?.Invoke(question);
        question.ConfirmCommand.Execute(null);
        await FinishesAsync(save, "saving the game");
        await WaitAsync(() => Shell.Games.Count == known + 1 && Shell.Content is not GameEditorViewModel, "the game to be added and the editor to close");
        return Shell.SelectedGame!;
    }

    // Creates a profile from what the game's folders hold now, through the three steps of the wizard.
    public async Task<ProfileRowViewModel> CreateProfileAsync(string name, Action<NewProfileWizardViewModel>? checkStep = null)
    {
        var wizardClosed = Games.NewProfileCommand.ExecuteAsync(null);
        var wizard = await DialogAsync<NewProfileWizardViewModel>();
        await WaitAsync(() => !wizard.Preview.IsLoading, "the wizard to read what would be captured");
        wizard.Name = name;
        await FinishesAsync(wizard.ContinueCommand.ExecuteAsync(null), "the wizard to reach its check step");
        checkStep?.Invoke(wizard);
        await FinishesAsync(wizard.ContinueCommand.ExecuteAsync(null), "the wizard to start saving");
        await WaitAsync(() => wizard.IsSaveStep && wizard.State != SaveState.Saving, "the profile to be saved");
        Assert.True(wizard.IsSaved, wizard.ResultCard?.Title ?? "the wizard did not report a save");
        wizard.CloseCommand.Execute(null);
        await FinishesAsync(wizardClosed, "the wizard to close");
        await WaitAsync(() => Games.Profiles.Any(row => row.Name == name), $"the {name} profile to appear in the table");
        return Games.Profiles.Single(row => row.Name == name);
    }

    // A game with two profiles, as a person leaves it with the folders on the second. Vanilla is the game as installed with one save; Modded adds
    // two mod files, patches a game file and has a second save. Each profile's folders are recorded as they were when it was saved.
    public async Task<TwoProfiles> TwoProfilesAsync(string name, params string[] gameOptions)
    {
        var game = NewGame(name);
        WriteFile(Path.Combine(game.Saves, "slot1.sav"), "save one");
        var added = await AddGameAsync(game, ["--saves", game.Saves, .. gameOptions]);
        var vanilla = await CreateProfileAsync("Vanilla");
        var vanillaInstall = FolderSnapshot.Of(game.Install);
        var vanillaSaves = FolderSnapshot.Of(game.Saves);

        WriteFile(Path.Combine(game.Install, "mods", "a.txt"), "mod a");
        WriteFile(Path.Combine(game.Install, "mods", "b.txt"), "mod b");
        WriteFile(Path.Combine(game.Install, "data", "base.pak"), "a longer, patched base content");
        WriteFile(Path.Combine(game.Saves, "slot2.sav"), "second save");

        // Git compares a file's time with the index's to the second on Windows, and a file saved in the same second is read again by every later check. A person
        // edits a game and keeps a profile more than a second apart, so the test does too.
        await Task.Delay(TimeSpan.FromMilliseconds(1100), TestContext.Current.CancellationToken);
        var modded = await CreateProfileAsync("Modded");
        var moddedInstall = FolderSnapshot.Of(game.Install);
        var moddedSaves = FolderSnapshot.Of(game.Saves);
        await WaitAsync(() => modded.IsActive && !vanilla.IsActive, "the rows to show the folders are on Modded");
        return new TwoProfiles(game, added.Id, vanilla, modded, vanillaInstall, vanillaSaves, moddedInstall, moddedSaves);
    }

    // Presses Play on a profile that is not in place: the switch dialog opens, the test looks at it, and "Switch and play" answers it. Returns once
    // the launch has been handed to SavePoint; the game itself starts a moment later.
    public async Task<LaunchDialogViewModel> PlayWithSwitchAsync(ProfileRowViewModel row, Action<LaunchDialogViewModel>? look = null)
    {
        Assert.True(row.CanPlay, $"Play is not on offer for {row.Name}.");
        var play = row.PlayCommand.ExecuteAsync(null);
        var dialog = await DialogAsync<LaunchDialogViewModel>();
        look?.Invoke(dialog);
        Assert.True(dialog.CanSwitch, $"The switch dialog is not ready to switch: [{string.Join(" / ", dialog.UnsavedBlocks.Select(block => block.Title + ": " + block.Body))}]");
        dialog.SwitchCommand.Execute(null);
        await FinishesAsync(play, "Play to hand the game over");
        return dialog;
    }

    // Runs a command a screen hands out as a plain command, and returns its task.
    public static Task RunAsync(System.Windows.Input.ICommand command) =>
        Assert.IsAssignableFrom<IAsyncRelayCommand>(command).ExecuteAsync(null);

    public Task WaitForRunningAsync() => WaitAsync(() => Games.InlineContent is RunningCardViewModel, "the game to be running");

    // Answers "Not now" to the question that follows a session, and waits until SavePoint has let go of the game.
    public async Task SkipSessionAsync(PostPlayDialogViewModel postPlay)
    {
        postPlay.NotNowCommand.Execute(null);
        await WaitAsync(() => !Shell.LaunchFlow!.IsActive(Shell.SelectedGame!.Id) && Games.InlineContent is null && Games.Profiles.All(row => row.CanPlay), "the session to end and Play to come back");
    }

    // A value typed into one of the editor's chip lists, as a person adds one.
    private static void AddChip(ChipListViewModel list, string value)
    {
        list.BeginAddCommand.Execute(null);
        list.NewText = value;
        list.CommitAddCommand.Execute(null);
    }

    // The way a person waits: the window keeps running while the test looks at what the app shows. A wait that runs out says where the app stood.
    public async Task WaitAsync(Func<bool> condition, string described, TimeSpan? limit = null)
    {
        var until = DateTime.UtcNow + (limit ?? _patience);
        while (true)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition())
            {
                return;
            }

            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException($"Waited for {described}, which did not happen. {Where()}");
            }

            await Task.Delay(15, TestContext.Current.CancellationToken);
        }
    }

    // Awaits something the test started (a command, a call) without letting it hang the run.
    public async Task FinishesAsync(Task task, string described)
    {
        try
        {
            await task.WaitAsync(_patience, TestContext.Current.CancellationToken);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"Waited for {described} to finish, which did not happen. {Where()}");
        }
    }

    public async Task<T> DialogAsync<T>(Func<T, bool>? where = null)
        where T : class, IDialogViewModel
    {
        await WaitAsync(() => Find(where) is not null, $"a {typeof(T).Name} to be shown");
        return Find(where)!;
    }

    public T Show<T>(NavDestination destination)
        where T : class, IScreen
    {
        Shell.Navigate(destination);
        Dispatcher.UIThread.RunJobs();
        return Assert.IsType<T>(Shell.CurrentScreen);
    }

    // Lets the window lay out and draw what the app shows now, and saves the frame where the renders of this run go.
    public string Render(string name)
    {
        Dispatcher.UIThread.RunJobs();
        return ThemeTestSupport.SaveFrame(_host.Window, Path.Combine(ThemeTestSupport.RepoRoot(), "render-output", "end-to-end", name + ".png"));
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        Main.Dispose();
        _host.Dispose();
        foreach (var program in _programs)
        {
            StandInGame.KillAll(program, TimeSpan.FromSeconds(20));
        }

        // Work queued behind a call, such as the cleanup after a capture, must end before its folders go.
        var until = DateTime.UtcNow + _patience;
        while (Services.Jobs.List().Any(job => job.Status == JobStatus.Running) && DateTime.UtcNow < until)
        {
            await Task.Delay(25);
        }

        await Task.Run(() => DeleteFolder(Root));
    }

    // Git writes its object files read-only, which a plain recursive delete cannot remove on Windows; a program that was just stopped lets go a moment later.
    public static void DeleteFolder(string folder)
    {
        for (var attempt = 0; attempt < 20 && Directory.Exists(folder); attempt++)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(folder, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(250);
            }
        }
    }

    private T? Find<T>(Func<T, bool>? where)
        where T : class, IDialogViewModel =>
        Main.Dialogs.Stack.OfType<T>().LastOrDefault(dialog => where is null || where(dialog));

    private string Where()
    {
        var sessions = string.Join(", ", Services.Launch.ListSessions().Select(session => $"{session.GameId}: {session.State.GetType().Name}"));
        var jobs = string.Join(", ", Services.Jobs.List().Select(job => $"{job.Kind}: {job.Status}"));
        var dialogs = string.Join(" | ", Main.Dialogs.Stack.Select(dialog => dialog.Title));
        var errors = string.Join(" | ", Log.Entries.Where(entry => entry.Level == LogLevel.Error).TakeLast(3).Select(entry => entry.Message));
        return $"The app stood at: sessions [{sessions}], jobs [{jobs}], dialogs [{dialogs}], logged errors [{errors}].";
    }

    // The running OS's platform over a home inside the throwaway folder, so nothing real is touched.
    private static IPlatform HostPlatform(string home)
    {
        var environment = new PlatformEnvironment { HomeDirectory = home };
        return PlatformFactory.CurrentOs switch
        {
            OsKind.Windows => new WindowsPlatform(environment),
            OsKind.Linux => new LinuxPlatform(environment),
            _ => new MacOSPlatform(environment),
        };
    }
}
