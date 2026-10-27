# SavePoint guide for screen authors

How to build a screen in `Savepoint.App`. The headless renders in `render-output/` show each screen as built, and `docs/decisions.md` records every design call.

## Where things go

- A feature lives in `src/Savepoint.App/Features/<Area>/` (view models, views, dialogs), with tests in `tests/Savepoint.App.Tests/Features/<Area>/`.
- Its copy lives in `src/Savepoint.App/Localization/Assets/Lang/en/<area>.json`. Keys are literal strings and enter the catalog with the code that uses them.
- Copy says each fact once, where it belongs: the full Recently removed rule lives in the help page's Recently removed section, and dialogs only name it. Copy never restates the title, label or button beside it, and no helper line only repeats its label.
- A feature is wired in exactly one file, `src/Savepoint.App/Composition/FeatureComposition.<Feature>.cs`. Nothing else in the composition changes.
- Views are found by name: `FooViewModel` is shown by `FooView` in the same folder, and `FooDialogViewModel` by `FooDialogView`.

## Shell API (`Savepoint.App.Features.Shell`)

- A screen implements `IScreen { Destination; OnShown() }`. It is built by an `IScreenFactory { Destination; Create(IShellContext) }` that the feature's composition method returns.
- `IShellContext`:
  - properties `SelectedGame`, `Games`, `Layout`, `Destination`, `LayoutToggle`, `LaunchFlow`;
  - events `LibraryRefreshed`, `AddGameRequested`;
  - calls `Navigate`, `SelectGame`, `RefreshLibraryAsync` (call it after any game or profile change; the shell also refreshes when a non-launch job finishes), `SetLayoutAsync`, `SettingsChanged(AppSettings)` (for settings saved elsewhere), `RequestAddGame(prefill)`, and `ShowPane(vm)` / `ClosePane()` (a full-pane takeover, used by the game editor);
  - `ShowActivity(gameId)` opens Activity (`NavDestination.Activity`) for that game, or for every game when the id is null. It raises `ActivityRequested` first, so an already shown screen takes the scope.
- `ILaunchFlowHost` (D13.19): `BeginAsync(gameId, LaunchTarget)`, `BeginSwitchAsync(gameId, LaunchTarget)` (Try again and the recovery buttons stay a switch), `IsActive(gameId)`, `InlineContentFor(gameId)` (the view model of the launch banner, running card, saving card or failure card, or null), `InlineContentChanged`, `SettingsSaved` (the shell takes the stored settings) and `EditGameRequested` (a game id, and Games opens the editor).
  - A game in a Steam library that runs its program first asks for Steam when it is not running: a card with Start Steam, Play without Steam and Not now, then a "Waiting for Steam to start" card with Stop waiting that ends once Steam is signed in (D67). After play, "Checking the game has closed" shows while SavePoint watches for a relaunch, and a save refused because the game still runs offers Choose again.
  - Games pins the banner and the checking, saving and running cards at the bottom of the pane above the storage footer. It shows a failure or partial-result card under the profile table, scrolled into view.
  - A launch started from History navigates to Games.
- `IGameEditorFactory` (`Features/Common/Navigation`): `CreateForExisting(shell, gameId)` and `CreateForNew(shell, prefill)`. The Games screen opens the result with `ShowPane`.
- The layout toggle in a header: `<shell:LayoutToggleView DataContext="{Binding Shell.LayoutToggle}"/>`.

### The pill, toasts, and the room under them

- The job status pill is a button that floats in the content area's bottom-left corner (past the rail in the sidebar layout). It is capped at 360 px and trims long text, with the full text in its tooltip and name. The shell's layout never moves when it shows.
- The pill opens Activity on the running job's game, or on every game for a job that spans them all or for several jobs.
- Toasts float over the content's bottom-right corner, in a stack of at most a fifth of the window height (136 px at most), with "Dismiss all" at the top of its scrolling list. The pill stands above the toasts, and the tour's card stays above both.
- Nothing ends under the pill or the toasts. Games, History and Activity end with a `pill-room` strip. It grows from 0 to the room the pill (64 px) and the toast stack need, and lifts their pinned footers, banners and cards above them. Settings, Help and the game editor get the same room as scroll space under their last row.
- On Games, a pane shorter than 300 px has no room for a pinned launch card. There the banner and the checking and running cards scroll with the profiles, and scroll into view when they arrive.

### Where features sit

