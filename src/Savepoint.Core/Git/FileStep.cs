namespace Savepoint.Core.Git;

// The step of a switch or discard that could not change a file, so the message names what was being done to it.
internal enum FileStep
{
    // The point's copy of a file could not be written over what the folder holds.
    BringToPoint,

    // A file the point does not hold could not be removed.
    RemoveNew,

    // A file the point being left held, which the target does not, could not be removed.
    RemoveDisplaced,

    // A file standing where the point being left has a folder could not be removed.
    RemoveStanding,
}
