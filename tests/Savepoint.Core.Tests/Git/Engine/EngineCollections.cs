namespace Savepoint.Core.Tests.Git.Engine;

// Every engine test starts dozens of git processes, and on a machine with a virus scanner starting a process costs most of
// the time. Left to run one class per core they queue behind one another and make other tests that watch the clock miss
// their limits. The classes are spread over six collections: the classes of one collection run one after the other, the
// six collections run side by side.
internal static class EngineCollections
{
    public const string A = "git engine A";
    public const string B = "git engine B";
    public const string C = "git engine C";
    public const string D = "git engine D";
    public const string E = "git engine E";
    public const string F = "git engine F";
}
