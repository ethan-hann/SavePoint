---
paths:
  - "tests/**/*.cs"
---

# Test names and structure

- **Status:** accepted
- **Date Created:** 2026-10-26 17:55
- **Last Updated:** 2026-10-27 18:21
- **Applies To:**
  - Any test class or test helper in `tests/`.

## Introduction

A SavePoint test name is a sentence that states one behavior. A test builds its own temp folders and never touches a real user folder. Expensive state is built once and copied, and a test that waits, waits on a condition. The App tests run each screen against a scripted fake backend, and the contract tests run one story against the fake and the real backend. `AGENTS.md` holds the binding rules for writing tests first and for temp folders. This standard adds how to follow them.

### Purpose

- **Primary:** A failing test tells the reader which behavior broke without opening the file. The name is the behavior, and the test asserts only that behavior.
- **Secondary:** The suite runs in parallel on every OS without touching the developer's machine, and gives the same answer every run.
- **Side effect:** The test list reads as a specification of the product.

### Scope

C# in `tests/Savepoint.Core.Tests`, `tests/Savepoint.App.Tests`, and `tests/Shared`.

## When to Apply

1. **Are you adding a test?**
   - If yes, apply "Name a test as the behavior it proves" and "Give each test its own temp folder".
   - If no, continue.
2. **Does the test need a git repository with history, or other state that takes seconds to build?**
   - If yes, apply "Build expensive state once and copy it".
   - If no, continue.
3. **Does the test wait for something to happen?**
   - If yes, apply "Wait on a condition, not a fixed time". If the wait is for a clock outside the process, see "Exception - waits for an outside clock".
   - If no, continue.
4. **Is it an App test of a screen?**
   - If yes, apply "Drive screens through the fake backend".
   - If no, this standard does not apply.

**Exception - waits for an outside clock:** A fixed delay is allowed when the thing being waited for is a clock outside the process, such as git comparing file times to the second, or the headless render timer that only moves when ticked. A comment on the delay names the clock.

**Exception - proving nothing happens:** a test that shows something did not happen (a call is still waiting, a file was not written) can only wait a fixed time and look. The wait is a named constant, and a comment on the constant's declaration says what would show inside the window if the behavior were wrong. A test that asserts something did happen polls for it instead.

**Exception - an act on a timer:** a test that releases a hold or cancels a call after a delay says in a comment on that line why that timing matters.

**Verification step:** list fixed sleeps in tests, then read each one:

```bash
grep -rn -B2 --include=*.cs "Thread\.Sleep\|Task\.Delay(" tests
```

Every hit is one of these:

- It sits inside a loop that checks a condition against a deadline or a limit on tries, or races a condition against a deadline in `Task.WhenAny`.
- It is `Task.Delay(Timeout.Infinite, ...)`, a fake that stays put until it is cancelled.
- A comment names an outside clock, or `GitClock.NextSecondAsync` stands in for git's whole-second file times and point dates.
- Its time is a named constant whose declaration comment says what the window proves.
- A comment on the line says why a timer acts at that time.

## When NOT to Apply

- **A pure function.** A test of a parser or a formatter needs no temp folder, no fake backend, and no wait. Call the function and assert.
- **A test that reads a fixed file from the repository.** Tests in `tests/Savepoint.Core.Tests/Build` read `build/` scripts and workflows through `TestEnvironment.RepoRoot`. That is the repository, not a user folder.
- **A test of the composition root.** A test that checks `PlatformFactory.ForCurrentOs` reads the real home folder's path to compare with the result. Reading a path is not touching the folder.
- **A one-off fake.** A small fake that one test class uses, such as `FakeProcessRunner`, can stay a plain class. Not every fake must go through `ScenarioBuilder`.

## Background

