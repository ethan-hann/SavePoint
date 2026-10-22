using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using Savepoint.App.Platform;
using Savepoint.App.Tests.Platform.Fakes;
using Savepoint.Core.Platform;

namespace Savepoint.App.Tests.Platform;

public sealed class SingleInstanceTests : IDisposable
{
    private const string _user = "test-user";

    private readonly List<IDisposable> _disposables = [];
    private readonly string _appId = "test-" + Guid.NewGuid().ToString("N");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }

    // A Unix socket needs a short path on a file system that holds sockets (a Windows drive in WSL does not); SAVEPOINT_TEST_SOCKET_DIR names another folder.
    internal static string PipeDirectory()
    {
        var directory = Environment.GetEnvironmentVariable("SAVEPOINT_TEST_SOCKET_DIR") ?? Path.GetTempPath();
        if (!OperatingSystem.IsWindows())
        {
            Assert.SkipUnless(CanHoldSockets(directory), $"A Unix socket cannot be created in {directory}: too long a path, or a file system without sockets.");
        }

        return directory;
    }

    private static bool CanHoldSockets(string directory)
    {
        var path = Path.Combine(directory, "sp-" + new string('0', 12));
        try
        {
            using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            probe.Bind(new UnixDomainSocketEndPoint(path));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            File.Delete(path);
        }
    }
    private SingleInstance Create(string? appId = null, string? user = null, string? pipeDirectory = null, TimeSpan? connectTimeout = null)
    {
        var instance = new SingleInstance(appId ?? _appId, user ?? _user, pipeDirectory ?? PipeDirectory(), connectTimeout ?? TimeSpan.FromSeconds(10));
        _disposables.Add(instance);
        return instance;
    }

    private static SemaphoreSlim Count(SingleInstance instance)
    {
        var activations = new SemaphoreSlim(0);
        instance.ActivationRequested += (_, _) => activations.Release();
        return activations;
    }

    [Fact]
    public void The_first_copy_acquires_and_a_second_does_not()
    {
        var first = Create();
        var second = Create();

        Assert.True(first.TryAcquire());
        Assert.False(second.TryAcquire());
    }

    [Fact]
    public void Acquiring_twice_on_the_same_object_stays_true()
    {
        var first = Create();

        Assert.True(first.TryAcquire());
        Assert.True(first.TryAcquire());
    }

    [Fact]
    public void Once_the_first_copy_is_disposed_another_can_take_over()
    {
        var first = Create();
        var second = Create();
        Assert.True(first.TryAcquire());

        first.Dispose();

        Assert.True(second.TryAcquire());
    }

    [Fact]
    public void Another_app_id_is_a_different_app()
    {
        var first = Create();
        var other = Create(appId: _appId + "-other");

        Assert.True(first.TryAcquire());
        Assert.True(other.TryAcquire());
    }

    [Fact]
    public void Another_user_is_a_different_app()
    {
        var first = Create();
        var other = Create(user: "another-user");

        Assert.True(first.TryAcquire());
        Assert.True(other.TryAcquire());
    }

    [Fact]
    public async Task A_second_copy_asks_the_first_to_show_itself()
    {
        var first = Create();
        var second = Create();
        Assert.True(first.TryAcquire());
        var activations = Count(first);
        Assert.False(second.TryAcquire());

        var delivered = second.SignalFirstInstance();

        Assert.True(delivered);
        Assert.True(await activations.WaitAsync(TimeSpan.FromSeconds(10), Ct));
    }

    [Fact]
    public async Task Every_request_raises_one_activation()
    {
        var first = Create();
        Assert.True(first.TryAcquire());
        var activations = Count(first);
        var second = Create();

        for (var i = 0; i < 3; i++)
        {
            Assert.True(second.SignalFirstInstance());
        }

        for (var i = 0; i < 3; i++)
        {
            Assert.True(await activations.WaitAsync(TimeSpan.FromSeconds(10), Ct));
        }

        await Task.Delay(200, Ct);
        Assert.Equal(0, activations.CurrentCount);
    }

    [Fact]
    public async Task A_request_that_arrives_before_anyone_listens_is_delivered_to_the_first_listener()
    {
        var first = Create();
        Assert.True(first.TryAcquire());
        var second = Create();
        Assert.True(second.SignalFirstInstance());
        await Task.Delay(300, Ct);

        var activations = Count(first);

        Assert.True(await activations.WaitAsync(TimeSpan.FromSeconds(10), Ct));
        await Task.Delay(200, Ct);
        Assert.Equal(0, activations.CurrentCount);
    }

    [Fact]
    public async Task A_handler_that_throws_does_not_stop_the_next_request_from_being_served()
    {
        var first = Create();
        Assert.True(first.TryAcquire());
        var activations = new SemaphoreSlim(0);
        var calls = 0;
        first.ActivationRequested += (_, _) =>
        {
            activations.Release();
            if (Interlocked.Increment(ref calls) == 1)
            {
                throw new InvalidOperationException("boom");
            }
        };
        var second = Create();

        Assert.True(second.SignalFirstInstance());
        Assert.True(await activations.WaitAsync(TimeSpan.FromSeconds(10), Ct));
        Assert.True(second.SignalFirstInstance());

        Assert.True(await activations.WaitAsync(TimeSpan.FromSeconds(10), Ct));
    }

    [Fact]
    public void With_no_first_copy_running_the_request_is_not_delivered()
    {
        var lonely = new SingleInstance(_appId, _user, PipeDirectory(), TimeSpan.FromMilliseconds(200));
        _disposables.Add(lonely);

        Assert.False(lonely.SignalFirstInstance());
    }

    [Fact]
    public async Task Text_other_than_the_request_is_ignored_and_the_pipe_keeps_serving()
    {
        var first = Create();
        Assert.True(first.TryAcquire());
        var activations = Count(first);

        using (var rogue = new NamedPipeClientStream(".", first.PipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly))
        {
            rogue.Connect(10_000);
            rogue.Write(Encoding.UTF8.GetBytes("hello there\n"));
            rogue.Flush();
            await Task.Delay(300, Ct);
        }

        Assert.Equal(0, activations.CurrentCount);
        Assert.True(Create().SignalFirstInstance());
        Assert.True(await activations.WaitAsync(TimeSpan.FromSeconds(10), Ct));
    }

    [Fact]
    public async Task A_client_that_connects_and_says_nothing_cannot_hold_up_the_next_request_or_shutdown()
    {
        var first = Create();
        Assert.True(first.TryAcquire());
        var activations = Count(first);
        using var silent = new NamedPipeClientStream(".", first.PipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
        silent.Connect(10_000);

        Assert.True(Create().SignalFirstInstance());
        Assert.True(await activations.WaitAsync(TimeSpan.FromSeconds(10), Ct));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        first.Dispose();

        // Stopping the server waits at most two seconds; a prompt answer proves it was told to stop rather than timing out.
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1.5), $"Dispose took {watch.Elapsed}");
    }

    [Fact]
    public void Disposing_twice_is_harmless_and_a_disposed_copy_cannot_acquire()
    {
        var first = Create();
        Assert.True(first.TryAcquire());

        first.Dispose();
        first.Dispose();

        Assert.Throws<ObjectDisposedException>(() => first.TryAcquire());
    }

    [Fact]
    public void A_first_copy_that_crashed_holding_the_mutex_does_not_lock_everyone_out()
    {
        var first = Create();
        var crashed = MutexCrash.LeaveHeld(first.MutexName);

        var acquired = first.TryAcquire();

        GC.KeepAlive(crashed);
        Assert.True(acquired);
    }
    [Fact]
    public void A_socket_file_left_by_a_crashed_copy_does_not_stop_the_next_one_serving()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows pipes disappear with their process; only Unix leaves a file behind.");
        var first = Create();
        Assert.StartsWith("/", first.PipeName, StringComparison.Ordinal);
        using (var stale = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
        {
            stale.Bind(new UnixDomainSocketEndPoint(first.PipeName));
        }

        Assert.True(File.Exists(first.PipeName));

        Assert.True(first.TryAcquire());
        Assert.True(Create().SignalFirstInstance());
    }

    [Fact]
    public void A_pipe_that_cannot_be_created_still_leaves_exactly_one_copy_running()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows pipe names are not paths, so they cannot point at a missing folder.");
        var folder = "/savepoint-no-such-folder-" + Guid.NewGuid().ToString("N");
        var first = Create(pipeDirectory: folder);
        var second = Create(pipeDirectory: folder, connectTimeout: TimeSpan.FromMilliseconds(300));

        Assert.True(first.TryAcquire());
        Assert.False(second.TryAcquire());
        Assert.False(second.SignalFirstInstance());
    }

    [Fact]
    public void A_pipe_name_someone_else_holds_does_not_cost_the_first_copy_its_mutex()
    {
        var first = Create();
        using var squatter = new NamedPipeServerStream(first.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var second = Create(connectTimeout: TimeSpan.FromMilliseconds(300));

        Assert.True(first.TryAcquire());
        Assert.False(second.TryAcquire());
    }

    [Fact]
    public async Task A_request_that_nobody_answers_is_reported_as_not_delivered_instead_of_hanging()
    {
        var first = Create();
        using var squatter = new NamedPipeServerStream(first.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        Assert.True(first.TryAcquire());
        var second = Create(connectTimeout: TimeSpan.FromMilliseconds(300));

        var delivered = await Task.Run(second.SignalFirstInstance, Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.False(delivered);
    }

    [Fact]
    public void The_mutex_name_is_global_and_holds_no_other_backslash()
    {
        var name = new SingleInstance(_appId, _user, "/tmp", TimeSpan.FromSeconds(1)).MutexName;

        Assert.StartsWith("Global\\sp-", name, StringComparison.Ordinal);
        Assert.DoesNotContain('\\', name["Global\\".Length..]);
        Assert.Matches("^Global\\\\sp-[0-9a-f]{16}$", name);
    }

    [Fact]
    public void The_names_follow_the_app_id_and_the_user_and_nothing_else()
    {
        var baseline = new SingleInstance(_appId, _user, "/tmp", TimeSpan.FromSeconds(1));
        var same = new SingleInstance(_appId, _user, "/other", TimeSpan.FromSeconds(1));
        var otherApp = new SingleInstance(_appId + "x", _user, "/tmp", TimeSpan.FromSeconds(1));
        var otherUser = new SingleInstance(_appId, _user + "x", "/tmp", TimeSpan.FromSeconds(1));

        Assert.Equal(baseline.MutexName, same.MutexName);
        Assert.NotEqual(baseline.MutexName, otherApp.MutexName);
        Assert.NotEqual(baseline.MutexName, otherUser.MutexName);
        Assert.NotEqual(otherApp.MutexName, otherUser.MutexName);
    }

    [Fact]
    public void The_app_id_and_user_cannot_be_shuffled_into_each_other()
    {
        var one = new SingleInstance("ab", "c", "/tmp", TimeSpan.FromSeconds(1));
        var two = new SingleInstance("a", "bc", "/tmp", TimeSpan.FromSeconds(1));

        Assert.NotEqual(one.MutexName, two.MutexName);
    }

    [Fact]
    public void On_Unix_the_pipe_is_a_short_socket_path_in_the_given_folder()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows pipe names are not paths.");
        var instance = new SingleInstance(_appId, _user, "/run/user/1000", TimeSpan.FromSeconds(1));

        Assert.StartsWith("/run/user/1000/sp-", instance.PipeName, StringComparison.Ordinal);
        Assert.Matches("^/run/user/1000/sp-[0-9a-f]{12}$", instance.PipeName);
    }

    [Fact]
    public void On_Windows_the_pipe_is_a_bare_name_that_holds_no_slash()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only.");
        var instance = new SingleInstance(_appId, _user, "/tmp", TimeSpan.FromSeconds(1));

        Assert.Matches("^sp-[0-9a-f]{12}$", instance.PipeName);
    }

    public static TheoryData<OsKind, string?, bool, string, string?> PipeFolderCases => new()
    {
        { OsKind.Linux, "/run/user/1000", true, "/tmp/x", "/run/user/1000" },
        { OsKind.Linux, "/run/user/1000", false, "/tmp/x", "/tmp/x" },
        { OsKind.Linux, null, true, "/tmp/x", "/tmp/x" },
        { OsKind.Linux, string.Empty, true, "/tmp/x", "/tmp/x" },
        { OsKind.Linux, "relative/run", true, "/tmp/x", "/tmp/x" },
        { OsKind.MacOS, "/run/user/1000", true, "/var/folders/ab/T", "/var/folders/ab/T" },
        { OsKind.MacOS, null, true, "/var/folders/ab/T", "/var/folders/ab/T" },
    };

    [Theory]
    [MemberData(nameof(PipeFolderCases))]
    public void The_pipe_folder_is_the_runtime_folder_when_the_login_has_a_usable_one(
        OsKind os,
        string? runtimeDir,
        bool runtimeDirExists,
        string tempPath,
        string? expected)
    {
        var folder = SingleInstance.DefaultPipeDirectory(
            os,
            name => name == "XDG_RUNTIME_DIR" ? runtimeDir : null,
            path => runtimeDirExists && path == runtimeDir,
            tempPath);

        Assert.Equal(expected, folder);
    }

    [Fact]
    public void Windows_needs_no_pipe_folder()
    {
        var folder = SingleInstance.DefaultPipeDirectory(OsKind.Windows, _ => "/run/user/1000", _ => true, "C:\\Temp");

        Assert.Null(folder);
    }
}
