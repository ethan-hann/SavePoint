namespace Savepoint.Core.Catalog;

public sealed record ExternalLocation(string Path = "", ExternalRole Role = ExternalRole.Saves);
