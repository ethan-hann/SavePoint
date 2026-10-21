# SavePoint v2 - guide for screen authors

How to build a screen in `Savepoint.App`. The visual spec is `../Savepoint/docs/ui/README.md` and its screenshots (`docs/ui/screenshots/<id>.png`); behavior comes from `../Savepoint/plan.md`, `docs/reference/v1-ui.md`, and `docs/decisions.md`.

## Where things go

- A feature lives in `src/Savepoint.App/Features/<Area>/` (view models, views, dialogs) with tests in `tests/Savepoint.App.Tests/Features/<Area>/`.
- Its copy lives in `src/Savepoint.App/Localization/Assets/Lang/en/<area>.json`. Every user-visible string goes through the catalog; keys are literal strings (never built at run time); a key enters the catalog with the code that uses it. Port v1 copy (`docs/reference/v1-en.json`) and v1 keys where the same element exists. Lint tests enforce all of this.
- It is wired in exactly one file: `src/Savepoint.App/Composition/FeatureComposition.<Feature>.cs`. Nothing else in the composition changes.
- Views are found by name: `FooViewModel` is shown by `FooView` (same folder), dialogs `FooDialogViewModel` by `FooDialogView`.

## Shell API (`Savepoint.App.Features.Shell`)

- A screen implements `IScreen { Destination; OnShown() }` and is built by an `IScreenFactory { Destination; Create(IShellContext) }` returned from the feature's composition method.
- `IShellContext`: `SelectedGame`, `Games`, `Layout`, `Destination`, `LayoutToggle`, `LaunchFlow`; events `LibraryRefreshed`, `AddGameRequested`; calls `Navigate`, `SelectGame`, `RefreshLibraryAsync` (call after any game/profile mutation; the shell also refreshes when a non-launch job finishes), `SetLayoutAsync`, `SettingsChanged(AppSettings)` (tell the shell about settings saved elsewhere), `RequestAddGame(prefill)`, `ShowPane(vm)` / `ClosePane()` (full-pane takeover, used by the game editor).
- `ILaunchFlowHost` (shell-owned launch flow, D13.19): `BeginAsync(gameId, LaunchTarget)`, `IsActive(gameId)`, `InlineContentFor(gameId)` (2a banner, 2c running card, saving card, or failure card view model, shown below the profile table; or null) and `InlineContentChanged`; `SettingsSaved` (the shell takes the stored settings) and `EditGameRequested` (game id; the Games screen opens the editor). A launch started from History navigates to Games so its banner and failures are visible.
- `IGameEditorFactory` (`Features/Common/Navigation`): `CreateForExisting(shell, gameId)`, `CreateForNew(shell, prefill)`; the Games screen opens the result with `ShowPane`.
- Layout toggle in a header: `<shell:LayoutToggleView DataContext="{Binding Shell.LayoutToggle}"/>`.

## Shared components (`Savepoint.App.Features.Common`)

