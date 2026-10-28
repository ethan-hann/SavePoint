---
paths:
  - "tests/**/*.cs"
---

# Tests that can fail and run fast

- **Status:** accepted
- **Date Created:** 2026-10-27 18:21
- **Last Updated:** 2026-10-27 21:45
- **Applies To:**
  - Any test case, theory, fake, rig, or test helper in `tests/`.

## Introduction

Every SavePoint test case must be able to fail when the product breaks, and must cost no more time than the behavior it proves. People and agents copy the tests they find next to their change. So each rule below shows a pattern to copy and a pattern not to copy. [Test names and structure](tests-naming-and-structure.md) holds the rules for names, temp folders, templates, and waits. This standard adds what a test asserts, how honest its fakes are, and what it costs.

### Purpose

- **Primary:** A green suite means the product works. A case that cannot fail adds to the count and proves nothing.
- **Secondary:** The suite stays fast on the CI machines. No gate retries a failed test (D60, item 3), so a slow or timing-sensitive test is fixed, not run again.
- **Side effect:** The case count measures coverage, and a reviewer can trust it.

### Scope

C# in `tests/Savepoint.Core.Tests`, `tests/Savepoint.App.Tests`, and `tests/Shared`.

## When to Apply

Walk every question. A "yes" adds rules, and the walk goes on to the next question.

1. **Are you adding or changing a test case?**
   - If yes, apply "Make every case able to fail" and "Extend the test that already checks the behavior". If the test writes a picture or is a child process entry point, see the exceptions below. Then continue.
   - If no, continue.
2. **Does an assertion read a fake's call log, `FakeDialogs`, or another recorder?**
   - If yes, apply "Assert what the player or caller sees". If the call is itself the outcome, see "Exception - the call is the outcome". Then continue.
   - If no, continue.
3. **Is it a theory, or a test over a data set such as the templates, the goldens, or the app states?**
   - If yes, apply "Make every theory row reach its assertion". Then continue.
   - If no, continue.
4. **Are you adding or changing a fake or a rig that more than one test class uses, or testing code that reacts to an event, a timer, or a post?**
   - If yes, apply "Keep the fakes honest". Then continue.
   - If no, continue.
5. **Is the subject of the test a fake, a rig, or a tool that only serves the tests?**
   - If yes, apply "Test a test tool only for its own logic". Then continue.
   - If no, continue.
6. **Does the test build a git repository, a backend workspace, or a launch rig, or open a headless window?**
   - If yes, apply "Keep each test cheap".
   - If no, stop.

**Exception - the call is the outcome:** some calls are what the screen or class is for: the definition the editor sends, the confirmation text the player reads, the program a launcher starts. Then the call is the behavior. Assert the whole call. When the call also changes something the player sees, such as a toast or a list row, assert that too.

**Exception - a picture for a person:** a test that writes a PNG for someone to look at is not coverage, and it never counts as the check that a screen works. It skips unless an environment variable turns it on, through `RenderOutput.SkipUnlessRequested` and `SAVEPOINT_RENDER` (as `DocScreenshotTests` does with `SAVEPOINT_DOC_SCREENSHOTS`). The gate fails on any skip its list does not name, so the test's full name goes in each list under `build/expected-skips/`. [Platform-specific code](csharp-platform-specific-code.md), section "Skip a test on an OS, and list the skip", describes the lists.

**Exception - a child process entry point:** some test methods exist only to be started as a child copy of the test host, such as `GitProcessJobTests.Child_for_the_kill_test`. In a normal run they return at once. They are plumbing for the test that starts them, not cases to fix.

**Verification step:** before you call a test done, run these steps.

