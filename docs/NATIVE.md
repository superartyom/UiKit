# Native integrations

This document covers the game-facing part of UiKit: Harmony hooks, native handles and the internal lifecycle of
TopBar, Settings, scoreboard and Rules. User-facing signatures and examples are in the [main README](../README.md).
The general runtime/event model is in [ARCHITECTURE.md](ARCHITECTURE.md).

## Design rules

- Native additions are **add-only**.
- The game remains authoritative for its own UI.
- A native handle is stable for the lifetime of its registration; only the attached game objects change.
- Every game callback has an error boundary.
- Hook groups are installed independently where possible, so a game update that breaks one integration does not
  disable the others.
- There is no recurring retry timer. Attachment is retried from relevant scene, screen, page, row, canvas or scoreboard
  events.

## Native host and state model

`NativeUiHost` owns the list of `NativeHandle` registrations and cached native targets:

- settings screen;
- Rules screen;
- scoreboard screen and rows;
- top bar.

`NativeScene` is a snapshot passed to `NativeHandle.Refresh`; it contains the current top bar, Settings, Rules and
scoreboard rows. Discovery is performed by `GameUi` using Unity object searches with inactive objects included where
appropriate.

The handle state model lives in `NativeHandle`:

| State | Transition |
|---|---|
| `Pending` | Initial state while a target/template is unavailable |
| `Attached` | The handle owns live game UI |
| `Detached` | It was attached before and is waiting for a new target |
| `Failed` | The last attach attempt failed; `Status` carries the reason |
| `Unsupported` | The active policy does not support it, or the game does not |
| `Disposed` | Final state; it never attaches again |

`Detach`/`Dispose` are idempotent. `NativeHandle.Dispose` calls module cleanup, detaches native objects, unregisters
from the host and removes itself from the owning scope.

## Hook groups

The hooks are installed by `NativeHooks.Install`. Mandatory hooks are installed first; optional groups use
`TryPatch` and log a warning if the current game build does not expose the method.

### Required hooks

- `ScoreboardRow.Start` — announces a row after its own cells are built.
- `Rules.Awake` / `Rules.OnEnable` — announces the Rules screen.

### Optional hook groups

- `SettingSetInterface.GenerateElements` — page content generated/rebuilt.
- `SettingSetsInterface.OnEnable` / `OnDisable` — Settings screen shown/hidden.
- `MainMenu.Start` / `Interface.Start` — late UI-ready fallback.
- `ScoreboardRow.Island` setter — reapply a column value when the game assigns an island.
- `Scoreboard.Start` / `Populate` / `OnEnable` — scoreboard content/open sync.
- The removed `SettingSetInterface.OnEnable` hook was a dangerous native detour on a tiny tail-call thunk; page
  attachment now comes from `GenerateElements`.

Settings screen work from `OnEnable` is deferred through `LayoutOnce` (`RequestSettingsSync`) so hierarchy changes do
not run inside the game's enable callback.

## Top bar

`TopBarApi` creates `TopBarButtonHandle` registrations. A button is cloned from the game's own bar button, preserving
native art, sounds and hover behavior.

Attachment flow:

1. resolve the theme icon or caller sprite;
2. find the active top-bar transform (`GameUi.FindActiveBar`);
3. clone the native icon button;
4. apply visibility and interactability;
5. ask the host to reorder mod buttons by `Order` then registration sequence.

The handle keeps the requested icon, tooltip, visibility, interactability and click callback while detached, then
reapplies them on the next attachment. `TopBarApi.Get(id)` returns a live handle from the scope's dictionary.

## Settings

Settings has two native shapes:

- a mod-owned tab (`SettingsTabHandle`);
- a block appended to one of the game's built-in pages (`SettingsBlockHandle`).

Both build rows with `SettingsRowHost` from the game's `SettingEntryInterface` and header prefabs.

### Tabs

`SettingsTabHandle` clones the game's tab title and page prefabs. It removes the cloned page's
`SettingSetInterface` component before using the page as a row container, registers the tab with `TabArea`, builds
the stable rows and removes itself from `TabArea` on detach.

The custom tab is scope-owned but lives in the shared settings hierarchy. The view retains the screen it was attached
to; if the screen changes, the view is disposed and the tab is rebuilt against the new screen.

### Built-in page blocks

A block resolves its page through `SettingsPages.Find(page)`, then:

1. measures the native content inset (`SettingsPageChrome.MeasureContentLeft`);
2. creates a `SettingsRowHost` on the page holder;
3. builds all stable rows in a batch;
4. keeps block rows contiguous with `NativeUiHost.ReorderSettingsBlocks`;
5. applies page chrome when the page has mod rows.

`SettingsPage` covers `Video`, `Audio`, `Input`, `Game` and the match-start `Lobby` settings page.

When the game regenerates a page, `OnPageGenerated` rebuilds the block rows and reapplies chrome. A block remains
stable while its native rows are recreated.

