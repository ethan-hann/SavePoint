using Savepoint.Core.Catalog;

namespace Savepoint.Core.Api;

// Points in this folder that no other profile holds; Bytes is null when it cannot be measured cheaply.
public sealed record FolderPointLoss(RepoRole Role, int PointCount, long? Bytes);
