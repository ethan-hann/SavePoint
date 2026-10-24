using Savepoint.Core.Library;

namespace Savepoint.Core.Tests.Library;

// GOG's install records as a test hands them over; null means GOG's records are not on this computer.
internal sealed class FakeGogInstalls(IReadOnlyList<GogInstallRecord>? records) : IGogInstalls
{
    public IReadOnlyList<GogInstallRecord>? Records { get; set; } = records;

    public IReadOnlyList<GogInstallRecord>? ReadInstalls() => Records;
}
