using Avalonia.Controls;
using Avalonia.Input;

namespace Savepoint.App.Features.Shell;

/// <summary>The template picker's content; its DataContext is a <see cref="TemplatePickerViewModel"/>.</summary>
public partial class TemplatePickerView : UserControl
{
    /// <summary>Creates the view.</summary>
    public TemplatePickerView()
    {
        InitializeComponent();
        ClearSearchButton.Click += (_, _) => SearchField.Focus(NavigationMethod.Tab);
    }
}
