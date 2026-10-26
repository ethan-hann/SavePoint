using System.Collections.ObjectModel;
using System.Windows.Input;
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
/// <param name="Label">What a screen reader says for the item when that differs from the name; null says the name.</param>
public sealed record TemplateItem(string Name, ICommand Choose, string? Label = null)
{
    /// <summary>What a screen reader says for the item.</summary>
    public string AutomationName => Label ?? Name;
}

/// <summary>
/// The dialog behind "Start from a template": the built-in templates, or a game definition file. It answers with the
/// choice, or null when the user backs out.
/// </summary>
public sealed partial class TemplatePickerViewModel : DialogViewModel<TemplateChoice?>
{
    /// <summary>The words typed to narrow the list.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    private readonly List<(TemplateItem Item, SearchKey Key)> _all;

    /// <param name="templates">The built-in templates, in the order shown.</param>
    /// <param name="text">Supplies the title.</param>
    /// <param name="search">Words to narrow the list with from the start; empty lists every template.</param>
    public TemplatePickerViewModel(IReadOnlyList<GameTemplate> templates, UiFormatter text, string search = "")
        : base(text.Message("templatePicker.title"))
    {
        Description = text.Message("templatePicker.body");
        NoMatchesText = text.Message("templatePicker.noMatches");
        _all = [.. templates.Select(template => (
            new TemplateItem(template.DisplayName, new RelayCommand(() => Close(new TemplateChoice.Template(template)))),
            TemplateSearch.KeyOf(template.DisplayName)))];
        Templates = [.. _all.Select(entry => entry.Item)];
        SearchText = search;
    }

    /// <summary>The built-in templates that match the search; all of them when it is empty.</summary>
    public ObservableCollection<TemplateItem> Templates { get; }

    /// <summary>True when the search matches no template.</summary>
    public bool ShowNoMatches => Templates.Count == 0;

    /// <summary>"No games match your search.".</summary>
    public string NoMatchesText { get; }

    partial void OnSearchTextChanged(string value)
    {
        var shown = TemplateSearch.Find(_all, value);
        Templates.Clear();
        foreach (var item in shown)
        {
            Templates.Add(item);
        }

        OnPropertyChanged(nameof(ShowNoMatches));
    }

    /// <inheritdoc />
    public override string Description { get; }

    /// <inheritdoc />
    public override double DialogWidth => 480;

    /// <inheritdoc />
    public override bool DismissesOnScrimClick => true;

    /// <inheritdoc />
    public override bool PinsFooter => true;

    /// <inheritdoc />
    protected override TemplateChoice? DismissResult => null;

    [RelayCommand]
    private void OpenFile() => Close(new TemplateChoice.File());

    [RelayCommand]
    private void Cancel() => Close(null);
}