### Settings row types

`SettingsRowHost` builds:

- header (game header prefab);
- toggle (`NativeToggle`);
- slider (`NativeSlider`);
- dropdown (`NativeDropdown`);
- button(s) (`NativeButton`);
- stepper (`-` / value / `+`);
- info paragraph (managed TMP text with `AutoHeightText`).

All values and callbacks live in `SettingsRowHandle`; views are disposable rendering objects. The handle reapplies
initial values when a row is attached.

### Page chrome

`SettingsPageChrome` applies narrow, internal fixes only while a page has at least one mod row:

- a bottom spacer for the close button;
- a transparent raycast target so the game's `ScrollRect` receives the wheel;
- a faster wheel sensitivity;
- viewport clipping above the Close button;
- a shared Close-button shift when the native scrollbar appears;
- a one-time Lobby scroll/mask fit.

The chrome is removed when the last mod block on the page leaves. `SettingsViewport`, `SettingsCloseShift` and
`SettingsPageChrome` keep original values so restoring a page does not permanently alter the game UI.

The wheel target is a library-owned `Image` only when the holder has no existing graphic. Page chrome is not a public
editing API.

## Scoreboard

`ScoreboardColumnHandle` adds a column to every current and future scoreboard row. A cell is cloned from the row's
last native stat cell, stripped of game stat/button logic and filled from the handle's cached values.

### Attachment

- `ScoreboardRow.Start` calls `NativeUiHost.OnScoreboardRowReady`.
- `Scoreboard.Start`, `Populate` and `OnEnable` call `NativeUiHost.OnScoreboardShown`.
- The latter coalesces a `LayoutOnce` sync that rescans rows, refreshes columns and requests a refit.
- `ScoreboardRow.Island` setter re-reads the cached value for that row.
- Scoreboard rows get `ScoreboardRowLifetime`, which removes the row from the host when Unity destroys it.

Cell insertion happens only after the cloned object has been parented, activated and initialized. This prevents a
failed attach from leaving a stale cell entry.

### Layout and trail cells

The game row's last cell is an empty send-button slot. While a mod column occupies it, the slot is collapsed to zero
width. `ScoreboardLayout` keeps a refcounted `TrailState` for each donor:

- `CollapseTrail` captures the original `LayoutElement` fields and rect size and increments the count;
- `ReleaseTrail` decrements it;
- when the last column using that donor is detached, the original layout is restored.

This is why disposing a scoreboard column does not permanently break the game row's send-button slot. `Reset` only
forgets the cached board; trail restore state belongs to cells until they detach.

### Values

Values are keyed by `GameIsland` instance id in `ScoreboardColumnHandle`. `SetValue`, `SetValues` and `ClearValues`
update the cache and existing cells. `PrepareLayout` reapplies text on explicit layout events; there is no per-frame
text polling.

## Rules civilizations

`RulesCivilizationHandle` appends an option to the game's civilization dropdown and a boxed value to the parallel
`values` array. UiKit only draws the option for a civilization number that another system owns; it does not allocate
numbers, answer tech tree lookups or change skins.

- `DetachCore` removes both the dropdown option and the corresponding value, handling reordered/destroyed options
  defensively.
- Entries are keyed by civilization number; a number already shown by another scope is rejected.

`SetName` updates the displayed option without changing the number.
## Lifetime components and registries

Injected MonoBehaviours expose only Unity methods and supported Unity types. Managed-only bindings are kept outside
them in `ConditionalWeakTable` registries:

- `AutoHeightRegistry` — after-measure callback for `AutoHeightText`;
- `ScoreboardRowLifetimeRegistry` — host/row binding;
- `SettingsScreenLifetimeRegistry` — host/settings binding.

This avoids Il2CppInterop warnings for unsupported managed delegate/`object` parameters and lets Unity destruction
clear the binding naturally.

## Pointer and scroll internals

Top-level user behavior is documented in the README. Internally:

- `PointerTracker` subscribes to `InputSystem.onAfterUpdate` only while entries exist;
- `PointerReceiver` forwards Unity `EventTrigger` pointer events;
- `PointerRegistry` holds managed handlers outside the injected component;
- `UiScroll` uses a native `ScrollRect` and a one-shot `ScrollPin` to stay at the top while content grows;
- wheel candidates are prioritized by explicit priority then registration order; a handler can decline the wheel and
  pass it to the next candidate.

## Known constraints

- Hooks are tied to the current Dice Kingdoms build; optional groups degrade independently.
- Native UI is add-only.
- The scoreboard and Settings are only present in their game screens; handles remain stable while waiting.
- Template availability depends on the game scene and `Widgets/Templates` policy.
- `Class::Init signatures have been exhausted` is an external Il2CppInterop startup warning, not a UiKit hook failure.
- Settings chrome warnings and pending-block messages are status diagnostics; real failures are logged as errors.
