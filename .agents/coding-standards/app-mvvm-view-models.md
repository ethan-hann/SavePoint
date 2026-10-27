---
paths:
  - "src/Savepoint.App/**/*.cs"
  - "src/Savepoint.App/**/*.axaml"
---

# View models, views, and screen wiring

- **Status:** accepted
- **Date Created:** 2026-10-26 17:50
- **Last Updated:** 2026-10-26 18:10
- **Applies To:**
  - Any view model, view, or screen factory in `src/Savepoint.App/`.
  - Any App code that reacts to an Api event or reads the time.

## Introduction

A screen in SavePoint is a view model, a view with the same name beside it, and a factory in `Composition/` that builds the view model from the parts it needs. View models use the CommunityToolkit.Mvvm source generators (the library writes the property and command code at build time) and talk to Core only through the Api. This standard covers the shape of a view model, its commands, its view, how a screen is wired, and how it reaches the UI thread.

### Purpose

- **Primary:** A view model can be built and tested without a window, a real backend, or the real clock. Everything it uses comes in through its constructor, from a per-screen `Dependencies` record, so a test passes fakes for each part.
- **Secondary:** Every screen follows one shape, so a reader who knows one screen can find their way in any other.
- **Side effect:** Compiled bindings catch a misspelled property at build time instead of failing in silence at run time.

### Scope

C# and Avalonia XAML (`.axaml`) in `src/Savepoint.App/`.

## When to Apply

1. **Are you adding a screen or a dialog?**
   - If yes, apply every rule below.
   - If no, continue.
2. **Are you adding a property or a command to a view model?**
   - If yes, apply "Generate properties and commands". For a command that one object builds and hands to another, see "Exception - handed-off commands".
   - If no, continue.
3. **Does the view model handle an Api event, or need the time?**
   - If yes, apply "Reach the UI thread through `IUiDispatcher`, and the time through the clock".
   - If no, this standard does not apply.

**Exception - handed-off commands:** A command that one object builds and passes to another is created with `new RelayCommand(...)`. Examples are the remove command a list builds for each chip it adds, and the commands `ShellViewModel` passes into `FirstRunViewModel`. The generator makes commands only for the class that declares the method, so it cannot express these. A view model that exists once per row, such as `ProfileRowViewModel`, declares its own commands with `[RelayCommand]` like any other view model.

**Verification step:** list views that have bindings but no `x:DataType`, then App code that reaches past the Api:

```bash
grep -rL "x:DataType" --include=*.axaml src/Savepoint.App/Features | xargs grep -l "{Binding"
grep -rnE --include=*.cs "using Savepoint\.Core\.(Services|Git|Paths)" src/Savepoint.App
```

Both searches print nothing after the known violations below are fixed.

## When NOT to Apply

- **A view with no bindings.** A view that draws only from `{loc:Loc}` and `{x:Static}`, such as `HelpPictureView`, has nothing for `x:DataType` to check, and needs none.
- **A view model with no generated members.** A class whose state is all hand-written does not need to be `partial`. Do not add attributes to it only to match.
- **View code and shared view infrastructure.** Code-behind in a view, and shared helpers that act on the visual tree such as `Announcements`, may handle focus, scrolling, layout, and screen-reader announcements, and may call `Dispatcher.UIThread` for them. That is view work, not view model work.
- **`Hosting/`.** `AppBootstrap` builds Core through `CoreBackend.Create` and may use Core types the screens never see.

## Background

`docs/architecture.md` (section "Layers") states that views bind only to view models and view models call only the Api interfaces and `IDialogs`. `docs/ui-guide.md` (section "Where things go") describes the screen folders. The fake backend in the App tests implements the same Api interfaces, which is what lets every screen run under test.

## Coding Standard

### Generate properties and commands

A concrete view model is a `public sealed partial class` that derives from `ObservableObject`, or from `DialogViewModel<TResult>` for a dialog. A shared base, such as `DialogViewModel<TResult>`, is `abstract`, and `partial` only when it uses the generators. A bindable property is an `[ObservableProperty]` field. A command is a `[RelayCommand]` method. When the command can be disabled, its gate is a named property passed as `CanExecute`, and the fields it depends on carry `[NotifyCanExecuteChangedFor]`.

**Correct usage:**

```csharp
public sealed partial class ConfirmDialogViewModel : DialogViewModel<bool>
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _typedText = string.Empty;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
```

**What to avoid:**

```csharp
// A command this class owns, written by hand, with its gate hidden in a lambda.
BackCommand = new RelayCommand(Back, () => IsActive && _index > 0);
```

**Correct usage (the handed-off exception):**

```csharp
Items.Add(new ChipItem(trimmed, _text.Message("gameEditor.chip.remove", ("value", trimmed), ("list", GroupName)), new RelayCommand(() => Remove(trimmed))));
```

**Examples:**

- `src/Savepoint.App/Features/Common/Dialogs/ConfirmDialogViewModel.cs`, `ConfirmDialogViewModel.Confirm`: a generated command with a named gate.
- `src/Savepoint.App/Features/Editor/ChipListViewModel.cs`, `ChipItem`: a command built for each chip.

**Known violations:**

- Commands a class owns but builds by hand, in constructors or in `field ??= new RelayCommand(...)` properties. Examples are `TourViewModel.BackCommand`, `GamesScreenViewModel`, `NewProfileWizardViewModel`, `ProfileRowViewModel`, `HistoryScreenViewModel`, and `StartupViewModel`.

