using Savepoint.Core.Catalog;

namespace Savepoint.Core.Api;

/// <summary>History points a deleted profile would take from one folder.</summary>
/// <param name="Role">Which folder.</param>
/// <param name="PointCount">Points in this folder that no other profile holds.</param>
/// <param name="Bytes">Bytes the next optimization would free for them, or null when that cannot be measured cheaply.</param>
public sealed record FolderPointLoss(RepoRole Role, int PointCount, long? Bytes);
