using Savepoint.Core.Platform;
using Savepoint.Core.Platform.Linux;
using Savepoint.Core.Platform.MacOS;
using Savepoint.Core.Platform.Windows;
using Savepoint.Core.Tests.Services;
using Savepoint.Core.Tests.Services.Exe;

namespace Savepoint.Core.Tests.Launch;

// A private copy of the stand-in game under a process name no other test shares, so a test that looks for the game by name
// never sees the FakeGame processes other tests start in parallel. Disposing kills whatever is still running under that name.
internal sealed class FakeGameInstall : IDisposable
{
    private readonly TempFolder _temp = new("savepoint-launch-");

    public FakeGameInstall()
    {
        Name = "fg" + Guid.NewGuid().ToString("N")[..6];
        Folder = _temp.Combine("install");
        Directory.CreateDirectory(Folder);

        var source = Path.GetDirectoryName(FakeGameProcess.ExecutablePath)!;
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(Folder, Path.GetFileName(file)));
        }

        File.Move(Path.Combine(Folder, Path.GetFileName(FakeGameProcess.ExecutablePath)), ExecutablePath);
    }

    // At most eight characters, so the kernel's 15-character process name keeps it whole.
    public string Name { get; }

    public string Folder { get; }

    public string ExecutablePath => Path.Combine(Folder, Name + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));

    public string Combine(params string[] parts) => _temp.Combine(parts);

    public static IProcessInspector Inspector() =>
        OperatingSystem.IsWindows() ? new WindowsProcessInspector()
        : OperatingSystem.IsMacOS() ? new MacOSProcessInspector()
        : new LinuxProcessInspector();

    public IReadOnlyList<int> RunningIds() =>
        [.. Inspector().List().Where(p => ProcessNameMatcher.Matches(Name, p, PlatformFactory.CurrentOs)).Select(p => p.Pid)];

    public void KillAll()
    {
        foreach (var id in RunningIds())
        {
            FakeGameProcess.Kill(id);
        }
    }

    public void Dispose()
    {
        KillAll();
        _temp.Dispose();
    }
}
