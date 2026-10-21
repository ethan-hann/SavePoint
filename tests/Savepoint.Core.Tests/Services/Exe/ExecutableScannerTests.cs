using Savepoint.Core.Services.Exe;

namespace Savepoint.Core.Tests.Services.Exe;

public class ExecutableScannerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string P(params string[] parts) => Path.Combine(parts);

    private static Task<IReadOnlyList<string>> ScanAsync(TempFolder folder, ExecutableKinds kinds = ExecutableKinds.WindowsExe) =>
        new ExecutableScanner(kinds).ListAsync(folder.Path, Ct);

    [Fact]
    public async Task Windows_executables_are_listed_at_any_depth_as_paths_relative_to_the_folder()
    {
        using var temp = new TempFolder();
        temp.WriteBytes("Game.exe", 10);
        temp.WriteBytes("bin/x64/Game64.exe", 10);
        temp.WriteBytes("readme.txt", 10);
        temp.WriteBytes("bin/helper.dll", 10);

        var found = await ScanAsync(temp);

        Assert.Equal([P("Game.exe"), P("bin", "x64", "Game64.exe")], found.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task The_extension_is_matched_in_any_case()
    {
        using var temp = new TempFolder();
        temp.WriteBytes("GAME.EXE", 10);

        Assert.Equal([P("GAME.EXE")], await ScanAsync(temp));
    }

    [Theory]
    [InlineData("unins000.exe")]
    [InlineData("uninstall.exe")]
    [InlineData("UNINS001.EXE")]
    [InlineData("UnityCrashHandler64.exe")]
    [InlineData("CrashReportClient.exe")]
    [InlineData("vc_redist.x64.exe")]
    [InlineData("vcredist_x86.exe")]
    [InlineData("dxsetup.exe")]
    [InlineData("DXSETUP.exe")]
    [InlineData("dxwebsetup.exe")]
    [InlineData("dotnetfx35.exe")]
    [InlineData("directx_Jun2010_redist.exe")]
    [InlineData("oalinst.exe")]
    [InlineData("EasyAntiCheat_Setup.exe")]
    [InlineData("EasyAntiCheat.exe")]
    [InlineData("BEService.exe")]
    [InlineData("Setup.exe")]
    public async Task Installers_redistributables_and_crash_handlers_are_not_suggested(string name)
    {
        using var temp = new TempFolder();
        temp.WriteBytes(name, 10);
        temp.WriteBytes("Game.exe", 10);

        Assert.Equal([P("Game.exe")], await ScanAsync(temp));
    }

    [Theory]
    [InlineData("Skyrim.exe")]
    [InlineData("game-launcher.exe")]
    [InlineData("Binaries/Win64/MyGame-Win64-Shipping.exe")]
    [InlineData("Engine/Binaries/Win64/Game.exe")]
    public async Task Ordinary_game_executables_are_suggested(string relative)
    {
        using var temp = new TempFolder();
        temp.WriteBytes(relative, 10);

        Assert.Equal([relative.Replace('/', Path.DirectorySeparatorChar)], await ScanAsync(temp));
    }

    [Theory]
    [InlineData("_CommonRedist/vcredist/Game.exe")]
    [InlineData("_commonredist/Game.exe")]
    [InlineData("redist/Game.exe")]
    [InlineData("vc_redist/Game.exe")]
    [InlineData("Support/Game.exe")]
    [InlineData("__installer/Game.exe")]
    [InlineData("MonoBleedingEdge/EmbedRuntime/Game.exe")]
    [InlineData("Engine/Binaries/ThirdParty/Vendor/Game.exe")]
    [InlineData("engine/binaries/thirdparty/Game.exe")]
    [InlineData(".hidden/Game.exe")]
    public async Task Heavy_and_irrelevant_folders_are_not_entered(string relative)
    {
        using var temp = new TempFolder();
        temp.WriteBytes(relative, 10);
        temp.WriteBytes("Real.exe", 10);

        Assert.Equal([P("Real.exe")], await ScanAsync(temp));
    }

    [Fact]
    public async Task A_folder_marked_hidden_is_not_entered()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the hidden attribute is a Windows file system feature");
        using var temp = new TempFolder();
        temp.WriteBytes("shadow/Game.exe", 10);
        File.SetAttributes(temp.Combine("shadow"), FileAttributes.Hidden | FileAttributes.Directory);
        temp.WriteBytes("Real.exe", 10);

        Assert.Equal([P("Real.exe")], await ScanAsync(temp));
    }

    [Fact]
    public async Task A_thirdparty_folder_elsewhere_in_the_tree_is_still_entered()
    {
        using var temp = new TempFolder();
        temp.WriteBytes("ThirdParty/Game.exe", 10);

        Assert.Equal([P("ThirdParty", "Game.exe")], await ScanAsync(temp));
    }

    [Fact]
    public async Task Folders_are_read_down_to_four_levels_below_the_install_folder_and_no_further()
    {
        using var temp = new TempFolder();
        temp.WriteBytes("a/b/c/d/AtFour.exe", 10);
        temp.WriteBytes("a/b/c/d/e/AtFive.exe", 10);

        Assert.Equal([P("a", "b", "c", "d", "AtFour.exe")], await ScanAsync(temp));
    }

    [Fact]
    public async Task The_most_likely_game_executable_comes_first_shallow_before_deep_then_large_before_small()
    {
        using var temp = new TempFolder();
        temp.WriteBytes("small.exe", 10);
        temp.WriteBytes("big.exe", 1000);
        temp.WriteBytes("bin/deep.exe", 50000);
        temp.WriteBytes("bin/deeper/deepest.exe", 90000);

        var found = await ScanAsync(temp);

        Assert.Equal([P("big.exe"), P("small.exe"), P("bin", "deep.exe"), P("bin", "deeper", "deepest.exe")], found);
    }

    [Fact]
    public async Task Equal_candidates_are_ordered_by_name_so_the_result_is_stable()
    {
        using var temp = new TempFolder();
        temp.WriteBytes("b.exe", 10);
        temp.WriteBytes("a.exe", 10);
        temp.WriteBytes("c.exe", 10);

        Assert.Equal([P("a.exe"), P("b.exe"), P("c.exe")], await ScanAsync(temp));
    }

    [Fact]
    public async Task At_most_fifty_paths_come_back_and_they_are_the_best_fifty()
    {
        using var temp = new TempFolder();
        for (var i = 0; i < 30; i++)
        {
            temp.WriteBytes($"root{i:D2}.exe", 100 + i);
        }

        for (var i = 0; i < 40; i++)
        {
            temp.WriteBytes($"sub/deep{i:D2}.exe", 100 + i);
        }

        var found = await ScanAsync(temp);

        Assert.Equal(50, found.Count);
        Assert.Equal(30, found.Count(path => !path.Contains(Path.DirectorySeparatorChar)));
        Assert.Equal(P("sub", "deep39.exe"), found[30]);
        Assert.Equal(P("sub", "deep20.exe"), found[^1]);
    }

    [Fact]
    public async Task A_scan_stops_after_five_thousand_entries()
    {
        using var temp = new TempFolder();
        for (var i = 0; i < 5100; i++)
        {
            File.WriteAllBytes(temp.Combine($"f{i:D5}.dat"), []);
        }

        temp.WriteBytes("sub/Game.exe", 10);

        Assert.Empty(await ScanAsync(temp));
    }

    [Fact]
    public async Task A_folder_just_under_the_entry_limit_is_scanned_completely()
    {
        using var temp = new TempFolder();
        for (var i = 0; i < 4990; i++)
        {
            File.WriteAllBytes(temp.Combine($"f{i:D5}.dat"), []);
        }

        temp.WriteBytes("sub/Game.exe", 10);

        Assert.Equal([P("sub", "Game.exe")], await ScanAsync(temp));
    }

    [Fact]
    public async Task The_install_folders_own_files_are_seen_even_when_a_subfolder_alone_would_use_up_the_limit()
    {
        using var temp = new TempFolder();
        temp.WriteBytes("Game.exe", 10);
        Directory.CreateDirectory(temp.Combine("deep"));
        for (var i = 0; i < 5000; i++)
        {
            File.WriteAllBytes(temp.Combine("deep", $"f{i:D5}.dat"), []);
        }

        Assert.Equal([P("Game.exe")], await ScanAsync(temp));
    }

    [Fact]
    public async Task A_missing_folder_or_a_file_in_its_place_lists_nothing()
    {
        using var temp = new TempFolder();
        var file = temp.WriteBytes("Game.exe", 10);

        Assert.Empty(await new ExecutableScanner(ExecutableKinds.WindowsExe).ListAsync(temp.Combine("missing"), Ct));
        Assert.Empty(await new ExecutableScanner(ExecutableKinds.WindowsExe).ListAsync(file, Ct));
    }

    [Fact]
    public async Task Links_are_not_followed_or_listed()
    {
        using var temp = new TempFolder();
        temp.WriteBytes("tree/Real.exe", 10);
        temp.WriteBytes("outside/Elsewhere.exe", 10);
        Assert.True(temp.TryCreateDirectoryLink(temp.Combine("tree", "link"), temp.Combine("outside")), "could not create a directory link on this machine");

        var found = await new ExecutableScanner(ExecutableKinds.WindowsExe).ListAsync(temp.Combine("tree"), Ct);

        Assert.Equal([P("Real.exe")], found);
    }

    [Fact]
    public async Task A_token_that_is_already_cancelled_stops_the_scan()
    {
        using var temp = new TempFolder();
        temp.WriteBytes("Game.exe", 10);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ExecutableScanner(ExecutableKinds.WindowsExe).ListAsync(temp.Path, cts.Token));
    }

    [Fact]
    public void Cancelling_during_the_scan_stops_it()
    {
        using var temp = new TempFolder();
        for (var i = 0; i < 100; i++)
        {
            temp.WriteBytes($"f{i:D3}.exe", 1);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var visited = 0;
        var scanner = new ExecutableScanner(ExecutableKinds.WindowsExe)
        {
            Visited = _ =>
            {
                if (++visited == 3)
                {
                    cts.Cancel();
                }
            },
        };

        Assert.ThrowsAny<OperationCanceledException>(() => scanner.List(temp.Path, cts.Token));
        Assert.True(visited < 100, $"the scan went on after the cancel and visited {visited} entries");
    }

    [Fact]
    public async Task Only_the_kinds_asked_for_are_suggested()
    {
        using var temp = new TempFolder();
        temp.WriteBytes("Game.exe", 10);
        temp.WriteBytes("run.sh", 10);
        temp.WriteBytes("Game.app/Contents/MacOS/Game", 10);

        Assert.Equal([P("Game.exe")], await ScanAsync(temp, ExecutableKinds.WindowsExe));
        Assert.Equal([P("run.sh")], await ScanAsync(temp, ExecutableKinds.LinuxNative));
        Assert.Equal([P("Game.app")], await ScanAsync(temp, ExecutableKinds.MacApp));
        Assert.Equal([P("Game.exe"), P("run.sh"), P("Game.app")], await ScanAsync(temp, ExecutableKinds.WindowsExe | ExecutableKinds.LinuxNative | ExecutableKinds.MacApp));
        Assert.Empty(await ScanAsync(temp, ExecutableKinds.None));
    }

    [Theory]
    [InlineData("start.sh")]
    [InlineData("Start.SH")]
    [InlineData("Game.x86_64")]
    [InlineData("Game.X86_64")]
    public async Task Linux_scripts_and_x86_64_builds_are_suggested_by_extension_alone(string name)
    {
        using var temp = new TempFolder();
        temp.WriteBytes(name, 10);

        Assert.Equal([P(name)], await ScanAsync(temp, ExecutableKinds.LinuxNative));
    }

    [Fact]
    public async Task Hidden_files_and_noise_names_are_not_suggested_on_linux_either()
    {
        using var temp = new TempFolder();
        temp.WriteBytes(".hidden.sh", 10);
        temp.WriteBytes("setup.sh", 10);
        temp.WriteBytes("UnityCrashHandler64.x86_64", 10);
        temp.WriteBytes("play.sh", 10);

        Assert.Equal([P("play.sh")], await ScanAsync(temp, ExecutableKinds.LinuxNative));
    }

    [Fact]
    public async Task Files_marked_executable_are_suggested_only_when_they_are_real_programs()
    {
        var elf = new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1, 0 };
        using var temp = new TempFolder();
        temp.WriteBytesWith("native-elf", elf);
        temp.WriteBytesWith("bin/launcher", "#!/bin/sh\nexec ./native-elf\n"u8.ToArray());
        temp.WriteBytesWith("data-marked-executable.bin", [1, 2, 3, 4, 5, 6]);
        temp.WriteBytesWith("libengine.so", elf);
        temp.WriteBytesWith("libengine.so.2", elf);
        temp.WriteBytesWith("empty-but-executable", []);
        temp.WriteBytesWith("short-but-executable", [0x7F]);
        temp.WriteBytesWith("elf-without-the-bit", elf);
        var marked = new HashSet<string>(["native-elf", "launcher", "data-marked-executable.bin", "libengine.so", "libengine.so.2", "empty-but-executable", "short-but-executable"]);
        var scanner = new ExecutableScanner(ExecutableKinds.LinuxNative) { HasExecuteBit = path => marked.Contains(Path.GetFileName(path)) };

        var found = await scanner.ListAsync(temp.Path, Ct);

        Assert.Equal([P("bin", "launcher"), "native-elf"], found.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Windows_programs_and_libraries_are_not_native_programs_even_when_marked_executable()
    {
        var elf = new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F' };
        using var temp = new TempFolder();
        temp.WriteBytesWith("Game.exe", elf);
        temp.WriteBytesWith("helper.dll", elf);
        temp.WriteBytesWith("libfoo.dylib", elf);
        var scanner = new ExecutableScanner(ExecutableKinds.LinuxNative) { HasExecuteBit = _ => true };
        var withProton = new ExecutableScanner(ExecutableKinds.LinuxNative | ExecutableKinds.WindowsExe) { HasExecuteBit = _ => true };

        Assert.Empty(await scanner.ListAsync(temp.Path, Ct));
        Assert.Equal([P("Game.exe")], await withProton.ListAsync(temp.Path, Ct));
    }

    [Fact]
    public async Task The_real_execute_bit_decides_on_unix()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows has no execute bit");
        var elf = new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1, 0 };
        using var temp = new TempFolder();
        Executable(temp.WriteBytesWith("with-the-bit", elf));
        temp.WriteBytesWith("without-the-bit", elf);

        Assert.Equal(["with-the-bit"], await ScanAsync(temp, ExecutableKinds.LinuxNative));
    }

    [Fact]
    public async Task An_app_bundle_is_suggested_as_the_bundle_and_never_entered()
    {
        using var temp = new TempFolder();
        temp.WriteBytes("Game.app/Contents/MacOS/Game", 100);
        temp.WriteBytes("Game.app/Contents/Resources/Helper.app/Contents/MacOS/Helper", 100);
        temp.WriteBytes("Games/Other.APP/Contents/MacOS/Other", 100);
        temp.WriteBytes("Plain.exe", 10);

        var found = await ScanAsync(temp, ExecutableKinds.MacApp | ExecutableKinds.WindowsExe);

        Assert.Equal([P("Plain.exe"), P("Game.app"), P("Games", "Other.APP")], found);
    }

    [Fact]
    public async Task Without_the_mac_kind_an_app_folder_is_an_ordinary_folder()
    {
        using var temp = new TempFolder();
        temp.WriteBytes("Tool.app/Game.exe", 10);

        Assert.Equal([P("Tool.app", "Game.exe")], await ScanAsync(temp, ExecutableKinds.WindowsExe));
    }

    [Fact]
    public void The_default_kinds_follow_the_operating_system()
    {
        var kinds = ExecutableScanner.KindsForCurrentOs;

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(ExecutableKinds.WindowsExe, kinds);
        }
        else if (OperatingSystem.IsMacOS())
        {
            Assert.Equal(ExecutableKinds.MacApp, kinds);
        }
        else
        {
            Assert.Equal(ExecutableKinds.LinuxNative | ExecutableKinds.WindowsExe, kinds);
        }
    }

    private static void Executable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows has no execute bit.");
        }

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
    }
}
