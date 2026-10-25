# SavePoint guide for screen authors

How to build a screen in `Savepoint.App`. The screens follow a visual design from an earlier, unpublished version of SavePoint; the headless renders in `render-output/` show each screen as built, and `docs/decisions.md` records every design call.

## Where things go

- A feature lives in `src/Savepoint.App/Features/<Area>/` (view models, views, dialogs) with tests in `tests/Savepoint.App.Tests/Features/<Area>/`.
- Its copy lives in `src/Savepoint.App/Localization/Assets/Lang/en/<area>.json`. Every user-visible string goes through the catalog; keys are literal strings (never built at run time); a key enters the catalog with the code that uses it. Lint tests enforce all of this. Copy says each fact once, in the place it belongs (the full Recently removed rule lives in the help page's Recently removed section; dialogs only name it), never restates the title, label or button beside it, and leaves out a helper line that only repeats its label.
- It is wired in exactly one file: `src/Savepoint.App/Composition/FeatureComposition.<Feature>.cs`. Nothing else in the composition changes.
- Views are found by name: `FooViewModel` is shown by `FooView` (same folder), dialogs `FooDialogViewModel` by `FooDialogView`.

## Shell API (`Savepoint.App.Features.Shell`)

- A screen implements `IScreen { Destination; OnShown() }` and is built by an `IScreenFactory { Destination; Create(IShellContext) }` returned from the feature's composition method.
- `IShellContext`: `SelectedGame`, `Games`, `Layout`, `Destination`, `LayoutToggle`, `LaunchFlow`; events `LibraryRefreshed`, `AddGameRequested`; calls `Navigate`, `SelectGame`, `RefreshLibraryAsync` (call after any game/profile mutation; the shell also refreshes when a non-launch job finishes), `SetLayoutAsync`, `SettingsChanged(AppSettings)` (tell the shell about settings saved elsewhere), `RequestAddGame(prefill)`, `ShowPane(vm)` / `ClosePane()` (full-pane takeover, used by the game editor), and `ShowActivity(gameId)` (opens the Activity screen, `NavDestination.Activity`, for that game, or for every game when the id is null; it raises `ActivityRequested` first so the screen takes the scope even when it is already shown).
- The job status pill is a button that floats in the content area's bottom-left corner (past the rail in the sidebar layout), capped at 360 px with long text trimmed and the full text in its tooltip and name; the shell's layout never moves when it shows. Toasts float the same way, over the content's bottom-right corner, in a stack that takes at most a fifth of the window height (136 px at most) with "Dismiss all" at the top of its scrolling list; the pill stands above them when both show, and the tour's card stays above them. While either shows, nothing ends under it: Games, History and Activity end with a `pill-room` strip that grows from 0 to the room the pill (64 px) and the toast stack (its height and margins) need, and lifts their pinned footers, banners and cards above them (on Games, a pane shorter than 300 px has no room left for a pinned launch card, so the banner and the checking and running cards scroll with the profiles there and scroll into view when they arrive), and Settings, Help and the game editor get the same room as scroll space under their last row; it opens Activity on the running job's game, or on every game for a job that spans them all or for several jobs.
- The Games screen's tab strip (sidebar layout) has an Activity chip next to History; the library layout reaches Activity through the top navigation.
- The sidebar layout's storage footer has **Free up space** for the game, with its progress and result; the library layout puts that action in the game's menu.
- **Switch without playing (D49):** the Games row menu starts with "Switch to this profile", and History's cards, timeline entries and save rows offer "Switch to this snapshot" beside Go back. Both run the launch flow's check, preview, guards and progress dialog worded for a switch, then stop: a toast says the files are in place and the game was not started, the profile reads "Loaded now", and no after-play dialog follows. "Already in place" shows when nothing needs loading. The errors that used to say "Play this profile first" open a confirmation offering the switch.
- **Add games from your libraries (D52):** drawn first in the rail, on the library-layout shelf and on the first-run screen, before "Add a game". It opens a checklist of the installed Steam and GOG games (search, sort by name, store or known saves folder, "Hide games already added", select all or none; nothing is ticked at the start, and games already added cannot be ticked), then one confirmation naming every game's folders, then a run dialog with progress, "Stop after this game" and a result list. Quitting while it runs asks first. The template picker in "Add a game" has a search box over a scrolling list (D53).
- `ILaunchFlowHost` (shell-owned launch flow, D13.19): `BeginAsync(gameId, LaunchTarget)`, `BeginSwitchAsync(gameId, LaunchTarget)` (the same flow for a switch; Try again and the recovery buttons stay a switch), `IsActive(gameId)`, `InlineContentFor(gameId)` (the launch banner, the running card, the saving card, or the failure card view model; or null). The Games screen pins the banner and the checking, saving, and running cards at the bottom of the pane, above the storage footer, and shows a failure or partial-result card under the profile table, scrolled into view and `InlineContentChanged`; `SettingsSaved` (the shell takes the stored settings) and `EditGameRequested` (game id; the Games screen opens the editor). A launch started from History navigates to Games so its banner and failures are visible.
- `IGameEditorFactory` (`Features/Common/Navigation`): `CreateForExisting(shell, gameId)`, `CreateForNew(shell, prefill)`; the Games screen opens the result with `ShowPane`.
- Layout toggle in a header: `<shell:LayoutToggleView DataContext="{Binding Shell.LayoutToggle}"/>`.

## Shared components (`Savepoint.App.Features.Common`)

- Dialogs: `IDialogs.ShowAsync<T>(DialogViewModel<T>)`, `ConfirmAsync(ConfirmRequest(Title, Message, ConfirmLabel, Destructive, CancelLabel, Typed))`, `PickFolderAsync`, `PickOpenFileAsync`, `PickSaveFileAsync`. Custom dialogs derive from `DialogViewModel<T>` (`Title`, `CanDismiss`, `DialogWidth`, `DismissResult`, `Close(result)`). Tests use `FakeDialogs`.
- Errors: `ErrorCardViewModel(ErrorInfo, UiFormatter, actions?, supplement?)` with `ErrorCardAction(Label, Command, IsPrimary)`; `ErrorPresentation.For(id)` gives icon/tone/title key (every `error.*` id needs an entry).
- `ToastHostViewModel.Show(ToastMessage(Text, Kind, Details, Duration))`.
- `UiFormatter`: `Message(key, args)`, `Message(ErrorInfo)`, `FileSize`, `Number`, `RelativeTime`, `Date`, `Time`, `DateAndTime`, `NameDate`, `Duration`, and the short display forms `ShortDateTime` ("Jul 5, 11:42 PM", year only outside the current year), `WeekdayDateTime` ("Sun Jul 5, 11:42 PM"), `TimelineDateTime` ("Jul 2, 2026 · 6:03 PM"), `ShortDate` ("Jul 1").
- Settings writes: always through `SettingsWriter` on `FeatureContext` (read-modify-write of one field, so writers never clobber each other); `FeatureContext.Clock` for anything time-based.
- Destructive confirmations: `ConfirmRequest.Typed` makes the user type a word (the game name for delete game; a shown word for clear all data).
- `ProgressMilestones` announces long progress at each quarter through a live region.
- `ArtTile` (`GameName`, `Size`, `Art`): draws the game's artwork over its name tint.
  - Bind `Art` to a `GameArt`, which comes from `IShellContext.Artwork` (`IArtworkProvider`) by game id. The provider decodes off the UI thread into a bounded bitmap cache and refreshes on the api's change event.
  - A tile at least 1.5 times as wide as it is tall uses the wide image; otherwise it uses the square one. When only one image exists, every tile uses it.
  - The tile shows the tint until an image is ready, and it stays decoration for assistive technology.
- `AnnouncementRegion` (`Announce`), `IUiDispatcher` (`AvaloniaUiDispatcher`, tests `ImmediateUiDispatcher`; `PostWhenIdle` for work that must wait until the library has rendered, such as artwork lookups).
- Test images for decoders and tiles: `tests/Shared/TestImages.cs`.
- Opening folders (the log folder, the notices) goes through `AppServices.FileRevealer`, which opens a folder as a folder on every OS; Settings > About and the startup card for a backend that failed to start both offer the log folder (D34).

## Theme vocabulary (`src/Savepoint.App/Styles`, `Theme/`)

- Colors: `Md3<Role>Brush` / `Md3<Role>Color` for every MD3 role, plus Warning/Success families, `ErrorCalloutBackground`/`Border` (and Warning/Success), `ScrimOverlay`, `StateHover`, `StatePressed`, `SelectedRow`, `Card`. Always `DynamicResource`.
- Tokens: `Md3Radius{Pill,Card,Panel,Dialog,Row,Control,Small}`, `Md3Space{6,9,12,14,20,26}`, `Md3ScreenPadding`, `Md3DialogPadding`, `Md3RowPadding`, `Md3DialogShadow`, `Md3MotionDuration{Short,Medium}`; fonts `RobotoFont`, `RobotoMonoFont`, `SymbolsFont`, `SymbolsFilledFont`.
- TextBlock classes: `page-title dialog-title section-title card-title card-heading row-title body button-text caption timestamp overline overline-small mono (small|large) muted subtle accent error warning success icon (s14|s16|s18|s24|s28, filled)`. Uppercase labels: `{theme:Upper {loc:Loc key}}`.
- Button classes: outlined (default), `filled tonal text error error-outlined`; modifiers `pill row large icon dashed tile`. Icon-only buttons need `AutomationProperties.Name` (test helpers `AutomationAudit.IconButtonsWithoutNames(root)` and `AutomationAudit.Problems(root)`). `ToggleButton.chip` for filter chips.
- ListBox classes: `nav (horizontal) rows segmented segmented-tray radio-cards choice swatches`.
- Border classes: `panel card(selected) table table-header table-row(selected,last) strip recessed divider rail topbar dialog scrim callout(error|warning|success|info) input-chip chip art-tile icon-badge`.
- Icons: `Savepoint.App.Theme.Icons` constants with the `icon` TextBlock class.
- The style gallery (`tests/Savepoint.App.Tests/Theme/Gallery/`) shows every style in use.

## Rules from the accessibility review (D36)

- **Contrast:** every text and boundary role pair meets WCAG 2.2 AA (4.5:1 for text, 3:1 for field borders and switch tracks) in both themes and all five accents. `RoleContrastTests` computes the ratios from the generated palette, so a theme change that regresses fails a test.
- **Names:** an accessible name contains the visible text, so voice control can say what it sees. Icon-only buttons are at least 32 px square.
- **Toasts:** errors stay until dismissed and can be paused, every failure also has a durable place, and toasts never cover an error card's actions.
- **Window size:** the window's minimum is 640x420, and every screen scrolls rather than clipping. History scrolls as a whole, and its snapshot list keeps at least 220 px (its heading and three rows) when the Recently removed section is open. Headless renders at 910x432 and 960x460 stand for 150% and 200% scaling.
- **Dialogs:** a long dialog pins its footer (`PinsFooter` on the dialog, drawn by `DialogFrame`), so the actions stay visible while the body scrolls. Screen readers get the dialog's message as well as its title.
- **Motion:** when the system asks for reduced motion, the window carries the `reduced-motion` class and indeterminate progress shows as a still bar.
- **Announcements:** announcements go through `IAnnouncementHost`, and the live regions raise their automation events; tests assert the events, and a Narrator/NVDA pass is part of the manual dogfood.
- **Focus:** focus survives a library refresh in the rail and the shelf.
- **Picking a game:** in the rail, moving the selection (arrow keys) only marks a game, so it never pulls the player off a half-filled Settings page; a click, Enter or a screen reader's invoke opens it, even when it is already selected, closing an open Help page and leaving Settings for Games. History and Activity stay put and show the new game. The shelf only shows on Games with no page open, so there a selection is all a pick needs.

## Testing

- View models are tested against the scriptable fake backend (`tests/Savepoint.App.Tests/Fakes/`, `ScenarioBuilder.Mockup()` builds the sample library the renders show) with `ImmediateUiDispatcher` and `FakeDialogs`. Fake events fire synchronously.
- TDD: test first, observed assertion failure, then code; mutation-check first-time passes; record reds in commit bodies.
- Views: headless renders (Skia) at 1040x660 under `render-output/<area>/`, opened and checked by eye after every change to the screen. Use `ThemeHost` so the theme resets after each test.
- Accessibility: every flow keyboard-operable (Tab order, Enter/Space, Escape dismisses dialogs), visible focus, icon buttons named, progress milestones announced, list/table semantics through automation properties.
- Every destructive action takes a second explicit confirmation. Every failure shows copy (D13.21); partial results show the partial card (D13.20).
