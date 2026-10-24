using System.Xml.Linq;

namespace Savepoint.Tools.SkipCheck;

/// <summary>What the xunit report files of one gate run say about the tests that ran.</summary>
/// <param name="Skipped">Skipped tests, by name, with the reason each gave.</param>
/// <param name="Ran">Names of the tests that ran to a result other than a skip.</param>
public sealed record XunitReport(IReadOnlyDictionary<string, string> Skipped, IReadOnlySet<string> Ran)
{
    /// <summary>Reads reports from xunit's XML (the <c>--report-xunit</c> format) and merges them; a test seen both skipped and run counts as run.</summary>
    public static XunitReport Read(IEnumerable<TextReader> reports)
    {
        var skipped = new Dictionary<string, string>(StringComparer.Ordinal);
        var ran = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reader in reports)
        {
            foreach (var test in XDocument.Load(reader).Descendants("test"))
            {
                var name = (string?)test.Attribute("name");
                if (name is null)
                {
                    continue;
                }

                if ((string?)test.Attribute("result") == "Skip")
                {
                    skipped[name] = test.Element("reason")?.Value ?? string.Empty;
                }
                else
                {
                    ran.Add(name);
                }
            }
        }

        foreach (var name in ran)
        {
            skipped.Remove(name);
        }

        return new XunitReport(skipped, ran);
    }
}
