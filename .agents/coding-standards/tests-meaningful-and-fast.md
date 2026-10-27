---
paths:
  - "tests/**/*.cs"
---

# Tests that can fail and run fast

- **Status:** proposed
- **Date Created:** 2026-10-27 18:21
- **Last Updated:** 2026-10-27 18:21
- **Applies To:**
  - Any test case, theory, fake, rig, or test helper in `tests/`.

## Introduction

Every SavePoint test case must be able to fail when the product breaks, and must cost no more time than the behavior it proves. People and agents copy the tests they find next to their change. So each rule below has a test to copy and a test not to copy. [Test names and structure](tests-naming-and-structure.md) holds the rules for names, temp folders, templates, and waits. This standard adds what a test asserts, how honest its fakes are, and what it costs.

### Purpose

- **Primary:** A green suite means the product works. A case that cannot fail adds to the count and proves nothing.
- **Secondary:** The suite stays fast on the CI machines. No gate retries a test (D60), so a slow or timing-sensitive test is fixed, not run again.
- **Side effect:** The case count measures coverage, and a reviewer can trust it.

### Scope

C# in `tests/Savepoint.Core.Tests`, `tests/Savepoint.App.Tests`, and `tests/Shared`.

## When to Apply

1. **Are you adding or changing a test case?**
   - If yes, apply "Make every case able to fail" and "Extend the test that already checks the behavior".
   - If no, continue.
2. **Does an assertion read a fake's call log, `FakeDialogs`, or another recorder?**
   - If yes, apply "Assert what the player or caller sees". If the call is itself the outcome, see "Exception - the call is the outcome".
   - If no, continue.
3. **Is it a theory, or a test over a data set such as the templates, the goldens, or the app states?**
   - If yes, apply "Make every theory row reach its assertion".
   - If no, continue.
4. **Are you adding or changing a fake or a rig that App tests share, or testing code that reacts to an event, a timer, or a post?**
   - If yes, apply "Keep the fakes honest". A fake that one test class uses is the "one-off fake" in "When NOT to Apply".
   - If no, continue.
5. **Is the subject of the test a fake, a rig, or a tool that only serves the tests?**
   - If yes, apply "Test a test tool only for its own logic".
   - If no, continue.
6. **Does the test build a git repository, a backend workspace, or a launch rig, or open a headless window?**
   - If yes, apply "Keep each test cheap".
   - If no, this standard does not apply.

**Exception - the call is the outcome:** when the outgoing call is what the screen or class is for, the call is the behavior. Examples are the definition the editor sends, the confirmation text the player reads, and the program a launcher starts. Assert the whole call, then also assert one thing the player or caller sees.

**Exception - a picture for a person:** a test that writes a PNG for someone to look at is not coverage. It skips unless an environment variable turns it on, as `DocScreenshotTests` does with `SAVEPOINT_DOC_SCREENSHOTS`. It never counts as a check that the screen works.

**Exception - a child process entry point:** a test method that a test starts as a child copy of the test host, such as `GitProcessJobTests.Child_for_the_kill_test`, returns at once in a normal run. It is plumbing for the test that starts it, not a case to fix.

**Verification step:** run these before you call a test done.

1. Break the product line the test names, or invert its assertion, and run the test. It must fail with an assertion failure. AGENTS.md, section "Code", asks for this mutation check when a test passes on its first run.
2. For a theory, count the failing rows in that broken run. Every row must fail. A row that still passes reached no assertion.
3. Search for render checks that only measure a file. Every hit must sit behind a skip switch:

   ```bash
   grep -rn "Length > 10_000\|File.Exists(path))" tests
   ```

4. List the files that build only the instant dispatcher. A file that tests a handler for an event, a timer, or a post must also use `ManualDispatcher` or `AvaloniaUiDispatcher` in at least one test:

   ```bash
   grep -rln "new ImmediateUiDispatcher()" tests/Savepoint.App.Tests
   ```

5. List the tests that build a fresh world. A class where several tests build the same world copies one template instead:

   ```bash
   grep -rn "new BackendWorkspace()\|LaunchRig.CreateAsync(\|new EngineWorkspace()" tests/Savepoint.Core.Tests
   ```

