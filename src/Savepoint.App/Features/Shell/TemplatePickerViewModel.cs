using System.Windows.Input;
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

    /// <summary>A built-in template; <see cref="GameTemplate.ToDraft"/> turns it into a definition to edit.</summary>
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
    /// <param name="templates">The built-in templates, in the order shown.</param>
    /// <param name="text">Supplies the title.</param>
    public TemplatePickerViewModel(IReadOnlyList<GameTemplate> templates, UiFormatter text)
        : base(text.Message("templatePicker.title"))
    {
        Templates = [.. templates.Select(template => new TemplateItem(template.DisplayName, new RelayCommand(() => Close(new TemplateChoice.Template(template)))))];
    }

    /// <summary>The built-in templates.</summary>
    public IReadOnlyList<TemplateItem> Templates { get; }

    /// <inheritdoc />
    public override double DialogWidth => 480;

    /// <inheritdoc />
    protected override TemplateChoice? DismissResult => null;

    [RelayCommand]
    private void OpenFile() => Close(new TemplateChoice.File());

    [RelayCommand]
    private void Cancel() => Close(null);
}
