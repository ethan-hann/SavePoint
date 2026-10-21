using System.Reflection;
using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Hosting;

/// <summary>
/// Stands in for an Api that could not be built (the backend failed to start): every call answers the same failure, every
/// list is empty, every event is silent. The startup screen shows the failure; nothing else runs.
/// </summary>
public class UnavailableApi : DispatchProxy
{
    private static readonly MethodInfo _failure = typeof(OperationResult)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method => method.Name == nameof(OperationResult.Failure) && method.IsGenericMethodDefinition && method.GetParameters()[0].ParameterType == typeof(ErrorInfo));

    private static readonly MethodInfo _completed = typeof(Task).GetMethod(nameof(Task.FromResult))!;

    private ErrorInfo _error = new("error.unknown", new Dictionary<string, object?>(), string.Empty);

    /// <summary>Builds an Api of type <typeparamref name="T"/> whose every operation fails with <paramref name="error"/>.</summary>
    /// <typeparam name="T">One of the Api interfaces.</typeparam>
    /// <param name="error">The failure every call answers with.</param>
    public static T Create<T>(ErrorInfo error)
        where T : class
    {
        var proxy = Create<T, UnavailableApi>();
        ((UnavailableApi)(object)proxy)._error = error;
        return proxy;
    }

    /// <inheritdoc />
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var returns = targetMethod?.ReturnType;
        if (returns is null || returns == typeof(void) || targetMethod!.Name.StartsWith("add_", StringComparison.Ordinal) || targetMethod.Name.StartsWith("remove_", StringComparison.Ordinal))
        {
            return null;
        }

        if (returns.IsGenericType && returns.GetGenericTypeDefinition() == typeof(Task<>) && returns.GenericTypeArguments[0] is { IsGenericType: true } result
            && result.GetGenericTypeDefinition() == typeof(OperationResult<>))
        {
            var failed = _failure.MakeGenericMethod(result.GenericTypeArguments[0]).Invoke(null, [_error]);
            return _completed.MakeGenericMethod(result).Invoke(null, [failed]);
        }

        if (returns.IsGenericType && returns.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        {
            return Array.CreateInstance(returns.GenericTypeArguments[0], 0);
        }

        return returns.IsValueType ? Activator.CreateInstance(returns) : null;
    }
}
