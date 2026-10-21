using Savepoint.Core.Catalog;
using static Savepoint.Core.Tests.Catalog.CatalogSamples;

namespace Savepoint.Core.Tests.Catalog;

public class CatalogIdsTests
{
    // Draws the alphabet indexes "a", "b", "c", "9".
    private static Random Abc9() => new ScriptedRandom(0, 1, 2, 35);

    public static TheoryData<string, string> GameSlugs => new()
    {
        { "Skyrim", "skyrim" },
        { "  The Witcher 3: Wild Hunt!! ", "the-witcher-3-wild-hunt" },
        { "A--B__C", "a-b-c" },
        { "UPPER lower 42", "upper-lower-42" },
        { "Café Society", "caf-society" },
        { "--leading and trailing--", "leading-and-trailing" },
        { "", "game" },
        { "   ", "game" },
        { "!!!", "game" },
        { "日本語", "game" },
    };

    [Theory]
    [MemberData(nameof(GameSlugs))]
    public void Game_id_is_a_lowercase_ascii_slug_followed_by_the_suffix(string name, string slug)
    {
        Assert.Equal($"{slug}-abc9", CatalogIds.NewGameId(name, Abc9()));
    }

    [Fact]
    public void Slug_longer_than_forty_characters_is_cut_and_never_ends_with_a_dash()
    {
        var name = new string('a', 39) + " bbbbbbbb";

        var id = CatalogIds.NewGameId(name, Abc9());

        Assert.Equal(new string('a', 39) + "-abc9", id);
    }

    [Fact]
    public void Slug_of_exactly_forty_characters_is_kept_whole()
    {
        var name = new string('a', 40);

        Assert.Equal(new string('a', 40) + "-abc9", CatalogIds.NewGameId(name, Abc9()));
    }

    [Fact]
    public void Suffix_is_drawn_from_the_random_source_over_lowercase_letters_and_digits()
    {
        Assert.Equal("game-abyz", CatalogIds.NewGameId("", new ScriptedRandom(0, 1, 24, 25)));
        Assert.Equal("game-0189", CatalogIds.NewGameId("", new ScriptedRandom(26, 27, 34, 35)));
    }

    [Fact]
    public void Default_random_source_always_yields_a_four_character_lowercase_alphanumeric_suffix()
    {
        for (var i = 0; i < 500; i++)
        {
            Assert.Matches(@"^skyrim-[a-z0-9]{4}\z", CatalogIds.NewGameId("Skyrim"));
        }
    }

    [Fact]
    public void Generated_game_ids_pass_validation()
    {
        foreach (var name in new[] { "Skyrim", "", "日本語", new string('z', 90) })
        {
            var game = ExeGame(CatalogIds.NewGameId(name));

            Assert.Empty(CatalogValidator.Validate(game));
        }
    }

    [Fact]
    public void New_profile_gets_the_slug_id_and_a_branch_named_after_it()
    {
        var profile = CatalogIds.NewProfile("skyrim-se-ab12", "Modded playthrough!", Abc9());

        Assert.Equal("modded-playthrough-abc9", profile.Id);
        Assert.Equal("profile/modded-playthrough-abc9", profile.BranchName);
        Assert.Equal("skyrim-se-ab12", profile.GameId);
        Assert.Equal("Modded playthrough!", profile.Name);
        Assert.Null(profile.Notes);
        Assert.Null(profile.LastLaunchedAt);
        Assert.Null(profile.VersionOverride);
    }

    [Fact]
    public void New_profile_keeps_the_name_trimmed()
    {
        var profile = CatalogIds.NewProfile("g", "  Vanilla  ", Abc9());

        Assert.Equal("Vanilla", profile.Name);
    }

    [Fact]
    public void New_profile_with_a_name_that_has_no_usable_characters_still_gets_a_valid_id_and_branch()
    {
        var profile = CatalogIds.NewProfile("g", "日本語", Abc9());

        Assert.Equal("profile-abc9", profile.Id);
        Assert.Equal("profile/profile-abc9", profile.BranchName);
    }

    [Fact]
    public void New_profiles_pass_validation()
    {
        foreach (var name in new[] { "Modded", "!!!", "日本語", new string('q', 90) })
        {
            Assert.Empty(CatalogValidator.Validate(CatalogIds.NewProfile("skyrim-se-ab12", name)));
        }
    }

    private sealed class ScriptedRandom : Random
    {
        private readonly int[] _values;
        private int _next;

        public ScriptedRandom(params int[] values)
        {
            _values = values;
        }

        public override int Next(int maxValue)
        {
            var value = _values[_next++];
            Assert.InRange(value, 0, maxValue - 1);
            return value;
        }
    }
}