## When NOT to Apply

- **A small hand-picked theory.** When each row is a different branch or boundary, keep the rows. The branch-name rows in `CaptureTests` each hit one character of `BranchNames.IsValid`. The boundary rows in `DislikeAnalyzerTests` each sit at one edge. A row per case reads better than a loop there.
- **A deliberate change detector on safety data.** `DenylistTableTests` repeats the production denylist on purpose, so a change to the folders SavePoint refuses to track shows up in review.
- **A one-off fake.** A small fake that one test class uses needs no contract test. [Test names and structure](tests-naming-and-structure.md) has the same exception.
- **A negative test beside its positive twin.** `GamesScreenActionsTests.Play_does_nothing_while_the_game_has_a_session` asserts that nothing happened, and the Play test beside it asserts that something did. Together they can fail. Only a negative test with no twin needs a visible-state assertion.
- **Real git where git is the product.** Engine and backend tests run real git on purpose. "Keep each test cheap" asks them to copy their starting state, not to fake git.
- **A pure function.** A test of a parser or a formatter calls the function and asserts the exact result. It has no fake, no window, and no git, so only the first rule applies.

## Background

An audit of the suite (19,087 cases) found these problems:

- About 3,000 Core cases asserted nothing. Almost all were template theory rows. The theory took all 320 templates as rows, but only one template has a mods folder, so 1,595 of 1,600 mods rows checked nothing.
- About 400 more cases repeated another test's check.
- About 100 render cases only checked that a PNG was over 10 KB. A blank or clipped screen passes that.
- About 255 App tests could not catch an App regression. They tested the fakes themselves, or asserted only that a fake recorded a call.
- Fakes answer every call at once on the test's thread and ignore cancellation. Few of their members are compared with the real backend.