- Dialogs: `IDialogs.ShowAsync<T>(DialogViewModel<T>)`, `ConfirmAsync(ConfirmRequest(Title, Message, ConfirmLabel, Destructive, CancelLabel, Typed))`, `PickFolderAsync`, `PickOpenFileAsync`, `PickSaveFileAsync`. Custom dialogs derive from `DialogViewModel<T>` (`Title`, `CanDismiss`, `DialogWidth`, `DismissResult`, `Close(result)`). Tests use `FakeDialogs`.
- Errors: `ErrorCardViewModel(ErrorInfo, UiFormatter, actions?, supplement?)` with `ErrorCardAction(Label, Command, IsPrimary)`; `ErrorPresentation.For(id)` gives icon/tone/title key (every `error.*` id needs an entry).
- `ToastHostViewModel.Show(ToastMessage(Text, Kind, Details, Duration))`.
- `UiFormatter`: `Message(key, args)`, `Message(ErrorInfo)`, `FileSize`, `Number`, `RelativeTime`, `Date`, `Time`, `DateAndTime`, `NameDate`, `Duration`, and the mockup forms `ShortDateTime` ("Jul 5, 11:42 PM", year only outside the current year), `WeekdayDateTime` ("Sun Jul 5, 11:42 PM"), `TimelineDateTime` ("Jul 2, 2026 · 6:03 PM"), `ShortDate` ("Jul 1").
- Settings writes: always through `SettingsWriter` on `FeatureContext` (read-modify-write of one field, so writers never clobber each other); `FeatureContext.Clock` for anything time-based.
- Destructive confirmations: `ConfirmRequest.Typed` makes the user type a word (the game name for delete game; a shown word for clear all data).
- `ProgressMilestones` announces long progress at each quarter through a live region.
- `ArtTile` (`GameName`, `Size`), `AnnouncementRegion` (`Announce`), `IUiDispatcher` (`AvaloniaUiDispatcher`, tests `ImmediateUiDispatcher`).

## Theme vocabulary (`src/Savepoint.App/Styles`, `Theme/`)

- Colors: `Md3<Role>Brush` / `Md3<Role>Color` for every MD3 role, plus Warning/Success families, `ErrorCalloutBackground`/`Border` (and Warning/Success), `ScrimOverlay`, `StateHover`, `StatePressed`, `SelectedRow`, `Card`. Always `DynamicResource`.
- Tokens: `Md3Radius{Pill,Card,Panel,Dialog,Row,Control,Small}`, `Md3Space{6,9,12,14,20,26}`, `Md3ScreenPadding`, `Md3DialogPadding`, `Md3RowPadding`, `Md3DialogShadow`, `Md3MotionDuration{Short,Medium}`; fonts `RobotoFont`, `RobotoMonoFont`, `SymbolsFont`, `SymbolsFilledFont`.
- TextBlock classes: `page-title dialog-title section-title card-title card-heading row-title body button-text caption timestamp overline overline-small mono (small|large) muted subtle accent error warning success icon (s14|s16|s18|s24|s28, filled)`. Uppercase labels: `{theme:Upper {loc:Loc key}}`.
- Button classes: outlined (default), `filled tonal text error error-outlined`; modifiers `pill row large icon dashed tile`. Icon-only buttons need `AutomationProperties.Name` (test helpers `AutomationAudit.IconButtonsWithoutNames(root)` and `AutomationAudit.Problems(root)`). `ToggleButton.chip` for filter chips.
- ListBox classes: `nav (horizontal) rows segmented segmented-tray radio-cards choice swatches`.
- Border classes: `panel card(selected) table table-header table-row(selected,last) strip recessed divider rail topbar dialog scrim callout(error|warning|success|info) input-chip chip art-tile icon-badge`.
- Icons: `Savepoint.App.Theme.Icons` constants with the `icon` TextBlock class.
- The style gallery (`tests/Savepoint.App.Tests/Theme/Gallery/`) shows every style in use.

## Testing

- View models are tested against the scriptable fake backend (`tests/Savepoint.App.Tests/Fakes/`, `ScenarioBuilder.Mockup()` reproduces the mockup data) with `ImmediateUiDispatcher` and `FakeDialogs`. Fake events fire synchronously.
- TDD: test first, observed assertion failure, then code; mutation-check first-time passes; record reds in commit bodies.
- Views: headless renders (Skia) at 1040x660 under `render-output/<area>/`, opened and compared with the matching screenshot. Use `ThemeHost` so the theme resets after each test.
- Accessibility (plan "Theming and accessibility"): every flow keyboard-operable (Tab order, Enter/Space, Escape dismisses dialogs), visible focus, icon buttons named, progress milestones announced, list/table semantics through automation properties.
- Every destructive action takes a second explicit confirmation. Every failure shows copy (D13.21); partial results show the partial card (D13.20).
