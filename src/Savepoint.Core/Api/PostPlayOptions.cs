namespace Savepoint.Core.Api;

/// <summary>The choices a finished play session allows (D14).</summary>
/// <param name="Choices">
/// What this session allows. A session that started from an older point offers <see cref="CompletionChoice.ForkNewProfile"/>,
/// <see cref="CompletionChoice.MoveForward"/>, and <see cref="CompletionChoice.Skip"/>. <see cref="CompletionChoice.MoveForward"/> is
/// offered only when every folder the launch left as it was (a point the profile has no commit for in that folder) started on the
/// profile's own history; otherwise the session offers <see cref="CompletionChoice.ForkNewProfile"/> and
/// <see cref="CompletionChoice.Skip"/> (D37).
/// </param>
/// <param name="Default">The choice to preselect; always one of <paramref name="Choices"/>.</param>
/// <param name="UpdateTarget">
/// The save point <see cref="CompletionChoice.UpdateExisting"/> would replace; null when that choice is not offered.
/// </param>
/// <param name="NewerPointCount">
/// How many points stop being part of the profile on <see cref="CompletionChoice.MoveForward"/>, for its second confirmation;
/// 0 when the session did not start from an older point.
/// </param>
public sealed record PostPlayOptions(
    IReadOnlyList<CompletionChoice> Choices,
    CompletionChoice Default,
    SaveEntry? UpdateTarget,
    int NewerPointCount);