- **Search (D64):** a search box over the games list (the rail and the library shelf, but not the narrow icon rail), above a game's profile table, and in History beside a Saves / Game and mods choice. Ctrl+F (Cmd+F on macOS) focuses the search of the screen in front. Ctrl+Shift+F (Cmd+Shift+F) focuses the games search. Both do nothing behind a dialog or the tour. Each search box's tooltip, History's included, names its key. The profile search is pinned above the profile table when there is room (D64.3). Escape clears. Every "no matches" message, including the template and library pickers, has a Clear search button that returns focus to the box. While a search narrows the profile table, its header shows the count.
- **Profile row menu:** More actions and a right-click menu (also the Menu key and Shift+F10 on a selected row, which open it below that row) show the same items from one list, `ProfileRowViewModel.MenuEntries`, including "Open game folder", "Open mods folder" and "Open saves folder" for the folders the game has. Each open item's tip says it opens the folder as it is now, which may hold another profile's files.
- **New profile wizard:** the first step offers "Start with no saves" and, for a game with a mods folder, "Start with no mods" (D65). A box is off, with its reason as help text, until that folder's files are in a snapshot.
- **Interface size (D63):** in Settings > Appearance. Sizes the screen cannot hold are off, with the reason.
- **Activity:** in the sidebar layout, a chip next to History in the Games tab strip. The library layout uses the top navigation.
- **Free up space:** in the sidebar layout's storage footer, with its progress and result. The library layout puts it in the game's menu. Once the estimate is in, Settings' row and every confirmation say about how much space it frees (D66).
- **Switch without playing (D49):** the Games row menu starts with "Switch to this profile". History's cards, timeline entries and save rows offer "Go back without playing" beside "Go back and play". Both run the launch flow, worded for a switch, and stop before the game starts. A toast says that the files are in place, or "Already in place" shows when nothing needs loading.
- **Add games from your libraries (D52):** drawn first in the rail, on the library-layout shelf and on the first-run screen, before "Add a game". It opens a checklist of installed Steam and GOG games, one confirmation that names every game's folders, and a run dialog with progress, "Stop after this game" and a result list.
- **Templates:** the template picker has a search box over a scrolling list (D53). It opens from first run, from "Pick from the game list" in Add a game's header, and from the library picker's pointer below its list. Add a game's Name field also lists up to 8 matches as the player types: Down moves into the list, Enter takes the top match, Escape closes it, and a pick returns focus to Name (D53.6).
- **Game editor (D19):** a column of cards, each with a heading and a Required, Recommended or Optional tag: Game (name, install folder, how to start), Saves, Mods ("Mods outside the game folder", D61, and "Mod files inside the game folder", with Choose files and Choose folders pickers, #247), Artwork, and a closed More settings card (leave-out rules, extra options, the program name, the Steam app ID). The header's buttons wrap under the title in a narrow window. Folder fields have location pills named for their field. Chips in its lists change in place: Enter, Space, F2, a double-click or a screen reader's invoke opens a chip's text, Enter keeps it and Escape drops it. Explanations are question buttons with their own text ("Does my store sync saves?", "When do I list mod files?", "How do these rules work?"), each linking to How SavePoint works where it has a topic, and the rules say they are patterns, not regular expressions (D7). Only the cloud-saves-on warning, the Steam update warning (Through Steam only) and the process-name nudge show unasked.

## Shared components (`Savepoint.App.Features.Common`)

- **Dialogs:** `IDialogs.ShowAsync<T>(DialogViewModel<T>)`, `ConfirmAsync(ConfirmRequest(Title, Message, ConfirmLabel, Destructive, CancelLabel, Typed))`, `PickFolderAsync`, `PickOpenFileAsync`, `PickSaveFileAsync`. Custom dialogs derive from `DialogViewModel<T>` (`Title`, `CanDismiss`, `DialogWidth`, `DismissResult`, `Close(result)`). Tests use `FakeDialogs`.
- **Destructive confirmations:** `ConfirmRequest.Typed` makes the user type a word (the game name to delete a game, a shown word to clear all data).
- **Errors:** `ErrorCardViewModel(ErrorInfo, UiFormatter, actions?, supplement?)` with `ErrorCardAction(Label, Command, IsPrimary)`. `ErrorPresentation.For(id)` gives the icon, tone and title key, and every `error.*` id needs an entry.
- **Toasts:** `ToastHostViewModel.Show(ToastMessage(Text, Kind, Details, Duration))`.
- **`UiFormatter`:** `Message(key, args)`, `Message(ErrorInfo)`, `FileSize`, `Number`, `RelativeTime`, `Date`, `Time`, `DateAndTime`, `NameDate`, `Duration`, and the short display forms `ShortDateTime` ("Jul 5, 11:42 PM", with the year only outside the current year), `WeekdayDateTime` ("Sun Jul 5, 11:42 PM"), `TimelineDateTime` ("Jul 2, 2026 · 6:03 PM") and `ShortDate` ("Jul 1").
- **Settings writes** always go through `SettingsWriter` on `FeatureContext`, which changes one field per write so writers never clobber each other. Use `FeatureContext.Clock` for anything time-based.
- **Progress:** `ProgressMilestones` announces long progress at each quarter through a live region.
- **`ArtTile`** (`GameName`, `Size`, `Art`) draws the game's artwork over its name tint.
  - Bind `Art` to a `GameArt` from `IShellContext.Artwork` (`IArtworkProvider`), by game id. The provider decodes off the UI thread into a bounded bitmap cache and refreshes on the api's change event.
  - A tile at least 1.5 times as wide as it is tall uses the wide image, and other tiles the square one. When only one image exists, every tile uses it.
  - The tile shows the tint until an image is ready, and it stays decoration for assistive technology.
- **Announcements and the UI thread:** `AnnouncementRegion` (`Announce`), `IUiDispatcher` (`AvaloniaUiDispatcher`, and `ImmediateUiDispatcher` in tests). Use `PostWhenIdle` for work that must wait until the library has rendered, such as artwork lookups.
- **Opening folders** (the log folder, the notices, a profile's folders) goes through `AppServices.FileRevealer.RevealAsync`, which opens a folder as a folder on every OS and returns false when the file manager could not open it, so the screen can say so. Settings > About and the startup card for a backend that failed to start both offer the log folder (D34).
- **Search:** `SearchBoxView` with `SearchBoxViewModel` (the query, the clear button, a debounced count announcement, and a `Cleared` event the view answers by focusing the box), `SearchQuery`, which matches a row's `SearchTarget` (its searchable words and initials), `ListSync` to add and remove rows in place so a keystroke never rebuilds a list, `UiFormatter.DateSearchText` for the date forms a snapshot answers to, and `FindKey`, the Ctrl+F (Cmd+F) and Ctrl+Shift+F (Cmd+Shift+F) gestures.
- **Folder words:** `FolderRoles` turns a `RepoRole` into the catalog's `install` / `mods` / `external` select value and the folders' order, so every `{role, select}` message has a `mods` branch (`FolderCopyTests` checks it).
- Test images for decoders and tiles are in `tests/Shared/TestImages.cs`.

## Theme vocabulary (`src/Savepoint.App/Styles`, `Theme/`)

- **Colors:** `Md3<Role>Brush` / `Md3<Role>Color` for every MD3 role, plus Warning/Success families, `ErrorCalloutBackground`/`Border` (and Warning/Success), `ScrimOverlay`, `StateHover`, `StatePressed`, `SelectedRow`, `Card`. Always use `DynamicResource`.
- **Tokens:** `Md3Radius{Pill,Card,Panel,Dialog,Row,Control,Small}`, `Md3Space{6,9,12,14,20,26}`, `Md3ScreenPadding`, `Md3DialogPadding` (30, which includes room for a footer button's focus ring), `Md3DialogRingRoom`, `Md3DialogRingOutset` and `Md3DialogBodyRingOutset` (the space a dialog's scrolling body keeps for that ring), `Md3RowPadding`, `Md3DialogShadow`, `Md3MotionDuration{Short,Medium}`. Fonts: `RobotoFont`, `RobotoMonoFont`, `SymbolsFont`, `SymbolsFilledFont`.
- **TextBlock classes:** `page-title dialog-title section-title card-title card-heading row-title body button-text caption timestamp overline overline-small mono (small|large) muted subtle accent error warning success icon (s14|s16|s18|s24|s28, filled)`. Uppercase labels: `{theme:Upper {loc:Loc key}}`.
- **Button classes:** outlined (default), `filled tonal text error error-outlined`, with the modifiers `pill row large icon dashed tile`. Icon-only buttons need `AutomationProperties.Name` (test helpers `AutomationAudit.IconButtonsWithoutNames(root)` and `AutomationAudit.Problems(root)`). Use `ToggleButton.chip` for filter chips.
- **ListBox classes:** `nav (horizontal) rows segmented segmented-tray radio-cards choice swatches`.
- **A choice of one is a `ChoiceListBox`** (launch method, theme, layout, accent, interface size, Sidebar/Library, History view, History's profile filter and its Saves / Game and mods choice, Activity scope, the after-play choice). It looks like a ListBox but reads as a named group of radio buttons, with selected and checked state on every system; arrow keys move and wrap (mirrored right to left), Tab enters at the selected option and leaves in one press. A lint test fails a plain ListBox used this way. Arrow keys on the Sidebar/Library toggle keep focus on the toggle in the layout it switches to.
- **Help links:** an editor question can end in "More in How SavePoint works", which opens that page scrolled to the topic with its heading focused; Back returns to the editor with its scroll position and focus on the question that opened it.
- **Border classes:** `panel card(selected) table table-header table-row(selected,last) strip recessed divider rail topbar dialog scrim callout(error|warning|success|info) input-chip chip art-tile icon-badge`.
  - A `table` draws its stroke inside the curve it clips to. So the `table-header` and the last `table-row` get matching inner corner radii (`Md3RadiusRowInsetTop`, `Md3RadiusRowInsetBottom`) and never paint over the stroke.
  - The last row is found by itself, whether rows are direct children or come from an `ItemsControl`. Set `last` only on a row that is not the final element. `GamesCornerClippingTests` checks the corners pixel by pixel.
- **Icons:** `Savepoint.App.Theme.Icons` constants with the `icon` TextBlock class.
- **The app mark:** `shell:BrandMark` (`Size`, `GlyphSize`), never a hand-built tile. It centers the bookmark by its ink, not its em box, follows theme and accent changes, and is hidden from screen readers.
- The style gallery (`tests/Savepoint.App.Tests/Theme/Gallery/`) shows every style in use.

## Accessibility rules (D36)

- **Contrast:** every text and boundary role pair meets WCAG 2.2 AA (4.5:1 for text, 3:1 for field borders and switch tracks) in both themes and all five accents. `RoleContrastTests` computes the ratios from the generated palette.
- **Names:** an accessible name contains the visible text. Icon-only buttons are at least 32 px square.
- **Toasts:** errors stay until dismissed and can be paused. Every failure also has a durable place, and toasts never cover an error card's actions.
- **Window size:** the window's minimum is 640x420 times the interface size, capped to the screen (D63), and every screen scrolls instead of clipping. Nothing scrolls sideways: the sidebar's games list scrolls alone with its own scrollbar strip, and the library shelf wraps its cards and scrolls in its own area (D69); `HorizontalOverflowAuditTests` checks every app state. History's lists draw only the entries in view (D64.4): the Versions and Saves columns and the cards (one list of group headings and card rows) scroll under fixed headings, and Recently removed scrolls inside its own area when it is long. Tab reaches every entry, and focus returns to the same snapshot after a search, a refresh or a resize. The snapshot list keeps at least 220 px (its heading and three rows) when the Recently removed section is open. Headless renders at 910x432 and 960x460 stand for 150% and 200% scaling.
- **Dialogs:** a long dialog pins its footer (`PinsFooter` on the dialog, drawn by `DialogFrame`), so the actions stay visible while the body scrolls. Screen readers get the dialog's message as well as its title.
- **Motion:** when the system asks for reduced motion, the window carries the `reduced-motion` class, and indeterminate progress shows as a still bar.
- **Announcements** go through `IAnnouncementHost`, and the live regions raise their automation events. Tests assert the events, and the manual dogfood includes a Narrator and NVDA pass.
- **Focus** survives a library refresh in the rail and the shelf.
- **Picking a game:** in the rail, the arrow keys only mark a game. A click, Enter or a screen reader's invoke opens it, even when it is already selected. That closes an open Help page and leaves Settings for Games, while History and Activity stay put and show the new game. On the shelf, a selection is the pick.

## Testing

- Test view models against the scriptable fake backend (`tests/Savepoint.App.Tests/Fakes/`) with `ImmediateUiDispatcher` and `FakeDialogs`. `ScenarioBuilder.Mockup()` builds the sample library the renders show. Fake events fire synchronously.
- After every change to a screen, open its headless renders (Skia, 1040x660, under `render-output/<area>/`) and check them by eye. Use `ThemeHost` so the theme resets after each test.
- A new tab stop goes into the screen's expected order in `TabOrderTests`, in the order it is drawn.
- Every flow works by keyboard (Tab order, Enter and Space, Escape dismisses dialogs), with visible focus, named icon buttons, announced progress milestones, and list and table semantics through automation properties.
- Every destructive action takes a second explicit confirmation. Every failure shows copy (D13.21), and partial results show the partial card (D13.20).
