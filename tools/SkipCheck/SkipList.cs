namespace Savepoint.Tools.SkipCheck;

/// <summary>
/// The tests a platform is expected to skip, read from a checked-in list. One test name per line, exactly as the xunit report spells it
/// (fully qualified, with theory data in parentheses). A line starting with <c>~</c> names a test that may skip but is not required to
/// (it depends on timing), <c>@include other.txt</c> adds another list from the same folder, and <c>#</c> starts a comment.
/// </summary>
public sealed class SkipList
{
    private const string _includePrefix = "@include ";

    /// <summary>Tests that must skip here; one that runs instead is reported as a note.</summary>
    public IReadOnlySet<string> Expected => _expected;

    /// <summary>Tests that may skip here without anyone minding, and are never reported either way.</summary>
    public IReadOnlySet<string> Intermittent => _intermittent;

    /// <summary>The part of <see cref="Expected"/> that came from <c>@include</c> lines rather than the list's own lines.</summary>
    public IReadOnlySet<string> Included => _included;

    private readonly HashSet<string> _expected = [];
    private readonly HashSet<string> _intermittent = [];
    private readonly HashSet<string> _included = [];

    /// <summary>Reads a list from a file, following its <c>@include</c> lines.</summary>
    /// <exception cref="InvalidDataException">The lists include each other in a circle.</exception>
    public static SkipList Load(string path)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        return Build(File.ReadAllLines(path), name => File.ReadAllLines(Path.Combine(folder, name)), Path.GetFileName(path));
    }

    /// <summary>Reads a list from its lines; <paramref name="readIncluded"/> returns the lines of an included list by file name.</summary>
    /// <exception cref="InvalidDataException">The lists include each other in a circle.</exception>
    public static SkipList Parse(IEnumerable<string> lines, Func<string, IEnumerable<string>> readIncluded) => Build(lines, readIncluded, null);

    private static SkipList Build(IEnumerable<string> lines, Func<string, IEnumerable<string>> readIncluded, string? rootName)
    {
        var list = new SkipList();
        var chain = new List<string>();
        if (rootName is not null)
        {
            chain.Add(rootName);
        }

        list.Add(lines, readIncluded, chain, included: false);
        return list;
    }

    private void Add(IEnumerable<string> lines, Func<string, IEnumerable<string>> readIncluded, List<string> chain, bool included)
    {
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (line.StartsWith(_includePrefix, StringComparison.Ordinal))
            {
                var name = line[_includePrefix.Length..].Trim();
                if (chain.Contains(name))
                {
                    throw new InvalidDataException($"The skip lists include each other in a circle: {string.Join(" -> ", chain)} -> {name}");
                }

                chain.Add(name);
                Add(readIncluded(name), readIncluded, chain, included: true);
                chain.RemoveAt(chain.Count - 1);
            }
            else if (line[0] == '~')
            {
                _intermittent.Add(line[1..].Trim());
            }
            else
            {
                _expected.Add(line);
                if (included)
                {
                    _included.Add(line);
                }
            }
        }
    }
}
