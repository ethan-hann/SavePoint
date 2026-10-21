namespace Savepoint.Core.Git;

/// <summary>The shape of the git bundle shipped with SavePoint, which decides where the executable is and what it needs.</summary>
public enum GitBundleLayout
{
    /// <summary>Git for Windows' MinGit: the entry point is <c>cmd/git.exe</c> and it finds its own helpers.</summary>
    MinGit,

    /// <summary>GitHub Desktop's portable git: <c>bin/git</c> needs <c>GIT_EXEC_PATH</c> and friends to find its helpers.</summary>
    Dugite,
}
