using System.Collections.Concurrent;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Savepoint.App.Features.Common;

/// <summary>
/// Finds the view for a view model by naming convention: <c>Some.Namespace.FooViewModel</c> is shown by
/// <c>Some.Namespace.FooView</c> in the same assembly. Registered once in <c>App.axaml</c>, so a screen, dialog or
/// pane plugs in by adding the two classes; nothing that hosts it needs editing.
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    private const string _viewModelSuffix = "ViewModel";
    private const string _viewSuffix = "View";

    private static readonly ConcurrentDictionary<Type, Type?> _views = new();

    /// <inheritdoc />
    public bool Match(object? data) => data is not null && ViewTypeFor(data.GetType()) is not null;

    /// <inheritdoc />
    public Control? Build(object? param)
    {
        if (param is null || ViewTypeFor(param.GetType()) is not { } viewType)
        {
            return null;
        }

        return (Control)Activator.CreateInstance(viewType)!;
    }

    /// <summary>The view class that pairs with a view model type, or null when the type is not named <c>...ViewModel</c> or has no such view.</summary>
    public static Type? ViewTypeFor(Type viewModelType) => _views.GetOrAdd(viewModelType, Resolve);

    private static Type? Resolve(Type viewModelType)
    {
        var name = viewModelType.FullName;
        if (name is null || !name.EndsWith(_viewModelSuffix, StringComparison.Ordinal))
        {
            return null;
        }

        var viewName = string.Concat(name.AsSpan(0, name.Length - _viewModelSuffix.Length), _viewSuffix);
        var view = viewModelType.Assembly.GetType(viewName);
        return view is not null && typeof(Control).IsAssignableFrom(view) ? view : null;
    }
}
