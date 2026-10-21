using System.Text.RegularExpressions;
using Savepoint.Core.Errors;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Paths;

/// <summary>
/// Turns a game definition's path into an absolute one for this OS. A path may start with {token}
/// (letters only); definitions are written with Windows separators, so both separators are accepted everywhere.
/// </summary>
public sealed partial class PathTokenResolver
{
    private readonly IPlatform _platform;
    private readonly PathSyntax _syntax;

    public PathTokenResolver(IPlatform platform)
    {
        _platform = platform;
        _syntax = PathSyntax.For(platform.Os);
    }

    /// <exception cref="ValidationException">The token is unknown on this OS, or the path is not absolute.</exception>
    /// <param name="rawPath">The path as written in the definition.</param>
    /// <param name="fieldPath">The field the path came from, reported in the validation issue.</param>
    public string Resolve(string rawPath, string fieldPath = "path")
    {
        var token = TokenPattern().Match(rawPath);
        if (token.Success)
        {
            if (!_platform.PathTokens.TryGetValue(token.Groups[1].Value, out var basePath))
            {
                throw Invalid(fieldPath, "validation.path.unknownToken");
            }

            var rest = ToPlatformSeparators(rawPath[token.Length..]).TrimStart(_syntax.Separator);
            return _syntax.Combine(basePath, rest);
        }

        var path = ToPlatformSeparators(rawPath);
        return _syntax.IsAbsolute(path) ? _syntax.Normalize(path) : throw Invalid(fieldPath, "validation.path.notAbsolute");
    }

    [GeneratedRegex(@"^\{([A-Za-z]+)\}")]
    private static partial Regex TokenPattern();

    private static ValidationException Invalid(string fieldPath, string messageId) =>
        new([new ValidationIssue(fieldPath, messageId)]);

    private string ToPlatformSeparators(string path) => path.Replace('/', _syntax.Separator).Replace('\\', _syntax.Separator);
}
