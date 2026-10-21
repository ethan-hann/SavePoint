using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Savepoint.App.Features.Launch;

/// <summary>The content of a <see cref="PostPlayDialogViewModel"/>, found by <see cref="Common.ViewLocator"/>.</summary>
public partial class PostPlayDialogView : UserControl
{
    /// <summary>Creates the view; its DataContext is the dialog view model.</summary>
    public PostPlayDialogView()
    {
        InitializeComponent();

        // A card that has focus swallows Enter to select itself, which would leave the default button out of reach.
        ChoiceList.AddHandler(KeyDownEvent, OnChoiceKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnChoiceKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.Source is ListBoxItem && DataContext is PostPlayDialogViewModel dialog && dialog.SaveCommand.CanExecute(null))
        {
            dialog.SaveCommand.Execute(null);
            e.Handled = true;
        }
    }
}
