namespace Savepoint.App.Tests.Platform.Fakes;

internal static class MutexCrash
{
    // Takes the named mutex on a thread that then exits without releasing it, which is what a crashed copy leaves behind.
    // Keep the returned mutex reachable until the test is done, or collecting it destroys the abandoned state.
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
