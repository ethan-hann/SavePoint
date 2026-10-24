using Savepoint.Core.Launch;

namespace Savepoint.Core.Tests.Launch;

// The fixtures are the relative paths and sizes (in KB) of real installs on one PC; no binaries are involved.
public class ProcessNameSuggesterTests
{
    private static InstalledProgram[] Files(params string[] entries) =>
        [.. entries.Select(entry =>
        {
            var split = entry.LastIndexOf('|');
            return new InstalledProgram(entry[..split], long.Parse(entry[(split + 1)..]) * 1024);
        })];

    private static string? Suggest(string folder, params string[] entries) =>
        ProcessNameSuggester.Suggest(folder, null, Files(entries));

    [Fact]
    public void A_folder_with_no_programs_has_no_suggestion()
    {
        Assert.Null(Suggest("Empty"));
    }

    [Fact]
    public void A_unity_game_is_its_own_small_program_at_the_top()
    {
        var suggestion = Suggest("Subnautica", "Subnautica.exe|636", "Subnautica32.exe|68", @"BepInEx\plugins\DecorationsMods\Configurator\DecorationsModConfigurator.exe|7475");

        Assert.Equal("Subnautica.exe", suggestion);
    }

    [Fact]
    public void An_unreal_game_is_the_shipping_program_not_the_small_starter_beside_it()
    {
        var suggestion = Suggest(
            "StellarBlade",
            "crs-handler.exe|1237",
            "crs-uploader.exe|839",
            "SB.exe|449",
            @"Engine\Binaries\Win64\UnrealCEFSubProcess.exe|3563",
            @"SB\Binaries\Win64\crs-handler.exe|1237",
            @"SB\Binaries\Win64\SB-Win64-Shipping.exe|350768");

        Assert.Equal("SB-Win64-Shipping.exe", suggestion);
    }

    [Fact]
    public void An_unreal_game_without_a_shipping_name_is_the_large_program_under_binaries()
    {
        var suggestion = Suggest("Hogwarts Legacy", "HogwartsLegacy.exe|283", @"Phoenix\Binaries\Win64\HogwartsLegacy.exe|440094");

        Assert.Equal("HogwartsLegacy.exe", suggestion);
    }

    [Fact]
    public void The_shipping_program_in_the_game_folder_beats_the_engine_copy_of_the_same_name()
    {
        var suggestion = Suggest(
            "Satisfactory",
            "FactoryGameSteam.exe|212",
            @"Engine\Binaries\Win64\FactoryGameSteam-Win64-Shipping.exe|264",
            @"FactoryGame\Binaries\Win64\FactoryGame-Win64-Shipping.exe|68176");

        Assert.Equal("FactoryGame-Win64-Shipping.exe", suggestion);
    }

    [Fact]
    public void A_source_game_is_the_program_at_the_top_not_the_authoring_tools_in_bin()
    {
        var suggestion = Suggest(
            "half-life 2",
            "hl2.exe|120",
            @"bin\bspzip.exe|296",
            @"bin\hammer.exe|154",
            @"bin\hlfaceposer.exe|1624",
            @"bin\studiomdl.exe|1752",
            @"bin\vbsp.exe|1214",
            @"bin\vrad.exe|94",
            @"bin\vtex.exe|83",
            @"bin\vvis.exe|84");

        Assert.Equal("hl2.exe", suggestion);
    }

    [Fact]
    public void Anti_cheat_bootstraps_and_launchers_are_never_the_suggestion()
    {
        var suggestion = Suggest(
            "ELDEN RING",
            @"Game\eldenring.exe|84935",
            @"Game\ersc_launcher.exe|172",
            @"Game\start_protected_game.exe|3884",
            @"Game\EasyAntiCheat\easyanticheat_eos_setup.exe|943");

        Assert.Equal("eldenring.exe", suggestion);
    }

    [Fact]
    public void Battleye_and_other_bootstrap_variants_lose_to_the_game_program()
    {
        var suggestion = Suggest(
            "PUBG",
            @"TslGame\Binaries\Win64\ExecPubg.exe|3898",
            @"TslGame\Binaries\Win64\TslGame_BE.exe|716",
            @"TslGame\Binaries\Win64\TslGame_EAC.exe|1117",
            @"TslGame\Binaries\Win64\TslGame_UC.exe|11732",
            @"TslGame\Binaries\Win64\TslGame_ZK.exe|13868",
            @"TslGame\Binaries\Win64\TslGame.exe|190279",
            @"TslGame\Binaries\Win64\BattlEye\BEService_x64.exe|8686");

        Assert.Equal("TslGame.exe", suggestion);
    }

    [Fact]
    public void A_launcher_with_the_games_own_name_loses_to_the_game_program()
    {
        var suggestion = Suggest("Skyrim Special Edition", "skse64_loader.exe|220", "SkyrimSE.exe|37022", "SkyrimSELauncher.exe|4613");

        Assert.Equal("SkyrimSE.exe", suggestion);
    }

