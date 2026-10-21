namespace Savepoint.App.Features.Launch;

/// <summary>
/// Asked of the Games screen by the launch flow when a failure is fixed in the game's definition (the launch settings
/// are wrong, or a folder has moved): the screen opens the game editor for the game. The flow never opens the editor itself.
/// </summary>
/// <param name="GameId">The game to edit.</param>
public sealed record EditGameRequest(string GameId);