### Put the view beside its view model, with `x:DataType`

A view is `<Name>View.axaml` in the same `Features/<Area>/` folder as `<Name>ViewModel.cs`. `ViewLocator` finds it by that name. The root element declares `x:DataType` for its view model, and each `DataTemplate` declares it for its item.

**Correct usage:**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             ...
             x:Class="Savepoint.App.Features.Activity.ActivityScreenView"
             x:DataType="activity:ActivityScreenViewModel">
```

**What to avoid:**

```xml
<!-- With no x:DataType, a misspelled property binds to nothing at run time instead of failing the build. -->
<UserControl x:Class="Savepoint.App.Features.Activity.ActivityScreenView">
  <TextBlock Text="{Binding Titel}" />
```

**Examples:**

- `src/Savepoint.App/Features/Activity/ActivityScreenView.axaml`: the root and its templates declare their types.
- `src/Savepoint.App/Features/Common/ViewLocator.cs`, `ViewLocator.Resolve`: maps `...ViewModel` to `...View`.

### Wire each screen in `FeatureComposition.<Area>.cs`

Each screen has a static factory method in `Composition/FeatureComposition.<Area>.cs`. The method takes the `FeatureContext` and passes the screen factory only what the screen needs, in a `<Screen>Dependencies` record. The record holds Api interfaces (`IJobsApi`, not a Core service), `IDialogs`, the text formatter, the dispatcher, and the clock. A screen factory never keeps the whole `FeatureContext`.

**Correct usage:**

```csharp
public static IScreenFactory ActivityScreen(FeatureContext context) =>
    new ActivityScreenFactory(new ActivityDependencies(context.Services.Jobs, context.Dialogs, context.Text, context.Dispatcher, context.Clock));
```

**What to avoid:**

```csharp
// The screen can now reach every service, and its tests must build all of them.
public static IScreenFactory SettingsScreen(FeatureContext context, SettingsEnvironment? environment = null) =>
    new SettingsScreenFactory(context, environment ?? SettingsEnvironmentOf(context.Services));
```

**Examples:**

- `src/Savepoint.App/Composition/FeatureComposition.Activity.cs`, `FeatureComposition.ActivityScreen`: a factory with its dependencies record.
- `src/Savepoint.App/Features/Activity/ActivityDependencies.cs`, `ActivityDependencies`: only what the screen needs.

**Known violations:**

- `src/Savepoint.App/Features/Settings/SettingsScreenFactory.cs`, `SettingsScreenFactory`, and `src/Savepoint.App/Features/History/HistoryScreenFactory.cs`, `HistoryScreenFactory`: both keep the whole `FeatureContext`.

### Call Core only through the Api

A view model uses the Api interfaces in `Savepoint.Core.Api`, the types they return, and the error types in `Savepoint.Core.Errors`. It does not import `Savepoint.Core.Services`, `Savepoint.Core.Git`, or `Savepoint.Core.Paths`. A helper the App needs from one of those namespaces moves to the Api first. Settings changes go through the shared `SettingsWriter`, one field per write.

**Correct usage:**

```csharp
using Savepoint.Core.Api;
using Savepoint.Core.Errors;
```

**What to avoid:**

```csharp
// Reaches past the Api into a Core service.
using Savepoint.Core.Services.Storage;
```

**Examples:**

- `src/Savepoint.App/Features/Common/Preferences/SettingsWriter.cs`, `SettingsWriter`: the one path for settings writes.

**Known violations:**

- `ErrorCardViewModel`, `StorageSectionViewModel`, and `ClearAllDataSectionViewModel` import `Savepoint.Core.Services.Storage` for `SnapshotFolderNames`. `StorageSectionViewModel` also takes the internal `SettingsStore`.

### Reach the UI thread through `IUiDispatcher`, and the time through the clock

Api events fire on background threads. A view model's handler wraps its body in `IUiDispatcher.Post`. A view model reads the time from the `TimeProvider` its dependencies give it, which is `FeatureContext.Clock`.

**Correct usage:**

```csharp
private void OnJobChanged(object? sender, JobSnapshot job) => _dispatcher.Post(() => Handle(job));
```

**What to avoid:**

```csharp
// Captures the thread's context directly, so a test cannot run the handler at once.
new UiProgress(SynchronizationContext.Current, step => ShowProgress(run, step))
```

**Examples:**

- `src/Savepoint.App/Features/Common/IUiDispatcher.cs`, `IUiDispatcher.Post`: `AvaloniaUiDispatcher` in the app, `ImmediateUiDispatcher` in tests.
- `src/Savepoint.App/Features/Games/FreeUpSpaceViewModel.cs`, `FreeUpSpaceViewModel.OnJobChanged`: an event handler that posts.

**Known violations:**

- `src/Savepoint.App/Features/Shell/StartupViewModel.cs`, `UiProgress`: uses `SynchronizationContext` directly.

## Additional Resources

### Project Documentation

- `docs/architecture.md`, section "Layers".
- `docs/ui-guide.md`, sections "Where things go" and "Shared components".
- [Api results and errors](core-api-results-and-errors.md): how a view model shows a failed result.

### External Resources

- [CommunityToolkit.Mvvm source generators](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/generators/overview)
- [Avalonia compiled bindings](https://docs.avaloniaui.net/docs/basics/data/data-binding/compiled-bindings)
