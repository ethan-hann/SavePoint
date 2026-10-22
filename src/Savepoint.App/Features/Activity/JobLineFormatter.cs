using Savepoint.App.Features.Common.Formatting;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.Activity;

/// <summary>Turns the lines of a job's details into the text the Activity screen shows.</summary>
public sealed class JobLineFormatter
{
    private readonly UiFormatter _text;

    /// <param name="text">Formats the catalog messages the steps name.</param>
    public JobLineFormatter(UiFormatter text)
    {
        _text = text;
    }

    /// <summary>
    /// The text of one line: a path as it is, a step in the player's language. A step whose id this version does not know reads as a
    /// plain sentence, never as the id. A <c>bytes</c> argument is shown as a size; every other value goes to the message as it is.
    /// </summary>
    /// <param name="line">The line a job wrote.</param>
    public string Format(JobLine line) => line switch
    {
        JobLine.FilePath path => path.Path,
        JobLine.Step step when _text.Knows(step.MessageId) => _text.Message(step.MessageId, Arguments(step)),
        _ => _text.Message("activity.line.unknown"),
    };

    private Dictionary<string, object?> Arguments(JobLine.Step step)
    {
        var arguments = new Dictionary<string, object?>();
        foreach (var (name, value) in step.Args)
        {
            arguments[name] = name == "bytes" && value is int or long ? _text.FileSize(Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture)) : value;
        }

        return arguments;
    }
}
