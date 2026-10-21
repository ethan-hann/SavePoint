using System.Diagnostics;
using Tmds.DBus.Protocol;

namespace Savepoint.App.Tests.Platform.Fakes;

// A throwaway D-Bus daemon on an abstract socket (no file, no path limit) so a test can use a real bus; disposing it kills the daemon.
internal sealed class PrivateSessionBus : IDisposable
{
    private readonly Process _daemon;
    private readonly string _configPath;

    private PrivateSessionBus(Process daemon, string address, string configPath)
    {
        _daemon = daemon;
        Address = address;
        _configPath = configPath;
    }

    public string Address { get; }

    public static string? FindDaemon() =>
        new[] { "/usr/bin/dbus-daemon", "/bin/dbus-daemon" }.FirstOrDefault(File.Exists);

    public static PrivateSessionBus? TryStart()
    {
        var daemonPath = FindDaemon();
        if (!OperatingSystem.IsLinux() || daemonPath is null)
        {
            return null;
        }

        var socket = "savepoint-test-" + Guid.NewGuid().ToString("N");
        var configPath = Path.Combine(Path.GetTempPath(), socket + ".conf");
        File.WriteAllText(
            configPath,
            $"""
            <busconfig>
              <type>session</type>
              <listen>unix:abstract={socket}</listen>
              <auth>EXTERNAL</auth>
              <policy context="default">
                <allow send_destination="*" eavesdrop="true"/>
                <allow eavesdrop="true"/>
                <allow own="*"/>
              </policy>
            </busconfig>
            """);

        var info = new ProcessStartInfo(daemonPath) { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
        info.ArgumentList.Add("--config-file=" + configPath);
        info.ArgumentList.Add("--nofork");
        info.ArgumentList.Add("--nopidfile");
        info.ArgumentList.Add("--print-address=1");
        var daemon = Process.Start(info) ?? throw new InvalidOperationException("dbus-daemon did not start.");
        try
        {
            var address = daemon.StandardOutput.ReadLine();
            if (string.IsNullOrWhiteSpace(address))
            {
                throw new InvalidOperationException("dbus-daemon printed no address.");
            }

            return new PrivateSessionBus(daemon, address.Trim(), configPath);
        }
        catch
        {
            daemon.Kill(entireProcessTree: true);
            daemon.Dispose();
            File.Delete(configPath);
            throw;
        }
    }

    public void Dispose()
    {
        if (!_daemon.HasExited)
        {
            _daemon.Kill(entireProcessTree: true);
            _daemon.WaitForExit();
        }

        _daemon.Dispose();
        File.Delete(_configPath);
    }
}

// Plays org.freedesktop.Notifications on a bus and records what Notify received.
internal sealed class FakeNotificationServer : IPathMethodHandler, IDisposable
{
    private readonly DBusConnection _connection;
    private readonly TaskCompletionSource<ReceivedNotify> _received = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private FakeNotificationServer(DBusConnection connection)
    {
        _connection = connection;
    }

    public string Path => "/org/freedesktop/Notifications";

    public bool HandlesChildPaths => false;

    public Task<ReceivedNotify> Received => _received.Task;

    public static async Task<FakeNotificationServer> StartAsync(string address)
    {
        var server = new FakeNotificationServer(new DBusConnection(address));
        await server._connection.ConnectAsync();
        server._connection.AddMethodHandler(server);
        await server._connection.RequestNameAsync("org.freedesktop.Notifications", RequestNameOptions.Default);
        return server;
    }

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        var request = context.Request;
        if (request.InterfaceAsString != "org.freedesktop.Notifications" || request.MemberAsString != "Notify")
        {
            context.ReplyUnknownMethodError();
            return ValueTask.CompletedTask;
        }

        var reader = request.GetBodyReader();
        var notify = new ReceivedNotify(
            request.SignatureAsString ?? string.Empty,
            request.DestinationAsString ?? string.Empty,
            request.PathAsString ?? string.Empty,
            reader.ReadString(),
            reader.ReadUInt32(),
            reader.ReadString(),
            reader.ReadString(),
            reader.ReadString(),
            reader.ReadArrayOfString(),
            reader.ReadDictionaryOfStringToVariantValue().Count,
            reader.ReadInt32());

        using (var writer = context.CreateReplyWriter("u"))
        {
            writer.WriteUInt32(42);
            context.Reply(writer.CreateMessage());
        }

        _received.TrySetResult(notify);
        return ValueTask.CompletedTask;
    }

    public void Dispose() => _connection.Dispose();
}

internal sealed record ReceivedNotify(
    string Signature,
    string Destination,
    string Path,
    string AppName,
    uint ReplacesId,
    string AppIcon,
    string Summary,
    string Body,
    string[] Actions,
    int HintCount,
    int ExpireTimeoutMilliseconds);
