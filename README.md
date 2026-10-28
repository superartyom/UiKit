# DiceModders UiKit

![UiKit Icon](https://raw.githubusercontent.com/DiceModders/UiKit/main/icon.png)

A shared UI toolkit for Dice Kingdoms mods. UiKit owns the global UI runtime, game skins, native hooks and
lifecycle, so a mod can build windows, widgets and native-looking additions without creating its own canvases or
Harmony patches.

> **Status:** active development. The managed API is usable and nullable-annotated; native integrations are
> add-only and target the current Dice Kingdoms build. There are no automated in-game tests; the built-in demo is
> the smoke test.

## Contents

- [Why UiKit](#why-uikit)
- [Features](#features)
- [Requirements](#requirements)
- [Installation](#installation)
- [Quick start](#quick-start)
- [Core concepts](#core-concepts)
- [User guide](#user-guide)
  - [Windows](#windows)
  - [Containers and layout](#containers-and-layout)
  - [Widgets](#widgets)
  - [Images, pointer input and wheel](#images-pointer-input-and-wheel)
  - [Theme and templates](#theme-and-templates)
- [Native integrations](#native-integrations)
  - [Top bar](#top-bar)
  - [Settings](#settings)
  - [Scoreboard](#scoreboard)
  - [Rules civilizations](#rules-civilizations)
  - [Native handle lifecycle](#native-handle-lifecycle)
- [Built-in demo](#built-in-demo)
- [Contracts and limitations](#contracts-and-limitations)
- [Documentation](#documentation)

## Why UiKit

A game UI is more than a window. It needs canvases, sorting orders, fonts, sprites, input handling, native skins,
settings integration and cleanup across scene changes. If every mod implements that itself, mods duplicate work and
fight over the same game objects.

UiKit provides one shared runtime and one ownership model:

- one set of layer canvases for the whole process;
- one theme/asset/template cache;
- one event-driven native lifecycle;
- one safe boundary for callbacks and game callbacks;
- stable scopes and native handles that survive scene changes.

A mod creates a `UiScope`, builds content through `scope.Custom`, and registers native additions through
`scope.Native`. Disposing the scope removes everything it owns.

## Features

| Area | What you get |
|---|---|
| Managed UI | Windows, rows, columns, grids, scrolling, text, images, buttons, toggles, sliders and dropdowns |
| Native integrations | Top-bar buttons, Settings tabs and blocks, scoreboard columns and Rules civilization entries |
| Native look | Game button/slider/dropdown/settings skins when available, with managed fallbacks for managed widgets |
| Lifecycle | Stable handles, scene-safe reattachment, `WhenReady`, scope disposal and idempotent cleanup |
| Input | Pointer events, drag support, fullscreen windows and wheel scrolling |
| Safety | Null/no-throw public contract, guarded callbacks and main-thread-only game access |
| Theming | Shared game font, materials, colors and sprites through `UiTheme` |

## Requirements

- Dice Kingdoms (IL2CPP build) with BepInEx 6 for IL2CPP.
- .NET 6 for the mod project.
- A reference to `DiceModders.UiKit.dll`.

## Installation

1. Build or obtain `DiceModders.UiKit.dll` and copy it to `BepInEx/plugins`.
   Keep only one UiKit assembly installed.
2. Reference the DLL from your mod project.
3. Add UiKit as a BepInEx dependency:

```csharp
using BepInEx;
using BepInEx.Unity.IL2CPP;

[BepInPlugin("author.my-mod", "My Mod", "1.0.0")]
[BepInDependency("DiceModders.UiKit")]
public sealed class MyPlugin : BasePlugin {
    public override void Load() {
        // Create a scope and register content here.
    }

    public override bool Unload() {
        // Dispose the scope here.
        return true;
    }
}
```

4. Create exactly one scope in `Load`, register startup content with `scope.WhenReady`, and dispose the scope in
   `Unload`.

Do not call `UiKit.Initialize` or `UiKit.Shutdown` from a mod. The UiKit plugin owns the shared runtime.

## Quick start

```csharp
using System;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using DiceModders.UiKit;
using DiceModders.UiKit.Custom;

[BepInPlugin("author.my-mod", "My Mod", "1.0.0")]
[BepInDependency("DiceModders.UiKit")]
public sealed class MyPlugin : BasePlugin {
    private UiScope? _ui;

    public override void Load() {
        _ui = UiKit.CreateScope("author.my-mod");

        _ui?.WhenReady(ui => {
            ui.Native.TopBar.AddIconButton("hammer", "Open My Mod", () => OpenWindow(ui));
        });
    }

    private static void OpenWindow(UiScope ui) {
        var window = ui.Custom.Window(w => {
            w.Title("My Mod")
             .Size(460f, 320f)
             .Center()
             .Draggable()
             .Closable();

            w.Body.Paragraph(
                "Everything created through this scope is removed when the mod unloads.");

            w.Body.Row(row => {
                row.Button("OK", () => w.Close())?.FillWidth();
                row.Button("Cancel", w.Close)?.FillWidth();
            });
        });

        window?.BringToFront();
    }

    public override bool Unload() {
        _ui?.Dispose();
        _ui = null;
        return true;
    }
}
```

The example does not create a canvas or a Harmony patch. `OpenWindow` uses the shared top bar and the shared managed
UI layer.

## Core concepts

### Runtime and scopes

`UiKit` is the only public entry point for the shared runtime. A mod never creates or stops it. `UiKit.CreateScope(id)`
returns a `UiScope?`; one scope owns all widgets and native handles created through that mod.

```csharp
var scope = UiKit.CreateScope("author.my-mod");
if (scope == null) {
    // UiKit is not installed or the runtime is unavailable.
    return;
}
```

Scope disposal is idempotent and removes owned windows and native handles in reverse creation order. A disposed scope
cannot register new content.

### Startup registration

`scope.WhenReady(Action<UiScope>)` is the one guaranteed startup registration point:

- the callback runs once on the main thread after the shared layers and game top bar exist;
- callbacks run in registration order;
- a callback registered after the runtime is ready runs immediately;
- disposing the scope before the callback runs cancels it;
- `WhenReady` is not repeated for scenes, matches, tabs or screens.

```csharp
_ui?.WhenReady(ui => {
    // Build startup windows and native registrations here.
});
```

`WhenReady` does not wait for match-only UI. Settings, the scoreboard and the Rules screen may not exist yet. Native
handles remain `Pending` and attach when their screen appears. Build match-specific managed content lazily if it
depends on match art.

Registering native handles directly from `Load` is also supported: the handle is stable and attaches later. For
managed widgets that should use native skins, build them from `WhenReady` or later.

### Main thread and error contract

- All UiKit calls are main-thread-only. UiKit does not provide a dispatcher.
- Public factories return `null` when the scope is unavailable, an argument is invalid, or construction fails.
- Fluent methods no-op on an invalid state and return the same handle.
- `Dispose` is idempotent.
- User callbacks run through an error boundary; one callback failure does not stop other mods or the game loop.
- Missing sprites, fonts or native templates are returned as `null` or replaced by a managed fallback, depending on
  the widget type.
- Nullable annotations are enabled. Treat factory results as nullable and use `?.` where appropriate.

### API map

| Type | Responsibility |
|---|---|
| `UiKit` | `CreateScope`, demo helpers, `IsInitialized`, `IsDemoVisible` |
| `UiScope` | `Id`, `Custom`, `Native`, `WhenReady`, `Dispose`, `IsDisposed` |
| `CustomApi` | `Window`, `Theme` |
| `NativeApi` | `TopBar`, `Settings`, `Scoreboard`, `Rules` |
| `UiElement` | Shared base: `Root`, `IsAlive`, `Dispose` |

### Native handles

Native additions return stable handles (`TopBarButtonHandle`, `SettingsTabHandle`, `SettingsBlockHandle`,
`ScoreboardColumnHandle`, `RulesCivilizationHandle`). The handle does not disappear when a scene changes; only its
attachment state changes.

```csharp
var button = scope.Native.TopBar.AddIconButton("hammer", "Open", OpenWindow);
if (button is { State: NativeState.Failed }) {
    // button.Status explains why it is not attached yet.
}

button?.SetInteractable(false);
```

Common handle members:

| Member | Meaning |
|---|---|
| `Id` | Stable diagnostic id, unique inside the owning scope |
| `State` | `Pending`, `Attached`, `Detached`, `Failed`, `Unsupported` or `Disposed` |
| `Status` | Human-readable detail for the current state |
| `IsAttached` | True while the handle owns live game UI objects |
| `IsDisposed` | True after disposal |
| `Dispose()` | Removes the registration and its native objects |

## User guide

### Windows

`scope.Custom.Window(build)` creates a managed window. The builder is also the handle.

```csharp
var window = scope.Custom.Window(w => {
    w.Title("Kingdom overview")
     .Size(520f, 360f)
     .Position(100f, 80f)
     .Layer(Layer.AboveGame)
     .SortingOrder(10)
     .Draggable()
     .Closable()
     .Fullscreenable()
     .FullscreenButton()
     .Ornament(OrnamentStyle.Border);

    w.OnClose(() => { /* cleanup owned state */ });
    w.Body.Paragraph("Window content goes here.");
});
```

| Member | Purpose |
|---|---|
| `Body` | `UiContainer` for window content |
| `TitleBar` | Optional `UiContainer?` for title-bar buttons/content |
| `Title`, `Size`, `Position`, `Center` | Basic window layout |
| `Layer` | `Layer.BehindGame`, `Layer.Game` or `Layer.AboveGame` |
| `SortingOrder` | Extra order within the selected layer |
| `Draggable`, `Closable` | Enable dragging and the close button |
| `Fullscreenable`, `FullscreenButton` | Allow fullscreen and optionally add the built-in toggle |
| `Ornament` | `OrnamentStyle.None`, `Border`, `BorderWithTwoCorners`, `BorderWithFourCorners` or `Classic` |
| `OnClose` | Callback invoked by `Close()` |
| `Show`, `Hide`, `Toggle`, `BringToFront` | Visibility and ordering |
| `Fullscreen`, `Restore`, `Close`, `Dispose` | Window state changes |

Top-level windows keep their explicit `Size`; weighted layout applies inside `Row`, `Column`, `Grid` and `Body`.

### Containers and layout

`UiContainer` is the layout surface used by `Body`, rows, columns, grids, scroll views and title bars.

```csharp
w.Body.Scroll(list => {
    for (int i = 0; i < 20; i++)
        list.Text("Row " + (i + 1));
}).Height(240f).FillWidth();

w.Body.Row(row => {
    row.Column(left => {
        left.Text("Fixed");
    })?.FixedW(140f);

    row.Column(right => {
        right.Button("Stretch", () => { })?.FillWidth();
    })?.WeightW(1f);
}).Height(48f);

w.Body.Grid(3, 100f, 80f, grid => {
    for (int i = 0; i < 6; i++)
        grid.Button("Cell " + i, () => { })?.Fill();
}).Height(180f);
```

| Method | Description |
|---|---|
| `Text`, `Paragraph` | One-line or wrapped text |
| `Button`, `IconButton` | Text or icon button |
| `Toggle`, `Slider`, `Dropdown` | Native-styled controls when available |
| `Image` | Picture with optional pointer and wheel callbacks |
| `Scroll` | Vertical scroll view with a managed content container |
| `Row`, `Column` | Horizontal or vertical layout group |
| `Grid` | Flexible or fixed-column grid |
| `Spacer`, `Separator` | Layout gap or visual line |
| `Spacing`, `Padding` | Layout spacing and insets |
| `Columns`, `CellSize` | Configure a grid after creation |
| `Clear`, `Remove` | Remove all children or one child |

Layout helpers are extension methods on `UiElement`:

| Helper | Effect |
|---|---|
| `Fixed(width, height)` / `Fixed(size)` | Fixed width and height |
| `FixedW(width)` / `FixedH(height)` | Fix one axis |
| `Width(width)` / `Height(height)` | Alias for a fixed axis |
| `Weight(weight)` | Share free width and height |
| `WeightW(weight)` / `WeightH(weight)` | Share one axis |
| `Fill()`, `FillWidth()`, `FillHeight()` | Weight of `1` |

`UiScroll` exposes `Content`, `Position` (`0` top, `1` bottom), `ToTop()` and `ToBottom()`.

### Widgets

Factories live on `UiContainer`. They can return `null`; use `?.` at the call site if you only need best-effort UI.

| Factory | Returns | Useful members |
|---|---|---|
| `Text` | `UiText` | `Value`, `Wrap`, `Align`, `Color`, `Size` |
| `Paragraph` | `UiText` | Wrapped text |
| `Button` | `UiButton` | `Text`, `Icon`, `Tooltip`, `Selected`, `Interactable`, `OnClick` |
| `IconButton` | `UiButton` | Icon from a theme name or `Sprite` |
| `Toggle` | `UiToggle` | `Value`, `Set`, `Text`, `Interactable` |
| `Slider` | `UiSlider` | `Value`, `Set`, `Range`, `Format`, `WholeNumbers`, `Interactable` |
| `Dropdown` | `UiDropdown` | `Index`, `Selected`, `Count`, `Set`, `Options`, `Interactable` |
| `Image` | `UiImage` | Texture, UV, tint, filter and pointer events |
| `Separator` | `UiContainer` | Thin horizontal line |
| `Spacer` | `UiContainer` | Empty layout space |

Examples:

```csharp
var toggle = w.Body.Toggle("Enable feature", true, enabled => {
    // Push changes into your own data.
});

var slider = w.Body.Slider(0f, 100f, 50f, value => {
    // Optional direct callback; call Set for programmatic updates.
});

var dropdown = w.Body.Dropdown(new[] { "Low", "Medium", "High" }, 1, index => {
    // 0, 1 or 2
});

var button = w.Body.Button("Apply", Apply);
button?.Icon("Misc-Check");
button?.Tooltip("Apply changes");

toggle?.Set(false);
slider?.Set(75f);
dropdown?.Set(2);
```

#### Updating values

Use the handle methods instead of rebuilding content:

```csharp
text.Value = "New status";
text.Wrap(true);
text.Color(Color.yellow);

button?.Text("Running");
button?.Interactable(false);

toggle?.Set(true);
slider?.Set(25f);
slider?.WholeNumbers(true);
dropdown?.Options(new[] { "A", "B", "C" });
dropdown?.Set(1);
```

#### Text

`Text` creates a one-line label with ellipsis. `Paragraph` wraps text and grows its height with the content at the
width the layout gives it.

```csharp
var label = w.Body.Text("Ready.");
var body = w.Body.Paragraph("A longer description that can wrap onto several lines.");

label?.Size(18f).Color(Color.white);
body?.Wrap(true);
```

### Images, pointer input and wheel

`Image(Texture?)` creates a picture with UV, tint, filter and pointer support.

```csharp
var picture = w.Body.Image(Texture2D.whiteTexture);
picture?.UvRect(new Rect(0f, 0f, 1f, 1f));
picture?.Tint(new Color(0.35f, 0.55f, 0.9f, 1f));
picture?.KeepAspect(true);

picture?.OnPointer(e => {
    if (e.Kind == UiPointerKind.Move) {
        // e.Uv, e.Position and e.Delta are inside the picture.
    }
    if (e.Kind == UiPointerKind.Drag) {
        // e.Button is 0 for the left mouse button.
    }
});

picture?.OnScroll(notches => {
    // Positive values scroll up; the library normalises wheel notches.
});
```

Pointer kinds: `Down`, `Up`, `Click`, `Move`, `Drag`, `Enter`, `Exit`.

`UiPointerEvent` contains:

| Member | Meaning |
|---|---|
| `Kind` | Pointer kind |
| `Button` | `0` left, `1` right, `2` middle; `-1` for non-button events |
| `Uv` | Position inside the element, 0..1 |
| `Position` | Position inside the element in canvas units, from bottom-left |
| `Delta` | Movement since the previous event in canvas units |

Wheel priority: an image `OnScroll` handler wins while the pointer is over it. If the image has no scroll handler,
the wheel passes to the surrounding `UiScroll`.

### Theme and templates

`scope.Custom.Theme` exposes the game's look. Lookups return `null` when an asset is not loaded or does not exist.

```csharp
using TMPro;
using UnityEngine;

var theme = scope.Custom.Theme;

Sprite? hammer = theme.Icon("hammer");
Sprite? panel = theme.Sprite("Background-3");
Sprite? ornament = theme.Ornament(OrnamentStyle.Classic);
TMP_FontAsset? font = theme.Font;
Material? material = theme.FontMaterial;
Color textColor = theme.TextColor;
```

The BepInEx configuration controls native widget usage:

```ini
[Widgets]
Templates = PreferNative
```

| Value | Behavior |
|---|---|
| `PreferNative` | Use the game's widget when its template is usable; otherwise build a managed fallback |
| `ManagedOnly` | Never use game templates; native handles become `Unsupported` |
| `NativeOnly` | Use only game widgets; a missing template is reported and the widget is skipped |

Managed widgets built from `WhenReady` or later can use native skins. A widget created before the game's templates are
available may use a managed fallback; recreate it or use the demo rebuild action after entering a match.

## Native integrations

Native APIs insert objects into the game's own UI. They are **add-only**: UiKit does not expose editing or removal of
existing game elements. Native specs, enums and handles live in `DiceModders.UiKit.Native`; add
`using DiceModders.UiKit.Native;` when using the examples below.

Native registrations are declarative and scene-safe. You register a description; the library attaches it when the
target exists, detaches it when the target disappears and reattaches when the target returns. There are no per-screen
callbacks for mods.

### Top bar

```csharp
var open = ui.Native.TopBar.Add(new TopBarButtonSpec {
    Id = "author.my-mod.open",
    IconName = "hammer",
    Tooltip = "Open My Mod",
    Order = 10,
    InitiallyVisible = true,
    InitiallyInteractable = true,
    OnClick = () => OpenWindow(ui),
});

open?.SetInteractable(false);
open?.SetTooltip("Open My Mod");
open?.SetVisible(true);

var same = ui.Native.TopBar.Get("author.my-mod.open");
```

`AddIconButton(iconName, tooltip, onClick, order)` and `AddIconButton(sprite, tooltip, onClick, order)` are
convenience overloads. Lower `Order` values come first among mod buttons. You can also supply a caller-created
`Sprite` instead of a theme icon name.

### Settings

Settings supports two kinds of additions:

- a full mod-owned tab;
- a stable block appended to a built-in page: `Video`, `Audio`, `Input`, `Game` or `Lobby`.

Both use the game's native row prefabs.

#### Custom tab

```csharp
ui.Native.Settings.AddTab("author.my-mod", "My Mod", tab => {
    tab.AddHeader("My Mod settings");
    tab.AddToggle("Enable feature", true, enabled => { });
    tab.AddSlider("Volume", 0f, 100f, 70f, value => { });
    tab.AddDropdown("Mode", new[] { "Simple", "Advanced" }, 0, index => { });
    tab.AddInfo("Settings in this tab are owned by my mod.");
});
```

`SettingsTabSpec` can be used with `SettingsApi.Add(...)`; `AddTab(id, title, build)` is the direct equivalent.
`SettingsTabHandle.Select()` selects the tab when the Settings screen exists.

#### Block on a built-in page

```csharp
ui.Native.Settings.AddBlock(new SettingsBlockSpec {
    Id = "author.my-mod.game",
    Page = SettingsPage.Game,
    Title = "My Mod",
    Order = 10,
    Build = block => {
        block.AddInfo("These rows are added to the game's Game page.");
        block.AddToggle("Enable feature", true, enabled => { });
        block.AddSlider("Intensity", 0f, 10f, 5f, value => { },
            format: "0.0", wholeNumbers: false);
        Action reset = () => { };
        Action apply = () => { };
        block.AddButtons("Actions", ("Reset", reset), ("Apply", apply));
    },
});
```

`SettingsApi.AddBlock(page, title, order)` and `AddBlock(id, page, title, order)` are shorthand methods. A block is
stable across Settings rebuilds and scene changes; its rows are recreated natively when needed.

#### Row kinds

`SettingsBlockHandle` and `SettingsTabHandle` expose the same row methods:

| Method | Result |
|---|---|
| `AddHeader(text)` | Group header |
| `AddToggle(label, value, changed)` | Checkbox/toggle |
| `AddSlider(label, min, max, value, changed, format, wholeNumbers)` | Slider with value text |
| `AddDropdown(label, options, index, changed)` | Dropdown |
| `AddButton(label, buttonText, clicked)` | One button |
| `AddButtons(label, params (text, clicked)[])` | Several buttons in one row |
| `AddStepper(label, valueText, step)` | `-` / value / `+` row |
| `AddInfo(text)` | Wrapped paragraph |

Rows are stable handles (`SettingsRowHandle`). Update them without rebuilding:

```csharp
SettingsRowHandle? info = null;
SettingsRowHandle? stepper = null;

block.AddToggle("Demo", true, value => {
    info?.SetText("Demo: " + value);
    info?.SetEnabled(value);
    info?.SetVisible(true);
});

info = block.AddInfo("Ready.");
stepper = block.AddStepper("Count", () => count.ToString(), delta => count += delta);

row?.SetValue(true);          // Toggle
row?.SetValue(5f);            // Slider
row?.SetValue(1);             // Dropdown index
row?.SetText("New text");     // Info or Stepper
row?.SetEnabled(false);
row?.SetVisible(false);
```

Rows apply their initial values when attached and push changes only when you call a setter or the player interacts.
Info and Stepper text providers are read on attach and after each step; they are not polled.

### Scoreboard

Scoreboard columns are added to every current and future scoreboard row. Cells are cloned from the game's own stat
cell, so they match the board's art.

```csharp
using System.Collections.Generic;
using GameIsland = DiceKingdoms.Game.Island;

var column = ui.Native.Scoreboard.AddColumn("author.my-mod.score", "My score");

column?.SetValue(island, score.ToString());
column?.SetValues(new Dictionary<GameIsland, string> {
    [islandA] = "12",
    [islandB] = "7",
});
column?.ClearValues();

column?.SetTooltip("Score from my mod");
column?.SetIcon(myIcon);
int attachedRows = column?.AttachedCells ?? 0;
```

Values are pushed explicitly and cached. A value set before the scoreboard exists appears when rows attach. The
column is stable across scene changes; rows without a cached value show an empty cell.

`ScoreboardColumnSpec` can be used with `ScoreboardApi.Add(...)`; it accepts `Id`, `Tooltip` and `Icon`.

### Rules civilizations

`Rules.AddCivilization(id, name)` appends an option to the game's Rules civilization dropdown. UiKit only draws and
removes the option. It does not allocate the number, provide a tech tree or change skins: the system that owns the
civilization (its number, tech tree, skins and network sync) registers it elsewhere and passes the number here.

```csharp
var entry = ui.Native.Rules.AddCivilization(civilizationId, "My Civilization");

entry?.SetName("My Civilization MK II");
entry?.Dispose();   // removes the option again
```

The number must be above the built-in civilizations (`0`..`4`). Selecting an option for a number that nothing else has
registered makes the game fail when it looks up that civilization, so only add entries for civilizations that exist.
The handle exposes `CivilizationId` and `Name`.
### Native handle lifecycle

Native handles are never replaced; their attachment state changes.

| State | Meaning |
|---|---|
| `Pending` | Waiting for a target, asset or template |
| `Attached` | Live objects are present in the game UI |
| `Detached` | Was attached before and is waiting to attach again |
| `Failed` | The last attempt failed; `Status` explains why |
| `Unsupported` | The active policy or game does not support this registration |
| `Disposed` | Removed for good |

Use `handle.IsAttached`, `handle.State` and `handle.Status` for diagnostics. `handle.Dispose()` removes an individual
registration; disposing the owning scope removes it automatically.

Native additions are add-only. UI that edits, moves or removes existing game elements is not exposed.

## Built-in demo

The UiKit plugin includes a demo as a manual smoke test. Enable it in `BepInEx/config/DiceModders.UiKit.cfg`:

```ini
[Demo]
Enabled = true

[Widgets]
Templates = PreferNative
```

Or launch the game with `--uikit-demo`. With BepInEx ConfigurationManager installed, `[Demo] Enabled` can be toggled
without restarting.

The demo includes:

- a scrollable widget gallery;
- layer probes for `Layer.Game` and `Layer.BehindGame`;
- window-ornament switching;
- a top-bar button with ordering and interactability checks;
- a custom Settings tab;
- blocks on built-in Settings pages;
- a scoreboard column;

The same actions are available from code:

```csharp
UiKit.ShowDemo();
UiKit.HideDemo();
UiKit.ToggleDemo();
UiKit.RebuildDemo();
```

After entering a match, use **Rebuild** in the demo to recreate managed widgets with native templates. The demo is not
required at runtime and can be disabled.

## Contracts and limitations

- **Main thread only.** UiKit does not provide a dispatcher.
- **No per-screen callbacks.** `WhenReady` fires once. Native handles manage their own attachment.
- **Managed factories may return `null`.** Check the result or use `?.`.
- **Callback failures are isolated.** UiKit logs them and keeps the game loop running.
- **Native integration is add-only.**
- **No automatic reskinning.** Managed windows created before game templates are available use managed fallbacks;
  recreate them or use the demo rebuild action.
- **No managed fallback for native integrations.** Under `ManagedOnly`, native handles become `Unsupported`.
- **No localization wrapper.** Pass already-localized strings.
- **No automated in-game tests.** The demo and a real-match pass are the current validation.

## Documentation

README is the user-facing guide. Implementation and contributor details live separately:

- [Documentation index](docs/README.md)
- [Architecture](docs/ARCHITECTURE.md) — runtime, scopes, events, assets and lifecycle internals
- [Native integrations](docs/NATIVE.md) — Harmony hooks, native state, Settings/scoreboard/Rules internals
- [Development](docs/DEVELOPMENT.md) — build, demo checks, logs, code map and release steps

For build and release instructions, start with [Development](docs/DEVELOPMENT.md).
