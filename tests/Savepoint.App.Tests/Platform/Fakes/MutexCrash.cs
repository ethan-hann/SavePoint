namespace Savepoint.App.Tests.Platform.Fakes;

internal static class MutexCrash
{
    // A crashed copy leaves its mutex abandoned: take it on a thread that exits unreleased, and keep the result reachable until the test ends.
    public static Mutex LeaveHeld(string name)
    {
        Mutex? held = null;
        var thread = new Thread(() =>
        {
            held = new Mutex(false, name);
            held.WaitOne();
        });
        thread.Start();
        thread.Join();
        return held!;
    }
}
