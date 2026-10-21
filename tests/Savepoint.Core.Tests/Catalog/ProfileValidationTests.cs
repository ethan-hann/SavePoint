using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using static Savepoint.Core.Tests.Catalog.CatalogSamples;

namespace Savepoint.Core.Tests.Catalog;

public class ProfileValidationTests
{
    [Fact]
    public void Valid_profile_has_no_issues()
    {
        Assert.Empty(CatalogValidator.Validate(Profile()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Blank_id_is_reported(string id)
    {
        var issues = CatalogValidator.Validate(Profile() with { Id = id });

        Assert.Equal([Issue("id", ValidationMessageIds.ProfileIdRequired)], issues);
    }

    [Fact]
    public void Blank_game_id_is_reported()
    {
        var issues = CatalogValidator.Validate(Profile() with { GameId = "" });

        Assert.Equal([Issue("gameId", ValidationMessageIds.ProfileGameIdRequired)], issues);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_name_is_reported(string name)
    {
        var issues = CatalogValidator.Validate(Profile() with { Name = name });

        Assert.Equal([Issue("name", ValidationMessageIds.ProfileNameRequired)], issues);
    }

    [Fact]
    public void Empty_branch_name_is_reported_as_required()
    {
        var issues = CatalogValidator.Validate(Profile() with { BranchName = "" });

        Assert.Equal([Issue("branchName", ValidationMessageIds.ProfileBranchNameRequired)], issues);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("bad!name")]
    [InlineData("a.b")]
    [InlineData("trés")]
    [InlineData("a\\b")]
    [InlineData("a:b")]
    [InlineData("abc\n")]
    [InlineData(" profile/a")]
    public void Branch_name_with_characters_outside_the_allowed_set_is_reported(string branch)
    {
        var issues = CatalogValidator.Validate(Profile() with { BranchName = branch });

        Assert.Equal([Issue("branchName", ValidationMessageIds.ProfileBranchNameInvalid)], issues);
    }

    [Theory]
    [InlineData("/profile/a")]
    [InlineData("profile/a/")]
    [InlineData("profile//a")]
    [InlineData("/")]
    public void Branch_name_with_a_leading_trailing_or_doubled_slash_is_reported(string branch)
    {
        var issues = CatalogValidator.Validate(Profile() with { BranchName = branch });

        Assert.Equal([Issue("branchName", ValidationMessageIds.ProfileBranchNameInvalid)], issues);
    }

    [Theory]
    [InlineData("-a")]
    [InlineData("--force")]
    public void Branch_name_that_git_would_read_as_an_option_is_reported(string branch)
    {
        var issues = CatalogValidator.Validate(Profile() with { BranchName = branch });

        Assert.Equal([Issue("branchName", ValidationMessageIds.ProfileBranchNameInvalid)], issues);
    }

    [Theory]
    [InlineData("profile/modded-ab12")]
    [InlineData("a/b/c")]
    [InlineData("Main_Line-2")]
    [InlineData("a-")]
    public void Ordinary_branch_names_are_accepted(string branch)
    {
        Assert.Empty(CatalogValidator.Validate(Profile() with { BranchName = branch }));
    }

    [Fact]
    public void Every_problem_is_reported_at_once_in_field_order()
    {
        var issues = CatalogValidator.Validate(new Profile(Id: "", GameId: "", Name: " ", BranchName: "bad branch"));

        Assert.Equal(
            [
                Issue("id", ValidationMessageIds.ProfileIdRequired),
                Issue("gameId", ValidationMessageIds.ProfileGameIdRequired),
                Issue("name", ValidationMessageIds.ProfileNameRequired),
                Issue("branchName", ValidationMessageIds.ProfileBranchNameInvalid),
            ],
            issues);
    }

    [Fact]
    public void Normalize_trims_the_name_and_the_version_override()
    {
        var profile = Profile() with { Name = "  Modded run ", VersionOverride = " 1.6.1170 " };

        var normalized = CatalogValidator.Normalize(profile);

        Assert.Equal("Modded run", normalized.Name);
        Assert.Equal("1.6.1170", normalized.VersionOverride);
    }

    [Fact]
    public void Normalize_turns_a_blank_version_override_into_none_and_keeps_notes_as_typed()
    {
        var profile = Profile() with { VersionOverride = "   ", Notes = "  spaced  " };

        var normalized = CatalogValidator.Normalize(profile);

        Assert.Null(normalized.VersionOverride);
        Assert.Equal("  spaced  ", normalized.Notes);
    }

    [Fact]
    public void RequireValid_returns_the_normalized_profile()
    {
        var result = CatalogValidator.RequireValid(Profile() with { Name = " Run " });

        Assert.Equal("Run", result.Name);
    }

    [Fact]
    public void RequireValid_throws_with_the_issues_for_an_invalid_profile()
    {
        var error = Assert.Throws<ValidationException>(() => CatalogValidator.RequireValid(Profile() with { Name = "" }));

        Assert.Equal(
            [Issue("name", ValidationMessageIds.ProfileNameRequired)],
            (IReadOnlyList<ValidationIssue>)error.Args["issues"]!);
    }
}
