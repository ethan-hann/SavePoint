using Savepoint.Core.Errors;

namespace Savepoint.Core.Api;

/// <summary>
/// One line of a job's details. A line is closed to exactly two shapes, so no free text can reach a player: a file path, which the
/// App shows as it is, or a step, which carries a catalog message id and its arguments and which the App formats in the player's
/// language. Text from the tools SavePoint runs underneath is never a line.
/// </summary>
public abstract record JobLine
{
    private JobLine()
    {
    }

    /// <summary>Makes a line that names a file.</summary>
    /// <param name="path">The file as it is shown, relative to its folder when the job knows the folder.</param>
    public static JobLine ForPath(string path) => new FilePath(path);

    /// <summary>Makes a line that says what the job is doing.</summary>
    /// <param name="messageId">The catalog key of the step's message; the ids Core writes are the constants of <see cref="JobSteps"/>.</param>
    /// <param name="args">The values for the message's placeholders. A byte count is named <c>bytes</c> (or starts with it), so the App can show it as a size.</param>
    public static JobLine ForStep(string messageId, params (string Name, object? Value)[] args) =>
        new Step(messageId, args.ToDictionary(arg => arg.Name, arg => arg.Value));

    /// <summary>A file the job is handling.</summary>
    /// <param name="Path">The path, shown as it is.</param>
    public sealed record FilePath(string Path) : JobLine
    {
        /// <summary>The path, for logs and test output.</summary>
        public override string ToString() => Path;
    }

    /// <summary>A step in plain words, which the App formats through the catalog.</summary>
    /// <param name="MessageId">The catalog key of the message.</param>
    /// <param name="Args">
    /// The values for the message's placeholders. The dictionary is copied when it is set, and enum values become camelCase strings
    /// so an ICU select can match them, exactly as for <see cref="ErrorInfo.Args"/>.
    /// </param>
    public sealed record Step(string MessageId, IReadOnlyDictionary<string, object?> Args) : JobLine
    {
        private readonly IReadOnlyDictionary<string, object?> _args = ErrorArgs.Normalize(Args);

        /// <summary>The values for the message's placeholders, as a snapshot taken when the step was made.</summary>
        public IReadOnlyDictionary<string, object?> Args
        {
            get => _args;
            init => _args = ErrorArgs.Normalize(value);
        }

        /// <summary>Equal when the id and the argument values are equal; arguments compare by content, in any order.</summary>
        /// <param name="other">The step to compare with.</param>
        public bool Equals(Step? other) => other is not null && MessageId == other.MessageId && ErrorArgs.AreEqual(Args, other.Args);

        /// <summary>A hash that agrees with <see cref="Equals(Step?)"/>.</summary>
        public override int GetHashCode() => HashCode.Combine(MessageId, ErrorArgs.HashOf(Args));

        /// <summary>The id and the arguments, for logs and test output.</summary>
        public override string ToString() => $"{MessageId} {{{string.Join(", ", Args.Select(pair => $"{pair.Key}={pair.Value}"))}}}";
    }
}