    [Fact]
    public void A_launcher_that_is_larger_than_the_game_still_loses()
    {
        var suggestion = Suggest("Mafia Definitive Edition", "launcher.exe|80572", "mafiadefinitiveedition.exe|108823");

        Assert.Equal("mafiadefinitiveedition.exe", suggestion);
    }

    [Fact]
    public void A_game_under_bin_beats_the_redmod_tools_and_the_error_reporter()
    {
        var suggestion = Suggest(
            "Cyberpunk 2077",
            "REDprelauncher.exe|1559",
            @"bin\x64\Cyberpunk2077.exe|58541",
            @"bin\x64\REDEngineErrorReporter.exe|256",
            @"tools\redmod\bin\redMod.exe|13325",
            @"tools\redmod\bin\scc.exe|6789");

        Assert.Equal("Cyberpunk2077.exe", suggestion);
    }

    [Fact]
    public void Crash_reporters_and_web_helpers_are_never_the_suggestion()
    {
        var suggestion = Suggest(
            "Arc Raiders",
            "PioneerGame.exe|12485",
            @"Engine\Binaries\Win64\EpicWebHelper.exe|4758",
            @"PioneerGame\Binaries\Win64\PioneerGame.exe|316639");

        Assert.Equal("PioneerGame.exe", suggestion);
    }

    [Fact]
    public void Redistributables_and_uninstallers_are_never_the_suggestion()
    {
        var suggestion = Suggest(
            "Divinity",
            @"DotNetCore\windowsdesktop-runtime-3.1.8-win-x64.exe|52982",
            @"_CommonRedist\vcredist\2015\vc_redist.x64.exe|14000",
            "unins000.exe|1200",
            "Game.exe|20000");

        Assert.Equal("Game.exe", suggestion);
    }

    [Fact]
    public void A_name_that_resembles_the_folder_wins_over_a_slightly_larger_helper()
    {
        var suggestion = Suggest("Far Cry 4", "GDFInstall.exe|92", @"bin\FarCry4.exe|98", @"bin\IGE_WPF64.exe|1412");

        Assert.Equal("FarCry4.exe", suggestion);
    }

    [Fact]
    public void The_game_name_helps_when_the_folder_name_does_not()
    {
        var files = Files("tool.exe|900", "zz.exe|800", "Odd.exe|700");

        Assert.Equal("Odd.exe", ProcessNameSuggester.Suggest("x1", "Odd Jobs", files));
    }

    [Fact]
    public void A_variant_with_original_in_its_name_loses_to_the_plain_program()
    {
        var suggestion = Suggest("Balatro", "Balatro_original.exe|55060", "Balatro.exe|54728");

        Assert.Equal("Balatro.exe", suggestion);
    }

    [Fact]
    public void Both_kinds_of_separator_are_understood()
    {
        var suggestion = Suggest("StellarBlade", "SB.exe|449", "SB/Binaries/Win64/SB-Win64-Shipping.exe|350768");

        Assert.Equal("SB-Win64-Shipping.exe", suggestion);
    }

    [Fact]
    public void A_game_whose_name_holds_a_helper_word_is_still_a_game()
    {
        Assert.Equal("CrimeCleaner.exe", Suggest("Crime Scene Cleaner", "CrimeCleaner.exe|638", @"MelonLoader\net35\MonoMod.exe|66"));
    }

    [Fact]
    public void A_runtime_with_hundreds_of_tools_is_not_a_game()
    {
        var wine = Enumerable.Range(1, 40).Select(n => $@"runtime\wine\fakedlls\tool{n}.exe|{n}").ToArray();

        Assert.Null(Suggest("Proton 4.2", wine));
    }

    [Fact]
    public void A_folder_of_vr_runtime_services_is_not_a_game()
    {
        var suggestion = Suggest(
            "SteamVR",
            @"bin\win64\vrserver.exe|4746",
            @"bin\win64\vrcompositor.exe|3714",
            @"bin\win64\vrmonitor.exe|2563",
            @"bin\win64\vrservice.exe|2270",
            @"bin\win64\vrcmd.exe|399",
            @"bin\win64\vrdashboard.exe|254",
            @"bin\win64\vrstartup.exe|251",
            @"bin\win64\vrurlhandler.exe|215");

        Assert.Null(suggestion);
    }

    [Fact]
    public void Only_noise_gives_no_suggestion()
    {
        Assert.Null(Suggest("Game", "Launcher.exe|900", "unins000.exe|100", "CrashReporter.exe|50"));
    }

    [Fact]
    public void The_same_input_always_gives_the_same_answer()
    {
        var files = Files("Control_DX11.exe|19992", "Control_DX12.exe|19992", "Control.exe|88");

        Assert.Equal(ProcessNameSuggester.Suggest("Control", null, files), ProcessNameSuggester.Suggest("Control", null, files.Reverse().ToArray()));
    }
}
