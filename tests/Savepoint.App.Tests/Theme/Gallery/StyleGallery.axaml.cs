using Avalonia.Controls;

namespace Savepoint.App.Tests.Theme.Gallery;

public partial class StyleGallery : UserControl
{
    public StyleGallery()
    {
        InitializeComponent();
        SavePicker.ItemsSource = new[]
        {
            new SaveChoice("Deep cave run - 5-30-26, 11:42 PM", "Most recent - 2 saves in this profile"),
            new SaveChoice("Before the final boss - 5-14-26, 9:10 PM", "1 save"),
        };
    }
}

public sealed record SaveChoice(string Title, string Subtitle);