xunit v3 runs the tests on the Microsoft Testing Platform. Core tests use `[Fact]` and `[Theory]`. App tests that need the Avalonia dispatcher use `[AvaloniaFact]` and `[AvaloniaTheory]` from Avalonia.Headless. A module initializer in `tests/Shared/TestEnvironment.cs` points `TMP`, `TEMP`, and `TMPDIR` at `.tools/tmp/<os>` inside the repository, so even a missed cleanup stays in the workspace. `TempConfinementTests` checks that.

Most Core tests run real git, and every git call starts a process. On some Windows machines each process start is slow, which is why expensive repository state is built once.

## Coding Standard

### Name a test as the behavior it proves

A test method name is a sentence with its words joined by underscores. It says what happens, and when the case needs it, under what condition. The subject may be a member name or a proper noun, followed by the behavior in plain words. A story test that compares the fake and real backends starts with `The_`.

A test class is named `<Subject>Tests`. When it tests one type, it sits in the folder that matches that type's folder in `src/`. Story and cross-cutting tests live in `Contract/` and `Features/EndToEnd/`.

**Correct usage:**

```csharp
public async Task A_file_another_program_holds_open_still_fails_as_a_file_in_use()
public void ForCurrentOs_builds_the_platform_of_the_running_os()
public void The_real_backend_tells_the_same_first_run_story_as_the_fake()
```

**What to avoid:**

```csharp
// Method, scenario, and result as labels; the name says nothing a reader can act on.
public async Task DeleteAsync_LockedFile_Throws()
```

**Examples:**

- `tests/Savepoint.Core.Tests/Services/Storage/StorageFolderDeleteTests.cs`, `StorageFolderDeleteTests`: behavior sentences.
- `tests/Savepoint.App.Tests/Contract/BackendContractTests.cs`, `BackendContractTests`: story tests.

### Give each test its own temp folder

A test never creates, writes, deletes, or lists anything in a real user folder. A test that writes files gets a fresh folder and deletes it when it ends. Every path the code under test would take from the machine, such as app data, storage, or the home folder, is passed in and points into that folder.

Use the helper that fits:

| Test | Helper |
| --- | --- |
| Core test that needs a whole backend | `BackendWorkspace` |
| Core git engine test | `EngineWorkspace` |
| Core test that needs a folder tree, links, or junctions | `TempTree` or `TempFolder` |
| Any other test | `Directory.CreateTempSubdirectory("savepoint-<area>-")`, deleted in `Dispose` |

**Correct usage:**

```csharp
private readonly string _dataHome = Directory.CreateTempSubdirectory("savepoint-appsmenu-").FullName;

public void Dispose() => Directory.Delete(_dataHome, recursive: true);
```

**What to avoid:**

```csharp
// Writes into the developer's real app data folder.
var settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SavePoint", "settings.json");
File.WriteAllText(settings, "{}");
```

**Examples:**

- `tests/Savepoint.App.Tests/Platform/AppsMenuEntryTests.cs`, `AppsMenuEntryTests`: a temp folder passed in as the data home.
- `tests/Shared/TestEnvironment.cs`, `TestEnvironment.ConfineTemp`: keeps every temp path inside the workspace.

### Build expensive state once and copy it

A repository state that many tests start from is built once per test run by `RepoTemplates.CopyAsync`, then copied into each test's workspace. Do not rebuild it with git in every test.

**Correct usage:**

```csharp
public static async Task<(RepoRef Repo, T Data)> CopyAsync<T>(string name, EngineWorkspace into, Func<EngineWorkspace, Task<(RepoRef Repo, T Data)>> build)
```

**What to avoid:**

```csharp
// Ten git processes per test, in every test that needs this history.
for (var i = 0; i < 10; i++)
{
    await ws.CaptureAsync(repo, "profile-1", $"snapshot {i}");
}
```

**Examples:**

