using System.Runtime.CompilerServices;

// The OS seams are internal so the composition root sees only the services; the tests build them around fakes.
[assembly: InternalsVisibleTo("Savepoint.App.Tests")]
