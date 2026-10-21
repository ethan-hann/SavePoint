namespace Savepoint.Core.Api;

public sealed record PostPlayOptions(
    // What this session allows: an older start offers ForkNewProfile, MoveForward and Skip (D14).
    IReadOnlyList<CompletionChoice> Choices,
    CompletionChoice Default,

    // The point UpdateExisting replaces; null when that choice is not offered.
    SaveEntry? UpdateTarget,

    // Points that stop being part of the profile on MoveForward, for its second confirmation.
    int NewerPointCount);
