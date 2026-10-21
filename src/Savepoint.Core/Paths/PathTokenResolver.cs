using System.Text.RegularExpressions;
using Savepoint.Core.Errors;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Paths;

/// <summary>Expands a leading {token}; both separators are accepted because definitions are written with Windows ones.</summary>
public sealed partial class PathTokenResolver
{
    private readonly IPlatform _platform;
    private readonly PathSyntax _syntax;

    /// <summary>Builds a resolver for one platform's token table and path rules.</summary>
    /// <param name="platform">The platform whose tokens and path syntax apply.</param>
    public PathTokenResolver(IPlatform platform)
    {
        _platform = platform;
        _syntax = PathSyntax.For(platform.Os);
    }

    /// <summary>
    /// Returns the absolute, normalized path for this OS. Throws <see cref="ValidationException"/> with one issue at
    /// <paramref name="fieldPath"/>: <see cref="PathValidationIds.UnknownToken"/>, <see cref="PathValidationIds.NotAbsolute"/>,
    /// or <see cref="PathValidationIds.InvalidCharacter"/> (a Windows colon after the drive letter).
    /// </summary>
    /// <param name="rawPath">The path as written in the definition. Backslashes count as separators only after a token on Linux and macOS.</param>
    /// <param name="fieldPath">The field the path came from, reported as the issue's path.</param>
    public string Resolve(string rawPath, string fieldPath = "path")
    {
        var token = TokenPattern().Match(rawPath);
        if (token.Success)
        {
            if (!_platform.PathTokens.TryGetValue(token.Groups[1].Value, out var basePath))
            {
                throw Invalid(fieldPath, PathValidationIds.UnknownToken);
            }

            var rest = ToPlatformSeparators(rawPath[token.Length..]).TrimStart(_syntax.Separator);
            if (_syntax.Os == OsKind.Windows && rest.Contains(':'))
            {
                throw Invalid(fieldPath, PathValidationIds.InvalidCharacter);
            }

            return _syntax.Combine(basePath, rest);
        }

        // A POSIX folder name may contain a backslash, so only a Windows path (or a token's tail, above) is rewritten.
        var path = _syntax.Os == OsKind.Windows ? ToPlatformSeparators(rawPath) : rawPath;
        if (_syntax.UsesStreamSyntax(path))
        {
            throw Invalid(fieldPath, PathValidationIds.InvalidCharacter);
        }

        return _syntax.IsAbsolute(path) ? _syntax.Normalize(path) : throw Invalid(fieldPath, PathValidationIds.NotAbsolute);
    }

    [GeneratedRegex(@"^\{([A-Za-z]+)\}")]
    private static partial Regex TokenPattern();

    private static ValidationException Invalid(string fieldPath, string messageId) =>
        new([new ValidationIssue(fieldPath, messageId)]);

    private string ToPlatformSeparators(string path) => path.Replace('/', _syntax.Separator).Replace('\\', _syntax.Separator);
}
