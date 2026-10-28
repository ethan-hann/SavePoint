using System.Collections.Concurrent;
using System.Reflection;
using Savepoint.App.Tests.Fakes;
using Xunit.v3;

[assembly: FailOnUnansweredConfirmation]

namespace Savepoint.App.Tests.Fakes;

/// <summary>
/// Fails a test when a <see cref="FakeDialogs"/> it built was asked a confirmation nobody answered. The fake throws at the question,
/// but the throw lands in the command's task, and a test that starts a command without awaiting it never sees that task. So the fake
/// also records the question, and this check reads the record when the test ends.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class FailOnUnansweredConfirmationAttribute : BeforeAfterTestAttribute
{
    private static readonly ConcurrentDictionary<string, ConcurrentBag<FakeDialogs>> _builtByTest = new();

    /// <summary>Remembers a fake as one the running test built; a fake built outside a test is not checked.</summary>
    internal static void Track(FakeDialogs dialogs)
    {
        if (TestContext.Current.Test is { } test)
        {
            _builtByTest.GetOrAdd(test.UniqueID, _ => []).Add(dialogs);
        }
    }

    /// <summary>The questions the given fakes were asked and nobody answered, in the order each fake recorded them.</summary>
    internal static List<string> Unanswered(IEnumerable<FakeDialogs> dialogs) =>
        [.. dialogs.SelectMany(fake => fake.Unanswered).Select(request => request.Title)];

    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (!_builtByTest.TryRemove(test.UniqueID, out var built))
        {
            return;
        }

        var unanswered = Unanswered(built);
        if (unanswered.Count > 0)
        {
            throw new Xunit.Sdk.XunitException($"The screen asked {string.Join(", ", unanswered.Select(title => $"\"{title}\""))} and the test queued no answer, so the action stopped there. Say yes or no with AnswerConfirm, or set DefaultConfirmAnswer.");
        }
    }
}