1. Break the product line the test names. Run the test. It must fail with an assertion failure. AGENTS.md, section "Code", asks for this mutation check when a test passes on its first run.
2. For a theory, invert its assertion instead. Run it. Every row must fail. A row that still passes never reached the assertion.
3. List the render checks that only measure a file. Read each hit. Every hit should be a picture behind a skip switch, or a test of the tool that saves frames; hits in the classes #257 lists, `AppIconTests` and `BackendContractTests` are known and tracked there:

   ```bash
   grep -rn "FileInfo(path).Length\|Assert.True(File.Exists(path))\|Length > [0-9_]*0_000" tests/Savepoint.App.Tests
   ```

4. List the App test files that hold work back on purpose. Code you change that posts from an event, a timer, or a `Post` call must have a test in one of these files:

   ```bash
   grep -rln "PostsImmediately = false\|new AvaloniaUiDispatcher()" tests/Savepoint.App.Tests
   ```

5. List the tests that build a fresh world. A class where more than one test builds the same world copies one template instead:

   ```bash
   grep -rn "new BackendWorkspace()\|LaunchRig.CreateAsync(\|InitializedFreshAsync()" tests/Savepoint.Core.Tests
   ```

## When NOT to Apply

- **A small hand-picked theory.** When each row is a different branch or boundary, keep the rows: a row per case reads better than a loop. Each branch-name row in `CaptureTests` is one character or clause that `BranchNames.IsValid` refuses. Each boundary row in `DislikeAnalyzerTests` sits at one edge. In such a theory, verification step 1 fails one row, and that is right.
- **A deliberate change detector on safety data.** `DenylistTableTests` repeats the production denylist on purpose, so a reviewer sees any change to the folders SavePoint refuses to track.
- **A fake that one test class uses.** No contract test is necessary for it. [Test names and structure](tests-naming-and-structure.md) also lets such a fake skip `ScenarioBuilder`.
- **A negative test beside its positive twin.** `GamesScreenActionsTests.Play_does_nothing_while_the_game_has_a_session` asserts that nothing happened, and the Play test beside it asserts that something did. Together they can fail. A visible-state assertion is necessary only for a negative test with no twin.
- **Real git where git is the product.** Engine and backend tests run real git on purpose. "Keep each test cheap" asks them to copy their starting state, not to fake git.
- **A pure function.** A test of a parser or a formatter calls the function and asserts the exact result. It has no fake, no window, and no git, so only the first rule applies.

## Background

An audit of the whole suite, about 19,000 cases, found these problems:

- About 3,000 Core cases asserted nothing. Almost all were template theory rows. A theory took every shipped template as a row, then looped over a field most templates lack. Only one template has a mods folder, so almost every mods row checked nothing.
- About 400 more cases repeated another test's check.
- About 100 render cases only checked that a PNG was over 10 KB. A blank or clipped screen passes that.
- About 250 App tests could not catch an App regression. They tested the fakes themselves, or asserted only that a fake recorded a call.
- `FakeBackend` answers every call at once, on the test's thread, and ignores cancellation. The tests compare few of its members with the real backend.

