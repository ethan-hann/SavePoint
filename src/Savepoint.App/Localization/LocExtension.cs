using Avalonia.Markup.Xaml;

namespace Savepoint.App.Localization;

public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        LocalizationCatalog.Current.Format(Key);
}
