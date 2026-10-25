using System.Runtime.InteropServices;
using System.Text;

namespace Savepoint.App.Hosting;

/// <summary>
/// The English text a player pastes into a bug report after a crash: the build, the operating system, where the crash came from, the
/// exception with its stack, and the log folder. It is for support, not for the player to read, so it is not in the catalog.
/// </summary>
public static class CrashSupportText
{
    /// <summary>Builds the text for a crash. It never throws: if the header cannot be built, the exception alone is returned.</summary>
    /// <param name="report">The crash.</param>
    /// <param name="build">The app's build, such as "0.9.4+3e0f9d9".</param>
    public static string Build(CrashReport report, string build)
    {
        var detail = report.Exception.ToString();
        try
        {
            var text = new StringBuilder();
            text.AppendLine($"SavePoint {build}");
            text.AppendLine($"System: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), {RuntimeInformation.FrameworkDescription}");
            text.AppendLine($"Crashed on: {report.Origin}");
            if (report.LogFolder is { } folder)
            {
                text.AppendLine($"Log folder: {folder}");
            }

            text.AppendLine();
            text.Append(detail);
            return text.ToString();
        }
        catch (Exception)
        {
            return detail;
        }
    }
}