The worst case was a UI-thread crash (#246). The Activity screen dropped a row before the list finished committing a click. In the app, `PostWhenIdle` queues work for later. The test dispatcher, `ImmediateUiDispatcher`, runs it at once inside the caller. So every test passed, and the crash showed only in the real app. `ActivityScreenViewTests.Clicking_a_task_while_the_open_one_has_been_let_go_by_the_registry_selects_it_and_drops_the_old_one` now runs that click on the real dispatcher.

Most Core time sits in classes that run real git. Each git call starts a process, and some of those classes build their whole world again in every test. App UI tests share one headless UI thread, so they run one at a time, and every window a test opens adds to that queue.

## Coding Standard

### Make every case able to fail

Every case must reach an assertion that a product regression can break. Assert a value that passed through the product: one the product computed, changed, stored, or returned. A value the test set is fine to assert when the product handled it on the way, as in a round trip through save and load. Do not assert an echo that no product code touched: a fixture's own constant, a constructor that stores its arguments, or a fact the compiler already guarantees. Do not count a render as checked because the file exists or is large. For pictures and child-process entry points, see the exceptions under "When to Apply".

**Correct usage:**

```csharp
// The message reaches git through the capture, and the test reads back what git stored: trimmed, and otherwise as given.
[InlineData("  spaced   out  ", "spaced   out")]
public async Task The_message_is_stored_without_surrounding_whitespace_and_otherwise_as_given(string message, string expected)
{
    var repo = await _ws.InitializedRepoAsync();
    _ws.Write(repo, "a.txt", "a");

    await _ws.CaptureAsync(repo, "profile-1", message);

    Assert.Equal(expected, await _ws.LogAsync(repo, "profile-1", "%s"));
}
```

**What to avoid:**

```csharp
// The properties are get-only and non-nullable, so the compiler already guarantees each one.
Assert.NotNull(backend.Games);
Assert.NotNull(backend.Profiles);

// A blank, clipped, or wrongly themed screen still renders to more than 10 KB.
Assert.True(new FileInfo(path).Length > 10_000, "The render looks empty.");

// The fixture's own number: this checks the test data, not the app.
Assert.Equal(41_200_000_000, usage.BytesUsed);
```

**Examples:**

- `tests/Savepoint.Core.Tests/Git/Engine/CaptureTests.cs`, `The_message_is_stored_without_surrounding_whitespace_and_otherwise_as_given`: reads back what the product stored.
- `tests/Savepoint.App.Tests/Features/EndToEnd/AppStateAuditTests.cs`, `AppStateAuditTests`: checks a rendered screen through the layout and automation audits, not through the file.

### Assert what the player or caller sees

A call log, a `FakeDialogs` list, or another recorder shows only that the code asked for something. Assert what the player or the caller can observe afterwards: the bound state of the screen, a toast, the value a method returns, or what the backend now holds. A test that says "nothing was saved" also asserts that the stored value or the screen is unchanged. Then it fails when the action broke in some other way. When the call itself is the outcome, see "Exception - the call is the outcome".

**Correct usage:**

```csharp
await Save(rig, editor);

// What the library now holds, and the toast the player reads.
var stored = Assert.Single(FakeAssert.Ok(await rig.Backend.Games.ListAsync(TestContext.Current.CancellationToken)), game => game.Name == "Hollow Knight");
Assert.Equal(["**/*.dmp", "**/*.mdmp"], stored.IgnorePaths);
var toast = Assert.Single(rig.Toasts.Toasts);
Assert.Equal("The rule **/*.log now applies to the game's own folder only, so your saves stay in your snapshots.", toast.Text);
```

**What to avoid:**

```csharp
// Passes when the sizes on screen never change, as long as something called the summary a second time.
rig.Backend.Jobs.Complete(job.JobId);

Assert.Single(rig.Backend.Calls.To<ISettingsApi>(nameof(ISettingsApi.GetStorageSummaryAsync)).Skip(1));
```

**Examples:**

- `tests/Savepoint.App.Tests/Features/Editor/GameEditorSaveTests.cs`, `A_new_game_whose_saves_folder_holds_files_a_default_rule_would_hide_says_which_rule_was_left_off`: the stored game and the toast.
- `tests/Savepoint.App.Tests/Features/Editor/GameEditorSaveTests.cs`, `Saving_a_new_game_adds_the_trimmed_definition_with_a_generated_id`: the call-is-the-outcome case. The definition sent is the screen's output, so the test asserts all of it.

### Make every theory row reach its assertion

Every row of a theory must run the assertion. Do not take a whole data set as rows and then skip the rows that lack the field. The skip can be an `if`, an `x is null ||`, or a loop over an empty list. Use one of two shapes:

1. **Filter the rows.** The `MemberData` source yields only the items that carry the field under test.
2. **Loop in one test.** A single `[Fact]` visits every item and collects each failure with the item's id. It asserts once, with all the failures in the message. It also asserts that the set it visited is not empty, or a separate test guards that data set, so an empty loop cannot pass. When a loop visits the set in parts, such as one system's folders or one optional field, the guard counts each part on its own, because a large part satisfies a count of the whole.

Use the loop when two things hold. Each row would be a cheap check on one data item. The set has hundreds of items, such as the shipped templates or the color goldens. A row per item there adds thousands of cases and no coverage. Keep a row per item when each row is expensive and its name tells the reader which item broke. An example is `AppStates.All`, where every row opens a different state.

**Correct usage:**

```csharp
// Filter: only the schemes of one group become rows.
public static TheoryData<string> V1Schemes() => SchemeIds(scheme => scheme.Group == "v1");

// Loop: one test visits every template and names every offender in one message.
var offenders = GameTemplates.All
    .Where(template => template.Definition.Notes is { } notes && notes.Contains("cloud", StringComparison.OrdinalIgnoreCase))
    .Select(template => template.Id)
    .ToList();

Assert.True(offenders.Count == 0, $"{offenders.Count} notes mention cloud saves, such as: {string.Join(", ", offenders.Take(5))}");

// Guard: the data the loops and rows rely on is there.
Assert.Equal(10, GoldenData.Current.Schemes.Count(s => s.Group == "v1"));
```

**What to avoid:**

```csharp
// Every template is a row, and a template with no notes passes without a check.
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
- `tests/Savepoint.Core.Tests/Catalog/ShippedTemplatesTests.cs`, `Notes_say_nothing_about_cloud_saves`: the loop shape. `GameTemplatesTests.Ships_the_skyrim_template_and_many_more` guards the template set, and `The_data_each_rule_loops_over_is_there_in_every_part_the_rules_check` guards each part the rules loop over.
- `tests/Savepoint.App.Tests/Localization/ErrorCopyTests.cs`, `Every_error_key_in_the_catalog_has_a_sample_error`: a loop with a coverage guard, reported through `LintAssert.NoProblems`.

### Extend the test that already checks the behavior

Before you add a test, search for one that already checks the behavior: the same product member, the same rule, or the same pattern. When the behavior is the same and only the input differs, add a row to that test. When the behavior is the same and you need one more fact about it, add an assertion. A new branch or a new condition gets its own test, named for it ([Test names and structure](tests-naming-and-structure.md)). Two tests that make the same check fail together and double the work of every change to that rule.

**Correct usage:**

```csharp
// One more name git would refuse is one more row of the test that owns the rule, not a new test.
[InlineData("open[bracket")]
[InlineData("back\\slash")]
public void A_branch_name_git_would_refuse_is_rejected_before_any_work(string branch)
```

**What to avoid:**

```csharp
// The same token pattern that TemplateRules.ResolveProblems already checks on every saves folder, along with more.
Assert.True(external is null || Regex.IsMatch(external.Path, @"^\{[A-Za-z]+\}"));
```

**Examples:**

- `tests/Savepoint.Core.Tests/Git/Engine/CaptureTests.cs`, `A_branch_name_git_would_refuse_is_rejected_before_any_work`: one rule, one test, a row per refused name.
- `tests/Savepoint.Core.Tests/Catalog/TemplateRules.cs`, `ResolveProblems`: the one place that checks a saves folder's token, its `..`, and that it resolves.

### Keep the fakes honest

App tests run against `FakeBackend`, so a fake that behaves unlike the real backend lets a real bug pass. Four rules apply to a fake or rig that more than one test class uses:

1. **Compare what the tests rely on.** When App tests depend on a fake member's ordering, errors, events, threading, or cancellation, a contract story in `tests/Savepoint.App.Tests/Contract/` runs that behavior on the fake and the real backend. The story compares the two. The story also names the steps that prove it ran, so two runs that failed the same way cannot match. A fake's own test that claims it works "like the real" code proves nothing about the real code.
2. **Honor the cancellation token.** A fake member that takes a `CancellationToken` fails as canceled when the token is canceled, as the real Api does in `ApiGuard.RunAsync`. A fake that ignores the token hands a screen success where the app gets `error.operation.cancelled`.
3. **Hold deferred work back in at least one test.** Code that reacts to an event, a timer, or a `Post` or `PostWhenIdle` call gets at least one test where that work waits. The test runs the work itself. Use `ManualDispatcher` with `PostsImmediately = false`, or `AvaloniaUiDispatcher` in a view test. `ImmediateUiDispatcher` runs every post at once, inside the caller's stack. The app runs `Post` at once only when the caller is already on the UI thread. It queues `Post` from any other thread, and it always queues `PostWhenIdle`. So the instant dispatcher alone cannot catch the #246 kind of crash. A shared rig takes a `dispatcher` parameter and hands it to everything it builds that posts, toasts included.
4. **Record a question nobody answered.** A command that asks a question and gets no answer fails inside the command's task, and many tests start a command with `_ = ...ExecuteAsync(null)` and never look at that task. So a fake that throws on an unanswered request also records the request, and a check at the end of the test fails it. `FakeDialogs.Unanswered` and `FailOnUnansweredConfirmationAttribute` do this for confirmations. A test that provokes the unanswered question on purpose clears the list.

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
// Contract: the fake's own test claims to match the real work, and nothing runs the real work to compare.
public async Task A_snapshot_named_after_its_profile_opens_with_the_first_snapshot_step_like_the_real_work()

// Cancellation: the overload of FakeBackend.Call without the token never looks at it, so every call succeeds after a cancel.
return _backend.Call(_api, nameof(GetAsync), [gameId], () => OperationResult.Success(Compose(State.Game(gameId))));

// Dispatcher: a shared rig with the instant dispatcher built in, and no way for a test to pass another.
var context = new FeatureContext(services, dialogs, text, toasts, new ImmediateUiDispatcher());
```

**Examples:**

- `tests/Savepoint.App.Tests/Contract/ArtworkContractTests.cs`, `The_real_backend_tells_the_same_artwork_story_as_the_fake` and `The_story_plays_the_whole_artwork_life`: the comparison, and the proof that the story ran.
- `tests/Savepoint.App.Tests/Fakes/FakeGamesApi.cs`, `CheckFormAsync` and `GetCloudSavesAsync(string, CancellationToken)`: both `GetCloudSavesAsync` overloads honor the token the way the real Api does.
- `tests/Savepoint.App.Tests/Features/Common/FailOnUnansweredConfirmationAttribute.cs`: fails the test that built a `FakeDialogs` asked a question nobody answered.
- `tests/Savepoint.App.Tests/Features/Games/StatusReadSchedulerTests.cs`, `The_tick_reaches_the_read_only_through_the_dispatcher`: holds the post until the test runs it.
- `tests/Savepoint.App.Tests/Features/Activity/ActivityScreenViewTests.cs`, `StartAsync`: takes a `dispatcher` parameter, and the #246 test passes `new AvaloniaUiDispatcher()`.
- `tests/Savepoint.App.Tests/Fakes/ManualDispatcher.cs`, `ManualDispatcher`: holds ordinary posts and idle work until the test runs them.

### Test a test tool only for its own logic

Write tests for a fake, a rig, or a test tool only when it has logic of its own that other tests trust. Examples are a parser, a comparison, or an audit that must be able to report a problem. Do not test that a fake stores what it was given, that a recorder keeps calls in order, or that a fixture holds its own constants. Those tests cannot fail for a product regression, and the tests that use the tool already exercise it.

**Correct usage:**

```csharp
// The layout audits are trusted to say "no problems", so each one is fed a tree that has problems.
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

A test pays for everything it builds. Keep three costs down:

1. **Copy a seeded world.** Every git call starts a process, so build the arrangement once per run and copy it into each test. [Test names and structure](tests-naming-and-structure.md), section "Build expensive state once and copy it", holds the rule and its helpers: `RepoTemplates.CopyAsync`, `BackendWorkspace.CopyOf`, and `TwoProfileTemplates`. In the test itself, make no more git calls than the behavior needs, and read results through the Api or the files.
2. **Share a window across the cases of a sweep.** A layout or audit sweep opens each state once, then visits its sizes in that one window, largest first. A reused window is only ever shrunk, so every size is one a fresh open could also give. Make one case per state, not one per state and size.
3. **Wait on a condition, with a generous deadline.** [Test names and structure](tests-naming-and-structure.md), section "Wait on a condition, not a fixed time", holds the rule and its exceptions. No gate retries a failed test (D60, item 3), so a wait that is tight on a fast machine fails on the CI machines and stays failed.

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
// Every test builds a three-folder game with captures and a continue: over a hundred git calls before the test starts.
var workspace = new BackendWorkspace();
var game = await workspace.AddGameAsync(workspace.NewGameWithMods("Alpha") with { ModPathHints = ["data"] });

// A fresh app for every state and size row, so each state opens once per size.
public async Task Every_dialogs_buttons_and_their_focus_rings_clear_its_edge(string state, string size, double width, double height)
{
    using var app = await OpenAsync(state);
```

**Examples:**

- `tests/Savepoint.Core.Tests/Backend/RemovedWorld.cs`, `RemovedWorld.CreateAsync`: copies a once-per-run template through `BackendWorkspace.CopyOf`, and builds other shapes from scratch.
- `tests/Savepoint.App.Tests/Features/EndToEnd/AppStateAuditTests.cs`, `Every_state_passes_the_naming_description_target_size_scaled_window_and_sideways_audits`: one window per state, sizes looped.
- `tests/Savepoint.App.Tests/Features/EndToEnd/JobPillPlacementTests.cs`, `LargestFirst`: why a reused window is only shrunk.

## Known violations

The audit found these. Do not copy them. Each entry must have an issue in the `Code Cleanup` milestone. When the code is fixed, close the issue and delete the entry.

- **Make every case able to fail:** render checks that only check a file and still run in the gate: the render cases in `ActivityScreenViewTests`, `StyleGalleryRenderTests`, and the render calls in `GamesProfileSearchViewTests`, `HistorySearchViewTests`, `ShellGameSearchViewTests`, `PinnedFooterTests`, `HelpAccessTests`, and `RenderSettleTests` (#257).
- **Assert what the player or caller sees:** the `EstimatesStoppedAtRunStart` flag that `FakeGamesApi` and `FakeSettingsApi` set for themselves (#259).
- **Make every theory row reach its assertion:** large golden theories that a loop can replace: `DynamicColorToneGoldenTests`, `HctTests`, `TonalPaletteTests`, `ContrastTests`, and the per-role rows of `Md3PaletteGoldenTests` (#258).
- **Keep the fakes honest:** `ComposedApp` builds the app with the instant dispatcher and has no `dispatcher` parameter (#260).
- **Test a test tool only for its own logic:** `FakeCallLogTests`, and the fake tests that only check a fake stores or echoes its input (#261).

## Additional Resources

### Project Documentation

- `AGENTS.md`, section "Code": tests first, a red that is an assertion failure, and the mutation check for a test that passes on its first run.
- [Test names and structure](tests-naming-and-structure.md): names, temp folders, "Build expensive state once and copy it", "Wait on a condition, not a fixed time", and "Drive screens through the fake backend".
- [Platform-specific code](csharp-platform-specific-code.md): skips and the expected-skip lists.
- [View models, views, and screen wiring](app-mvvm-view-models.md): how view models reach the UI thread through `IUiDispatcher`.
- `docs/decisions.md`, D60, item 3: no gate retries a failed test.
- `docs/developer-guide.md`, sections "Tests" and "Process starts are expensive".
- The `Code Cleanup` milestone on the tracker (https://git.tortal.tech/ethan-hann/SavePoint/milestones): one issue per known violation.
