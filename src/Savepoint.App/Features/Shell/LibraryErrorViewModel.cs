using Savepoint.App.Features.Common.Errors;

namespace Savepoint.App.Features.Shell;

/// <summary>
/// What fills the content region when the library could not be read and has never been read since SavePoint started: the error
/// and a way to try again, in place of a blank Games screen whose buttons do nothing.
/// </summary>
/// <param name="Card">The error card, with its Try again action.</param>
public sealed record LibraryErrorViewModel(ErrorCardViewModel Card);
