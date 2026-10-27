using Savepoint.App.Features.History;

namespace Savepoint.App.Tests.Features.History;

// The card view scrolls as one list: each group's heading, then its cards in rows as wide as the screen has room for.
public class HistoryCardRowsTests
{
    [Fact]
    public async Task The_cards_are_one_list_of_a_heading_then_rows_of_three_for_each_group()
    {
        var rig = await HistoryRig.ShownAsync();

        var expected = new List<string>();
        foreach (var group in rig.Screen.Groups)
        {
            expected.Add($"heading:{group.Heading}");
            expected.AddRange(group.Cards.Chunk(3).Select(row => "row:" + string.Join('|', row.Select(card => card.Name))));
        }

        Assert.Equal(expected, rig.Screen.CardEntries.Select(Describe));
        Assert.True(rig.Screen.CardEntries.OfType<CardRowViewModel>().All(row => row.Columns == 3));
    }

    [Fact]
    public async Task Fewer_columns_make_shorter_rows_and_more_of_them()
    {
        var rig = await HistoryRig.ShownAsync();
        var cards = rig.Screen.Groups.Sum(group => group.Cards.Count);

        rig.Screen.CardColumns = 1;

        var rows = rig.Screen.CardEntries.OfType<CardRowViewModel>().ToList();
        Assert.Equal(cards, rows.Count);
        Assert.All(rows, row => Assert.Single(row.Cards));
        Assert.All(rows, row => Assert.Equal(1, row.Columns));
    }

    [Fact]
    public async Task A_search_keeps_the_row_of_a_card_that_stays_where_it_was()
    {
        var rig = await HistoryRig.ShownAsync();
        rig.Screen.CardColumns = 1;
        var before = rig.Screen.CardEntries.OfType<CardRowViewModel>().ToDictionary(row => row.Cards[0].Name);

        rig.Screen.SelectedKindOption = rig.Screen.KindOptions.Single(option => option.Filter == HistoryKindFilter.GameAndMods);

        var after = rig.Screen.CardEntries.OfType<CardRowViewModel>().ToList();
        Assert.NotEmpty(after);
        Assert.True(after.Count < before.Count);
        Assert.All(after, row => Assert.Same(before[row.Cards[0].Name], row));
    }

    [Fact]
    public async Task A_group_the_search_leaves_empty_loses_its_heading_too()
    {
        var rig = await HistoryRig.ShownAsync();
        var earlier = rig.Screen.Groups.Last().Cards.First().Name;

        rig.Screen.Search.Text = earlier;

        Assert.DoesNotContain(rig.Screen.CardEntries.OfType<CardGroupHeadingViewModel>(), heading => heading.Heading != rig.Screen.Groups.Last().Heading);
        Assert.Single(rig.Screen.CardEntries.OfType<CardGroupHeadingViewModel>());
    }

    [Fact]
    public async Task The_cards_are_numbered_through_the_whole_list_not_within_each_group()
    {
        var rig = await HistoryRig.ShownAsync();

        var cards = rig.Screen.Groups.SelectMany(group => group.Cards).ToList();

        Assert.True(rig.Screen.Groups.Count > 1, "the test needs two groups");
        Assert.Equal(Enumerable.Range(1, cards.Count), cards.Select(card => card.PositionInSet));
        Assert.All(cards, card => Assert.Equal(cards.Count, card.SizeOfSet));
    }

    [Fact]
    public async Task A_search_numbers_the_cards_it_leaves_through_the_whole_list_again()
    {
        var rig = await HistoryRig.ShownAsync();

        rig.Screen.SelectedKindOption = rig.Screen.KindOptions.Single(option => option.Filter == HistoryKindFilter.GameAndMods);

        var cards = rig.Screen.Groups.SelectMany(group => group.Cards).ToList();
        Assert.Equal(Enumerable.Range(1, cards.Count), cards.Select(card => card.PositionInSet));
        Assert.All(cards, card => Assert.Equal(cards.Count, card.SizeOfSet));
    }

    [Fact]
    public async Task Each_card_names_its_group_ahead_of_itself_for_assistive_technology()
    {
        var rig = await HistoryRig.ShownAsync();

        foreach (var group in rig.Screen.Groups)
        {
            Assert.All(group.Cards, card => Assert.StartsWith($"{group.Heading}: {card.Name}", card.ItemAutomationName, StringComparison.Ordinal));
        }
    }

    private static string Describe(CardListEntry entry) => entry switch
    {
        CardGroupHeadingViewModel heading => $"heading:{heading.Heading}",
        CardRowViewModel row => "row:" + string.Join('|', row.Cards.Select(card => card.Name)),
        _ => throw new InvalidOperationException("An unknown line."),
    };
}
