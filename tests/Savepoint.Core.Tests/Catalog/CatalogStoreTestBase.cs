using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Catalog;

public abstract class CatalogStoreTestBase : IDisposable
{
    protected CatalogStoreTestBase()
    {
        Dir = Directory.CreateTempSubdirectory("savepoint-catalog-").FullName;
        Store = new CatalogStore(Dir);
    }

    protected string Dir { get; }

    protected CatalogStore Store { get; }

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected string PathOf(string fileName) => Path.Combine(Dir, fileName);

    protected string[] FileNames() =>
        Directory.GetFileSystemEntries(Dir).Select(p => Path.GetFileName(p)).Order(StringComparer.Ordinal).ToArray();

    public void Dispose()
    {
        Directory.Delete(Dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    protected sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
