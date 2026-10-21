namespace Savepoint.App.Features.Games;

// What a profile row asks of the screen that owns it.
internal interface IProfileRowActions
{
    Task PlayAsync(ProfileRowViewModel row);

    Task EditAsync(ProfileRowViewModel row);

    Task DuplicateAsync(ProfileRowViewModel row);

    Task DeleteAsync(ProfileRowViewModel row);
}
