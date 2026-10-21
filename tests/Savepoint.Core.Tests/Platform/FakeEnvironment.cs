using Savepoint.Core.Platform;

namespace Savepoint.Core.Tests.Platform;

/// <summary>A machine described as data, so any OS's tables can be built and checked on any host.</summary>
internal sealed class FakeEnvironment
{
    private FakeEnvironment(string home, StringComparer variableComparer)
    {
        Home = home;
        Variables = new Dictionary<string, string>(variableComparer);
    }

    public string Home { get; }

    public Dictionary<string, string> Variables { get; }

    public Dictionary<KnownFolder, string> KnownFolders { get; } = [];

    public List<string> DriveRoots { get; } = [];

    public Dictionary<string, string> Files { get; } = [];

    public List<string> LocalHostNames { get; } = [];

    /// <summary>A Windows user with OneDrive folder backup: Documents and Desktop live under OneDrive.</summary>
    public static FakeEnvironment Windows()
    {
        var env = new FakeEnvironment(@"C:\Users\Test", StringComparer.OrdinalIgnoreCase);
        env.Variables["APPDATA"] = @"C:\Users\Test\AppData\Roaming";
        env.Variables["LOCALAPPDATA"] = @"C:\Users\Test\AppData\Local";
        env.Variables["SystemRoot"] = @"C:\Windows";
        env.Variables["SystemDrive"] = "C:";
        env.Variables["ProgramFiles"] = @"C:\Program Files";
        env.Variables["ProgramFiles(x86)"] = @"C:\Program Files (x86)";
        env.Variables["ProgramW6432"] = @"C:\Program Files";
        env.Variables["ProgramData"] = @"C:\ProgramData";
        env.Variables["PUBLIC"] = @"C:\Users\Public";
        env.Variables["OneDrive"] = @"C:\Users\Test\OneDrive";
        env.KnownFolders[KnownFolder.Documents] = @"C:\Users\Test\OneDrive\Documents";
        env.KnownFolders[KnownFolder.Desktop] = @"C:\Users\Test\OneDrive\Desktop";
        env.KnownFolders[KnownFolder.Downloads] = @"C:\Users\Test\Downloads";
        env.KnownFolders[KnownFolder.SavedGames] = @"C:\Users\Test\Saved Games";
        env.KnownFolders[KnownFolder.LocalLow] = @"C:\Users\Test\AppData\LocalLow";
        env.DriveRoots.AddRange([@"C:\", @"D:\"]);
        env.LocalHostNames.AddRange(["TESTPC", "testpc.corp.example"]);
        return env;
    }

    public static FakeEnvironment Linux() => new("/home/test", StringComparer.Ordinal);

    public static FakeEnvironment MacOS() => new("/Users/test", StringComparer.Ordinal);

    public PlatformEnvironment Build() => new()
    {
        HomeDirectory = Home,
        GetVariable = name => Variables.GetValueOrDefault(name),
        GetKnownFolder = folder => KnownFolders.GetValueOrDefault(folder),
        ListDriveRoots = () => [.. DriveRoots],
        ReadTextFile = path => Files.GetValueOrDefault(path),
        LocalHostNames = () => [.. LocalHostNames],
    };
}
