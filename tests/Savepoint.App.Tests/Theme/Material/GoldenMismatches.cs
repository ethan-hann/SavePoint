using System.Globalization;
using System.Text;

namespace Savepoint.App.Tests.Theme.Material;

// Walks a golden table without stopping at the first wrong value: each row is checked, every mismatch is written down with the row's
// inputs, and one assertion at the end reports them all, so a change to the color math shows how far it reaches in one run.
internal sealed class GoldenMismatches
{
    // V8 and the platform libm differ in the last bits of atan2 and pow; ARGB results are still compared exactly.
    private static readonly double _tolerance = 1e-10;

    private readonly StringBuilder _lines = new();
    private int _count;
    private int _rows;

    public void Row() => _rows++;

    public void Exact(string row, string what, string expected, string actual)
    {
        if (expected != actual)
        {
            Add(row, what, expected, actual);
        }
    }

    public void Same(string row, string what, double expected, double actual)
    {
        if (expected != actual)
        {
            Add(row, what, R(expected), R(actual));
        }
    }

    public void Close(string row, string what, double expected, double actual)
    {
        if (!(Math.Abs(expected - actual) <= _tolerance))
        {
            Add(row, what, R(expected), R(actual));
        }
    }

    // The guard on the table's size makes a truncated or emptied golden file fail instead of passing with nothing checked.
    public void AssertNone(string table, int expectedRows)
    {
        Assert.True(_rows == expectedRows, $"{table}: the golden file has {_rows} rows, the test expects {expectedRows}.");
        Assert.True(_count == 0, $"{table}: {_count} mismatches in {_rows} rows.{Environment.NewLine}{_lines}");
    }

    private static string R(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private void Add(string row, string what, string expected, string actual)
    {
        _count++;
        _lines.AppendLine($"  {row}: {what} expected {expected}, actual {actual}");
    }
}
