using System.Windows.Input;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Features.Shell;

/// <summary>What the template picker answers with.</summary>
public abstract record TemplateChoice
{
    private TemplateChoice()
    {
    }

    /// <summary>A built-in template; <see cref="GameTemplate.ToDraft(Random?)"/> turns it into a definition to edit.</summary>
    public sealed record Template(GameTemplate Chosen) : TemplateChoice;

    /// <summary>The user wants to pick a game definition file instead.</summary>
    public sealed record File : TemplateChoice;
}

/// <summary>One built-in template in the picker.</summary>
/// <param name="Name">The game's name.</param>
/// <param name="Choose">Answers the picker with this template.</param>
public sealed record TemplateItem(string Name, ICommand Choose);

/// <summary>
/// The dialog behind "Start from a template": the built-in templates, or a game definition file. It answers with the
/// choice, or null when the user backs out.
/// </summary>
public sealed partial class TemplatePickerViewModel : DialogViewModel<TemplateChoice?>
{
    /// <summary>The words typed to narrow the list.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    private readonly List<(TemplateItem Item, string[] Words)> _all;

    /// <param name="templates">The built-in templates, in the order shown.</param>
    /// <param name="text">Supplies the title.</param>
    public TemplatePickerViewModel(IReadOnlyList<GameTemplate> templates, UiFormatter text)
        : base(text.Message("templatePicker.title"))
    {
        Description = text.Message("templatePicker.body");
        NoMatchesText = text.Message("templatePicker.noMatches");
        _all = [.. templates.Select(template => (
            new TemplateItem(template.DisplayName, new RelayCommand(() => Close(new TemplateChoice.Template(template)))),
            WordsOf(template.DisplayName)))];
        Templates = [.. _all.Select(entry => entry.Item)];
    }

    /// <summary>The built-in templates that match the search; all of them when it is empty.</summary>
    public ObservableCollection<TemplateItem> Templates { get; }

    /// <summary>True when the search matches no template.</summary>
    public bool ShowNoMatches => Templates.Count == 0;

    /// <summary>"No games match your search.".</summary>
    public string NoMatchesText { get; }

    partial void OnSearchTextChanged(string value)
    {
        // Every typed word has to start a word of the name, so "sid meiers civ" finds "Sid Meier's Civilization VI".
        var typed = WordsOf(value);
        var shown = _all.Where(entry => typed.All(word => entry.Words.Any(name => name.StartsWith(word, StringComparison.Ordinal))));
        Templates.Clear();
        foreach (var entry in shown)
        {
            Templates.Add(entry.Item);
        }

        OnPropertyChanged(nameof(ShowNoMatches));
    }

    // Lowercase words with apostrophes dropped and other punctuation treated as a space.
    private static string[] WordsOf(string text) =>
        [.. NotALetterOrDigit().Split(Apostrophes().Replace(text.ToLowerInvariant(), string.Empty)).Where(word => word.Length > 0)];

    [GeneratedRegex(@"['’]")]
    private static partial Regex Apostrophes();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NotALetterOrDigit();

    /// <inheritdoc />
    public override string Description { get; }

    /// <inheritdoc />
    public override double DialogWidth => 480;

    /// <inheritdoc />
    public override bool DismissesOnScrimClick => true;

    /// <inheritdoc />
    protected override TemplateChoice? DismissResult => null;

    [RelayCommand]
    private void OpenFile() => Close(new TemplateChoice.File());

    [RelayCommand]
    private void Cancel() => Close(null);
}