The worst case was a UI-thread crash (#246). The Activity screen dropped a row while the list was still committing a click. In the app, `PostWhenIdle` queues work for later. The test dispatcher, `ImmediateUiDispatcher`, runs it at once. So every test passed, and the crash showed only in the real app. `ActivityScreenViewTests.Clicking_a_task_while_the_open_one_has_been_let_go_by_the_registry_selects_it_and_drops_the_old_one` now runs that click on the real dispatcher.

Time in the suite sits in a few places. Most Core time is in classes that run real git, at about 45 ms per git process start, and some of them build their whole world again in every test. App UI tests share one headless UI thread, so they run one at a time, and every window a test opens adds to the queue.

## Coding Standard

### Make every case able to fail

Every case must reach an assertion that a product regression can break. Assert on a value the product computed. Do not assert a value the test itself set, a constant from the test's own fixture, or a fact the compiler already guarantees. Do not count a render as checked because a file exists or is large.

**Correct usage:**

```csharp
// The editor trimmed the name and the path, and generated the id: the product computed each value asserted.
var saved = rig.Saved("AddAsync");
Assert.Matches("^hollow-knight-[a-z0-9]{4}$", saved.Id);
Assert.Equal("Hollow Knight", saved.Name);
Assert.Equal(@"C:\Games\Hollow Knight", saved.Install.Path);
```

**What to avoid:**

```csharp
// The properties are get-only and non-nullable, so the compiler already guarantees all seven.
Assert.NotNull(backend.Games);
Assert.NotNull(backend.Profiles);

// A blank, clipped, or wrongly themed screen still renders to more than 10 KB.
Assert.True(new FileInfo(path).Length > 10_000, "The render looks empty.");

// 41_200_000_000 is the fixture's own number: this checks the test data, not the app.
Assert.Equal(41_200_000_000, usage.BytesUsed);
```

**Examples:**

- `tests/Savepoint.App.Tests/Features/Editor/GameEditorSaveTests.cs`, `Saving_a_new_game_adds_the_trimmed_definition_with_a_generated_id`: every value asserted is one the editor computed.
- `tests/Savepoint.App.Tests/Features/EndToEnd/AppStateAuditTests.cs`, `AppStateAuditTests`: checks a rendered screen through the layout and automation audits, not through the file size.

### Assert what the player or caller sees

A call log, a `FakeDialogs` list, or another recorder only shows that the code asked for something. Assert what the player or the caller can observe afterwards. That can be the bound state of the screen, the value a method returns, or what the backend now holds. A test that says "nothing was saved" also asserts that the stored value is unchanged, so it fails if the action broke and did nothing visible either.

**Correct usage:**

```csharp
var before = await StoredAsync(rig);

rig.Screen.Appearance.SelectedTheme = rig.Screen.Appearance.ThemeOptions.Single(option => option.Preference == ThemePreference.Light);

Assert.Equal(before with { Theme = ThemePreference.Light }, await StoredAsync(rig));
```

**What to avoid:**

```csharp
// Passes when the job event never reaches the shell at all, as long as something called the summary twice.
rig.Backend.Jobs.Complete(job.JobId);

Assert.Single(rig.Backend.Calls.To<ISettingsApi>(nameof(ISettingsApi.GetStorageSummaryAsync)).Skip(1));
```

**Examples:**

- `tests/Savepoint.App.Tests/Features/Settings/SettingsScreenViewModelTests.cs`, `StoredAsync`: reads what the backend now holds instead of the call log.
- `tests/Savepoint.App.Tests/Features/Editor/GameEditorSaveTests.cs`, `GameEditorSaveTests`: the exception case. The definition sent is the screen's output, so the tests assert all of it.

### Make every theory row reach its assertion

Every row of a theory must run the assertion. Do not take a whole data set as rows and then skip the rows that lack the field, through an `if`, a `x is null ||`, or a loop over an empty list. Use one of two shapes:

1. **Filter the rows.** The `MemberData` source yields only the items that carry the field under test.
2. **One test that loops.** A single `[Fact]` visits every item, collects each failure with the item's id, and asserts once with all of them in the message. It also asserts how many items it visited, or that the filtered set is not empty, so an empty loop cannot pass.

Use the loop for a large data set, such as the 320 templates or the color goldens. A row per item there adds thousands of cases and no coverage.

**Correct usage:**

```csharp
// Filter: only the schemes of one group become rows.
public static TheoryData<string> V1Schemes() => SchemeIds(scheme => scheme.Group == "v1");

// Loop: every template is visited, and every offender is named in one message.
var offenders = GameTemplates.All
    .Where(template => template.Definition.Notes is { } notes && notes.Contains("cloud", StringComparison.OrdinalIgnoreCase))
    .Select(template => template.Id)
    .ToList();

Assert.True(offenders.Count == 0, $"{offenders.Count} notes mention cloud saves, such as: {string.Join(", ", offenders.Take(5))}");

// Guard: the data the loops rely on is there.
Assert.Equal(10, GoldenData.Current.Schemes.Count(s => s.Group == "v1"));
```

**What to avoid:**

```csharp
// 320 rows, one per template. 149 templates have no notes, so 149 rows pass without checking anything.
[Theory]
[MemberData(nameof(TemplateIds))]
public void Notes_use_plain_words(string id)
{
    var notes = Template(id).Definition.Notes;

    Assert.True(notes is null || !BannedInNotes().IsMatch(notes), $"Notes for '{id}' use a word the app never shows: {notes}");
}
```

**Examples:**

- `tests/Savepoint.App.Tests/Theme/Material/Md3PaletteGoldenTests.cs`, `SchemeIds`: builds filtered rows. `The_golden_file_covers_every_group_the_tests_rely_on` guards the data.
- `tests/Savepoint.Core.Tests/Catalog/ShippedTemplatesTests.cs`, `Notes_say_nothing_about_cloud_saves` and `No_template_file_repeats_a_key`: loop over every template and name each offender.
- `tests/Savepoint.App.Tests/Localization/CatalogLint.cs`, `LintAssert.NoProblems`: one assertion that lists every problem a loop found.

### Extend the test that already checks the behavior

Before you add a test, search for one that already checks the behavior: the same product member, the same rule, or the same regex. When one exists, add your case to it as an assertion or a row. Add a new test only for a branch or a condition no test reaches. Two tests that make the same check fail together and double the work of every change to that rule.

**Correct usage:**

```bash
# Find the tests that already reach the rule before writing a new one.
grep -rn "AssertResolves\|PathTokenResolver" tests/Savepoint.Core.Tests
```

**What to avoid:**

```csharp
// GameTemplatesTests: the same token regex that ShippedTemplatesTests.AssertResolves already checks on every saves folder, plus more.
Assert.True(external is null || Regex.IsMatch(external.Path, @"^\{[A-Za-z]+\}"));
```

**Examples:**

- `tests/Savepoint.Core.Tests/Catalog/ShippedTemplatesTests.cs`, `AssertResolves`: the one place that checks a saves folder's token, its `..`, and that it resolves.

### Keep the fakes honest

App tests run against `FakeBackend`, so a fake that behaves unlike the real backend lets a real bug pass. Three rules keep a shared fake honest:

1. **Compare what the tests rely on.** When App tests depend on a fake member's ordering, errors, events, threading, or cancellation, a contract story in `tests/Savepoint.App.Tests/Contract/` runs that behavior against the fake and the real backend and compares the two. The story also names the steps that prove it ran, so two runs that failed the same way cannot match. A fake test that claims to work "like the real" code is not that proof.
2. **Honour the cancellation token.** A fake member that takes a `CancellationToken` fails as cancelled when the token is cancelled, as the real Api does (`ApiGuard.RunAsync`). A fake that ignores the token hands a screen success where the app gets `error.operation.cancelled`.
3. **Run deferred work on a real or queueing dispatcher.** Code that reacts to an event, a timer, or a `Post` or `PostWhenIdle` gets at least one test where that work waits. Use `ManualDispatcher` with `PostsImmediately = false`, or `AvaloniaUiDispatcher` in a view test. `ImmediateUiDispatcher` runs every post inside the caller's stack, which the app never does, so it alone cannot catch the #246 kind of crash. A rig that App tests share takes a `dispatcher` parameter.

**Correct usage:**

```csharp
// Contract: one story on both backends, the transcripts compared, and every difference shown.
Assert.True(differences.Count == 0 && fake.Count == real.Count, string.Join(Environment.NewLine + Environment.NewLine, differences));

// Cancellation: like the real Api, a token that is already cancelled fails the call before anything is read.
if (cancellationToken.IsCancellationRequested)
{
    return OperationResult.Failure<CloudSaveInfo>(new RequestCancelledException());
}

// Dispatcher: the tick reaches the read only when the test runs the posted work.
h.Clock.Advance(_settle);
Assert.Equal(0, h.Reads);
Assert.Equal(1, h.Dispatcher.PostedPending);

h.Dispatcher.RunPosted();

Assert.Equal(1, h.Reads);
```

**What to avoid:**

```csharp
// A rig that App tests share, with the instant dispatcher built in and no way for a test to pass another.
var context = new FeatureContext(services, dialogs, text, toasts, new ImmediateUiDispatcher());
```

**Examples:**

- `tests/Savepoint.App.Tests/Contract/ArtworkContractTests.cs`, `The_real_backend_tells_the_same_artwork_story_as_the_fake` and `The_story_plays_the_whole_artwork_life`: the comparison and the proof that the story ran.
- `tests/Savepoint.App.Tests/Fakes/FakeGamesApi.cs`, `GetCloudSavesAsync`: honours the token the way the real Api does.
- `tests/Savepoint.App.Tests/Features/Games/StatusReadSchedulerTests.cs`, `The_tick_reaches_the_read_only_through_the_dispatcher`: holds the post until the test runs it.
- `tests/Savepoint.App.Tests/Features/Activity/ActivityScreenViewTests.cs`, `StartAsync`: takes a `dispatcher` parameter, and the #246 test passes `new AvaloniaUiDispatcher()`.
- `tests/Savepoint.App.Tests/Features/Common/ArtworkProviderTests.cs`, `ManualDispatcher`: holds ordinary posts and idle work until the test runs them.

### Test a test tool only for its own logic

Write tests for a fake, a rig, or a test tool only when the tool has logic of its own that other tests trust. Examples are a parser, a comparison, or an audit that must be able to report a problem. Do not test that a fake stores what it was given, that a recorder keeps calls in order, or that a fixture holds its own constants. Those tests cannot fail for a product regression, and the tests that use the tool already exercise it.

**Correct usage:**

```csharp
// The layout audits are trusted to say "no problems", so each is fed a tree that has problems.
[AvaloniaFact]
public void The_layout_audit_names_each_control_a_person_cannot_reach()
```

**What to avoid:**

```csharp
// Tests the call recorder, not the app.
Assert.Equal<string>(
    ["ISettingsApi.GetAsync", "IGamesApi.ListAsync", "IHistoryApi.GetAsync"],
    backend.Calls.All.Select(c => $"{c.Api}.{c.Member}"));

// Cannot fail.
Assert.Equal(4, 2 + 2);
```

**Examples:**

- `tests/Savepoint.App.Tests/Theme/AuditsCanFailTests.cs`, `AuditsCanFailTests`: proves each audit reports a known-bad tree.
- `tests/Savepoint.Core.Tests/Build/SkipCheckTests.cs`, `SkipCheckTests`: the gate's skip-list comparer parses real XML.
- `tests/Savepoint.Core.Tests/Build/TempConfinementTests.cs`, `TempConfinementTests`: protects the rule that tests never touch a real user folder.

### Keep each test cheap

A test pays for what it builds. Keep these four costs down:

1. **Copy a seeded world.** When several tests start from the same repository, workspace, or launch world, build it once per run and give each test its own copy. Use `RepoTemplates.CopyAsync` for an engine repository, `BackendWorkspace.CopyOf` for a backend workspace, and `TwoProfileTemplates` for a launch world. [Test names and structure](tests-naming-and-structure.md), section "Build expensive state once and copy it", holds the rule. Never hand two tests the same mutable copy. Keep one test that builds the world from scratch, so the build itself stays covered.
2. **Share a window across the cases of a sweep.** A layout or audit sweep opens each state once, then visits its sizes in that one window, largest first. A reused window is only ever shrunk, so every size is one a fresh open could also give. Make a new case per state, not per (state, size) pair.
3. **Keep git calls per test down.** Every git call starts a process. Do the arrangement in the template, not in the test. Assert the result through the Api or through files, without more git calls than the behavior needs.
4. **Wait on a condition.** Never wait a fixed time for something to occur. [Test names and structure](tests-naming-and-structure.md), section "Wait on a condition, not a fixed time", holds the rule and its exceptions. Give each wait a generous deadline. No gate retries a failed test (D60), so a wait that is tight on a fast machine fails on the CI machines and stays failed.

**Correct usage:**

```csharp
// The plain world is built once for the whole run, and each caller gets its own copy of it.
var workspace = BackendWorkspace.CopyOf(template.Workspace);

// One window per state; the sizes are visited in it, largest first.
using var app = await AppStates.OpenAsync(state);
foreach (var (name, width, height) in LayoutAudit.ScaledScreens.OrderByDescending(size => size.Width))
{
    app.Window.Width = width;
    app.Window.Height = height;
    app.Settle();
    problems.AddRange(LayoutAudit.Problems(app.Window).Select(problem => $"[{name}, {width}x{height}] {problem}"));
}
```

**What to avoid:**

```csharp
// ModsFreeUpSpaceTests: every test builds a three-folder game with captures and a continue, about 130 git calls before it starts.
var workspace = new BackendWorkspace();
var game = await workspace.AddGameAsync(workspace.NewGameWithMods("Alpha") with { ModPathHints = ["data"] });

// DialogFooterClearanceTests: one fresh app for every (state, size) row, 120 opens for 30 states.
public async Task Every_dialogs_buttons_and_their_focus_rings_clear_its_edge(string state, string size, double width, double height)
{
    using var app = await OpenAsync(state);
```

**Examples:**

- `tests/Savepoint.Core.Tests/Backend/RemovedWorld.cs`, `RemovedWorld.CreateAsync`: copies a once-per-run template through `BackendWorkspace.CopyOf`, and builds other shapes from scratch.
- `tests/Savepoint.Core.Tests/Git/Engine/RepoTemplates.cs`, `RepoTemplates.CopyAsync`: the engine version.
- `tests/Savepoint.App.Tests/Features/EndToEnd/AppStateAuditTests.cs`, `Every_state_passes_the_naming_target_size_and_scaled_window_audits`: one window per state, sizes looped.
- `tests/Savepoint.App.Tests/Features/EndToEnd/JobPillPlacementTests.cs`, `LargestFirst`: why a reused window is only shrunk.

## Known violations

The audit found these. Fix one when you touch its file. Do not copy it.

- Template theories whose rows skip their assertion: `ShippedTemplatesTests` (`Store_ids_and_folder_names_are_single_safe_names`, the four saves-folder theories, `Per_system_saves_use_only_the_three_system_names`, `Notes_use_plain_words`, `A_template_that_starts_through_a_link_names_a_steam_game`), the five theories in `ModsFolderTemplateTests`, and `GameTemplatesTests.Templates_name_the_saves_folder_with_a_path_token`.
- Duplicated or restated checks: `GameTemplatesTests.Templates_name_the_saves_folder_with_a_path_token`, `GameTemplatesTests.Display_name_is_the_game_name_from_the_definition`, `ShippedTemplatesTests.Linux_and_macos_drafts_use_only_their_own_entry`, and `ReadmeTableTests`.
- Large theories over goldens that a loop can replace: `DynamicColorToneGoldenTests`, `HctTests`, `TonalPaletteTests`, `ContrastTests`, and the per-role rows of `Md3PaletteGoldenTests`.
- Cases that cannot fail: `PlaceholderTests.Test_project_runs`, `CoreBackendTests.Every_api_is_present`, and `SampleScenarioTests.Storage_matches_the_footer_and_settings`.
- Render tests that only check a file: the `*RenderTests` classes under `tests/Savepoint.App.Tests/Features/` (Games, Editor, Help, History, Launch, Library, Settings, Shell), `FinalRenderTests`, `ArtworkRenderTests`, and `JobPillPlacementTests.The_job_pill_renders_over_the_page_and_at_the_end_of_its_scroll`.
- Recorder-only assertions: `ShellViewModelTests.A_finished_optimization_updates_the_sizes`, `SettingsScreenViewModelTests.Choosing_the_theme_already_in_use_saves_nothing`, and the `EstimatesStoppedAtRunStart` flag that `FakeGamesApi` and `FakeSettingsApi` set for themselves.
- Fakes with no contract: `FakeBackend.Call` ignores the cancellation token and raises events before the call returns. `IAppStatusApi` and the job and session events have no contract story. `FakeDialogs` accepts a confirmation that no test queued.
- Rigs with the instant dispatcher built in: `EditorRig`, `HistoryRig`, `SettingsRig`, the App tests' `LaunchRig`, `GamesViewHarness`, `LaunchViewHarness`, and `ShellViewHarness`.
- Worlds rebuilt per test: `ModsFreeUpSpaceTests.World.CreateAsync`, `LeftOutReadOnceTests.CacheAsync`, and `LegacyLocalDataTests.SeedHistoryAsync`.
- Windows opened per size: `DialogFooterClearanceTests.Every_dialogs_buttons_and_their_focus_rings_clear_its_edge` and the overlay methods of `JobPillPlacementTests`.

## Additional Resources

### Project Documentation

- `AGENTS.md`, section "Code": tests first, a red that is an assertion failure, and the mutation check for a test that passes on its first run.
- [Test names and structure](tests-naming-and-structure.md): names, temp folders, "Build expensive state once and copy it", "Wait on a condition, not a fixed time", and "Drive screens through the fake backend".
- [View models, views, and screen wiring](app-mvvm-view-models.md): how view models use the UI dispatcher.
- `docs/decisions.md`, D60: no gate retries a test.
- `docs/developer-guide.md`, sections "Tests" and "Process starts are expensive".