- `tests/Savepoint.Core.Tests/Git/Engine/RepoTemplates.cs`, `RepoTemplates.CopyAsync`: builds a named template once and copies it. `CheckoutTests` uses it.
- `tests/Savepoint.Core.Tests/Launch/Orchestration/TwoProfileTemplates.cs`: builds the two-profile launch world once per free game program name and hands each `LaunchRig.WithTwoProfilesAsync` a copy, so tests running at once never share one.

### Wait on a condition, not a fixed time

A test that waits for work on another thread polls for the result with a deadline, such as `UiWait.UntilAsync`, and asserts on what it finds. A short delay between polls inside such a loop is fine. A fixed `Thread.Sleep` or `Task.Delay` on its own either wastes time or fails on a slow machine.

**Correct usage:**

```csharp
public static async Task<bool> UntilAsync(Func<bool> condition, int milliseconds = 8000)
```

**What to avoid:**

```csharp
// Hopes the work is done in half a second.
Thread.Sleep(500);
```

**Correct usage (the outside-clock exception):**

```csharp
// Git compares a file's time with the index's to the second on Windows, and a file saved in the same second is read again by every later check. A person
// edits a game and keeps a profile more than a second apart, so the test does too.
await Task.Delay(TimeSpan.FromMilliseconds(1100), TestContext.Current.CancellationToken);
```

**Examples:**

- `tests/Savepoint.App.Tests/Features/Common/UiWait.cs`, `UiWait.UntilAsync`: polls with a deadline.
- `tests/Savepoint.App.Tests/Features/EndToEnd/RealBackend/JourneyRig.cs`, `JourneyRig`: a fixed delay that names git's clock.
- `tests/Savepoint.Core.Tests/GitClock.cs`, `GitClock.NextSecondAsync`: the one delay Core tests use to get past git's whole second.
- `tests/Savepoint.Core.Tests/Backend/StorageGateTests.cs`, `StorageGateTests`: a named absence window with the comment on its declaration.

### Skip a test the OS cannot run

A test that cannot run on an OS skips with `Assert.SkipUnless` and is listed in the expected-skip list. [Platform-specific code](csharp-platform-specific-code.md), section "Skip a test on an OS, and list the skip", holds the rule.

### Drive screens through the fake backend

An App test builds the screen's view model with `FakeBackend` and a scenario from `ScenarioBuilder`, or through a rig such as `SettingsRig` that does it. A behavior that both backends must share also gets a story in `Contract/`, which `ContractHarness` runs against the fake and the real backend.

**Correct usage:**

```csharp
var rig = await SettingsRig.ShownAsync(notices: @"C:\Program Files\SavePoint\THIRD-PARTY-NOTICES.md");
```

**What to avoid:**

```csharp
// A real backend for a screen test: slow, and it needs real folders.
var backend = CoreBackend.Create(new BackendOptions { CatalogDirectory = folder });
```

**Examples:**

- `tests/Savepoint.App.Tests/Features/Settings/SettingsRig.cs`, `SettingsRig.ShownAsync`: builds the Settings screen on the fake backend.
- `tests/Savepoint.App.Tests/Fakes/ScenarioBuilder.cs`, `ScenarioBuilder`: the fluent builder for fake state.
- `tests/Savepoint.App.Tests/Contract/ContractHarness.cs`, `ContractHarness`, `FakeContractHarness`, `RealContractHarness`: one story, two backends.

## Additional Resources

### Project Documentation

- `AGENTS.md`, section "Code": write tests first, and never touch real user folders.
- `docs/developer-guide.md`, sections "Tests" and "Process starts are expensive".
- `docs/ui-guide.md`, section "Testing".
- [Platform-specific code](csharp-platform-specific-code.md): OS skips and the expected-skip lists.
- [Comments that say why](csharp-comments.md): what a test comment says.
- [Tests that can fail and run fast](tests-meaningful-and-fast.md): what a test asserts, honest fakes, and what a test costs.

### External Resources

- [xunit v3: what's new](https://xunit.net/docs/getting-started/v3/whats-new)
