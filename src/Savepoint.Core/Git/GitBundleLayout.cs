namespace Savepoint.Core.Git;

/// <summary>The shape of the git bundle shipped with SavePoint, which decides where the executable is and what it needs.</summary>
public enum GitBundleLayout
{
    /// <summary>
    /// Git for Windows' MinGit, recognized by <c>cmd/git.exe</c>. SavePoint runs the real git in <c>ucrt64/bin</c> (or
    /// <c>mingw64</c>, <c>clangarm64</c>) directly, with the <c>PATH</c> and <c>GIT_EXEC_PATH</c> the launcher would set,
    /// so each git call is one process instead of two; <c>cmd/git.exe</c> is used only when the real git is missing.
    /// </summary>
    MinGit,

    /// <summary>GitHub Desktop's portable git: <c>bin/git</c> needs <c>GIT_EXEC_PATH</c> and friends to find its helpers.</summary>
    Dugite,
}
