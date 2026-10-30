using Savepoint.Tools.SkipCheck;

return SkipCheckProgram.Run(args, Console.Out, Console.Error);

namespace Savepoint.Tools.SkipCheck
{
    /// <summary>The command line of the skip check the gates run after the tests.</summary>
    public static class SkipCheckProgram
    {
        /// <summary>
        /// Usage: <c>--reports &lt;folder&gt; [--expected &lt;list file&gt;] [--never-skip &lt;test name&gt;]... [--write]</c>. Reads every xunit report
        /// under the folder and compares the skipped tests with the list. A test named by <c>--never-skip</c> fails the check when it skipped
        /// (even if the list names it) or is not in the reports at all; without a list that is the only check, for a system that has none. Returns 0 when every skip was
        /// expected, 1 when one was not, 2 when the input is unusable (no report, no tests in it, no list and nothing named by
        /// <c>--never-skip</c>). With <c>--write</c> it rewrites the list from the run instead and returns 0.
        /// </summary>
        public static int Run(string[] args, TextWriter output, TextWriter error)
        {
            string? reportsFolder = null;
            string? listPath = null;
            var write = false;
            var neverSkip = new List<string>();
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--reports" when i + 1 < args.Length:
                        reportsFolder = args[++i];
                        break;
                    case "--expected" when i + 1 < args.Length:
                        listPath = args[++i];
                        break;
                    case "--never-skip" when i + 1 < args.Length:
                        neverSkip.Add(args[++i]);
                        break;
                    case "--write":
                        write = true;
                        break;
                    default:
                        error.WriteLine($"skip check: unknown argument {args[i]}");
                        return Usage(error);
                }
            }

            if (reportsFolder is null || (listPath is null && neverSkip.Count == 0) || (write && listPath is null))
            {
                return Usage(error);
            }

            if (listPath is not null && !File.Exists(listPath))
            {
                error.WriteLine($"skip check: there is no skip list at {listPath}");
                return 2;
            }

            XunitReport report;
            try
            {
                report = ReadReports(reportsFolder, error);
            }
            catch (Exception e) when (e is IOException or System.Xml.XmlException or UnauthorizedAccessException)
            {
                error.WriteLine($"skip check: a report could not be read: {e.Message}");
                return 2;
            }

            if (report.Ran.Count + report.Skipped.Count == 0)
            {
                error.WriteLine($"skip check: the reports under {reportsFolder} hold no tests, so there is nothing to check");
                return 2;
            }

            var skippedAgainstRule = neverSkip.Where(report.Skipped.ContainsKey).Order(StringComparer.Ordinal).ToList();
            foreach (var name in skippedAgainstRule)
            {
                error.WriteLine($"skip check FAILED: {name} skipped here ({report.Skipped[name]}), and it must run on every machine that runs the gate");
            }

            // A renamed or filtered-out test must not pass as if it had run.
            var absent = neverSkip.Where(name => !report.Skipped.ContainsKey(name) && !report.Ran.Contains(name)).Order(StringComparer.Ordinal).ToList();
            foreach (var name in absent)
            {
                error.WriteLine($"skip check FAILED: {name} did not run: it is not in the reports, and it must run on every machine that runs the gate");
            }

            skippedAgainstRule.AddRange(absent);

            if (listPath is null)
            {
                if (skippedAgainstRule.Count > 0)
                {
                    return 1;
                }

                output.WriteLine($"skip check passed: no skip list for this system; {neverSkip.Count} tests that must never skip all ran");
                return 0;
            }

            SkipList list;
            try
            {
                list = SkipList.Load(listPath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                error.WriteLine($"skip check: the skip list {listPath} could not be read: {e.Message}");
                return 2;
            }

            if (write)
            {
                Rewrite(listPath, list, report);
                output.WriteLine($"skip check: wrote {listPath} from {report.Skipped.Count} skipped tests");
                return 0;
            }

            var verdict = SkipVerdict.Compare(list, report);
            foreach (var name in verdict.NowRunning)
            {
                output.WriteLine($"note: expected to skip here but ran: {name}");
            }

            if (verdict.NotInReport > 0)
            {
                output.WriteLine($"note: {verdict.NotInReport} listed tests are not in the reports (a narrowed run, or a test that was removed)");
            }

            if (!verdict.Passed)
            {
                error.WriteLine($"skip check FAILED: {verdict.Unexpected.Count} tests skipped that the list {listPath} does not name. Fix what made them skip, or add them to the list if skipping here is right:");
                foreach (var name in verdict.Unexpected)
                {
                    error.WriteLine(name);
                }

                foreach (var group in verdict.Unexpected.GroupBy(name => report.Skipped[name]).OrderByDescending(g => g.Count()))
                {
                    error.WriteLine($"  {group.Count()} x {group.Key}");
                }

                return 1;
            }

            if (skippedAgainstRule.Count > 0)
            {
                return 1;
            }

            output.WriteLine($"skip check passed: {report.Skipped.Count} of {verdict.TotalTests} tests skipped, all of them listed");
            return 0;
        }

        private static int Usage(TextWriter error)
        {
            error.WriteLine("usage: SkipCheck --reports <folder> [--expected <list file>] [--never-skip <test name>]... [--write]");
            return 2;
        }

        private static XunitReport ReadReports(string folder, TextWriter error)
        {
            // The test platform writes <user>_<machine>_<time>.xunit unless it is given a file name.
            var files = Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(f => f.EndsWith(".xunit", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).Order().ToList()
                : [];
            if (files.Count == 0)
            {
                error.WriteLine($"skip check: no xunit report found under {folder}; the test step writes one with --report-xunit");
                return new XunitReport(new Dictionary<string, string>(), new HashSet<string>());
            }

            var readers = files.Select(file => (TextReader)new StreamReader(file)).ToList();
            try
            {
                return XunitReport.Read(readers);
            }
            finally
            {
                readers.ForEach(reader => reader.Dispose());
            }
        }

        // Keeps the header and the timing entries, and replaces the expected names.
        private static void Rewrite(string listPath, SkipList list, XunitReport report)
        {
            var existing = File.ReadAllLines(listPath);
            var header = existing.TakeWhile(line => line.Trim().Length == 0 || line.TrimStart().StartsWith('#')).ToList();
            var timing = existing.Where(line => line.TrimStart().StartsWith('~')).Select(line => line.Trim()).Distinct().Order(StringComparer.Ordinal);
            var own = report.Skipped.Keys
                .Where(name => !list.Intermittent.Contains(name))
                .Order(StringComparer.Ordinal);

            File.WriteAllLines(listPath, header.Concat(own).Concat(timing));
        }
    }
}
