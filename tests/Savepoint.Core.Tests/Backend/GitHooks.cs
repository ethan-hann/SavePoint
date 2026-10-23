using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Backend;

// The engine sees every git call before it runs; a test points the hook at the call it wants to meet.
internal sealed class GitHooks
{
    public Action<GitInvocation>? OnGit { get; set; }

    public void Observe(GitInvocation invocation) => OnGit?.Invoke(invocation);
}
