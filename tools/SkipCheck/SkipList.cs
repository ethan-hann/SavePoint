namespace Savepoint.Tools.SkipCheck;

/// <summary>
/// The tests a platform is expected to skip, read from a checked-in list. One test name per line, exactly as the xunit report spells it
/// (fully qualified, with theory data in parentheses). A line starting with <c>~</c> names a test that may skip but is not required to
/// (it depends on timing or on the machine), and <c>#</c> starts a comment.
/// </summary>
public sealed class SkipList
{
    /// <summary>Tests that must skip here; one that runs instead is reported as a note.</summary>
    public IReadOnlySet<string> Expected => _expected;

    /// <summary>Tests that may skip here without anyone minding, and are never reported either way.</summary>
    public IReadOnlySet<string> Intermittent => _intermittent;

    private readonly HashSet<string> _expected = [];
    private readonly HashSet<string> _intermittent = [];

    /// <summary>Reads a list from a file.</summary>
    public static SkipList Load(string path) => Parse(File.ReadAllLines(path));

    /// <summary>Reads a list from its lines.</summary>
    public static SkipList Parse(IEnumerable<string> lines)
    {
        var list = new SkipList();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (line[0] == '~')
            {
                list._intermittent.Add(line[1..].Trim());
            }
            else
            {
                list._expected.Add(line);
            }
        }

        return list;
    }
}
