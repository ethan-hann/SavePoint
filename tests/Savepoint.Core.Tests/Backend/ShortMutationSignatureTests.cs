using Savepoint.Core.Api;

namespace Savepoint.Core.Tests.Backend;

// D23: a short mutation finishes whatever the caller does, because stopping it halfway would leave the catalog or the folders worse than finishing it.
public class ShortMutationSignatureTests
{
    public static TheoryData<Type, string> ShortMutations => new()
    {
        { typeof(IAppStatusApi), nameof(IAppStatusApi.UseStorageFolderAsync) },
        { typeof(IAppStatusApi), nameof(IAppStatusApi.RecoverCatalogFileAsync) },
        { typeof(IAppStatusApi), nameof(IAppStatusApi.StartWithEmptyStorageAsync) },
        { typeof(IGamesApi), nameof(IGamesApi.ExportDefinitionAsync) },
    };

    [Theory]
    [MemberData(nameof(ShortMutations))]
    public void A_short_mutation_cannot_be_cancelled_by_the_caller(Type api, string member)
    {
        var method = Assert.Single(api.GetMethods(), candidate => candidate.Name == member);

        Assert.DoesNotContain(method.GetParameters(), parameter => parameter.ParameterType == typeof(CancellationToken));
    }
}
