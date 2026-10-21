namespace Savepoint.Core.Git;

public enum GitBundleLayout
{
    // Git for Windows' MinGit: the entry point is cmd/git.exe and it finds its own helpers.
    MinGit,

    // GitHub Desktop's portable git: bin/git needs GIT_EXEC_PATH and friends to find its helpers.
    Dugite,
}
