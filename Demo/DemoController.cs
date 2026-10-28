using System;
using System.Collections.Generic;
using DiceModders.UiKit.Custom;
using DiceModders.UiKit.Native;
using Il2CppInterop.Runtime;
using UnityEngine;
using GameCivilization = DiceKingdoms.Interface.Civilization;
using GameIsland = DiceKingdoms.Game.Island;
using GameTechnologies = DiceKingdoms.Game.Technologies;
using GameTechTree = DiceKingdoms.Game.TechTree;

namespace DiceModders.UiKit;

/// <summary>
/// The library's own test screens. They are created only when the plugin config enables the demo or a mod calls
/// <see cref="UiKit.ShowDemo"/>, and they use the same public API as any other mod.
/// </summary>
internal sealed class DemoController {
    private const string DemoScopeId = "dicemodders.uikit.demo";

    private readonly UiKitRuntime _runtime;
    private readonly List<UiWindow> _windows = new();
    private UiScope? _scope;
    private UiWindow? _main;
    private ScoreboardColumnHandle? _scoreboard;
    private bool _requested;
    private bool _visible;
    private bool _built;

    public DemoController(UiKitRuntime runtime) => _runtime = runtime;

    public bool IsVisible => _visible && _built && _main != null && _main.IsAlive;

    /// <summary>Marks the demo for creation on the next scene load.</summary>
    public void Request() => _requested = true;

    public void Show() {
        _requested = true;
        if (!_built || _scope == null || _main == null || !_main.IsAlive) {
            DestroyScope();
            Build();
            return;
        }
        SetVisible(true);
    }

    public void Hide() {
        _requested = false;
        SetVisible(false);
    }

    public void Toggle() {
        if (IsVisible) Hide();
        else Show();
    }

    /// <summary>Disposes the demo scope and builds it again. Useful after entering a scene with the game's native UI loaded.</summary>
    public void Rebuild() {
        _requested = true;
        DestroyScope();
        Build();
    }

    public void OnSceneLoaded() {
        if (!_requested) return;

        try {
            if (GameUi.FindActiveBar() == null) {
                UiLog.Info("demo is enabled and will be created after a scene with the game UI loads");
                return;
            }

            // The canvas found during sceneLoaded can still be the previous scene's. Now that the game's bar exists,
            // re-read its render mode and sorting orders before building or keeping the demo on screen.
            _runtime.Layers.Refresh();
            if (_built && _main != null && _main.IsAlive) {
                RefreshDemoScoreboard();
                return;
            }
            DestroyScope();
            Build();
        }
        catch (Exception e) {
            UiLog.Error("demo scene hook failed: " + e);
        }
    }

    public void Dispose() {
        _requested = false;
        DestroyScope();
    }

    private void Build() {
        if (_built) return;

        try {
            _runtime.Layers.Refresh();
            var scope = _runtime.CreateScope(DemoScopeId);
            if (scope == null) {
                UiLog.Error("demo could not create its UiKit scope");
                return;
            }

            _scope = scope;
            _built = true;
            _visible = true;
            _windows.Clear();

            BuildMain();
            scope.WhenReady(ui => {
                UiLog.Info("demo: the startup registration point fired");
                ui.Native.TopBar.AddIconButton("hammer", "UiKit demo", Toggle);

                bool interactable = true;
                TopBarButtonHandle? toggleInteractable = null;
                toggleInteractable = ui.Native.TopBar.Add(new TopBarButtonSpec {
                    Id = "uikit.demo.interactable",
                    IconName = "hammer",
                    Tooltip = "UiKit order 5; click toggles interactable",
                    Order = 5,
                    OnClick = () => {
                        interactable = !interactable;
                        toggleInteractable?.SetInteractable(interactable);
                    },
                });
                ui.Native.TopBar.Add(new TopBarButtonSpec {
                    Id = "uikit.demo.order",
                    IconName = "hammer",
                    Tooltip = "UiKit order 15",
                    Order = 15,
                    OnClick = Toggle,
                });
                if (ui.Native.TopBar.Get("uikit.demo.interactable") == null)
                    UiLog.Warn("demo: TopBar.Get did not return the ordered button");
            });
            BuildDemoSettings();
            BuildNativeDemo(scope);
            BuildProbe(Layer.Game, "This window is on Layer.Game. It should sit in the HUD band, below the game's popups.",
                       new Vector2(700f, 80f), OrnamentStyle.BorderWithTwoCorners);
            BuildProbe(Layer.BehindGame, "This window is on Layer.BehindGame. The game's own UI should draw over it.",
                       new Vector2(760f, 300f), OrnamentStyle.None);

            UiLog.Info("demo windows created; use Rebuild inside the demo after the game's UI has loaded to re-scan native skins");
        }
        catch (Exception e) {
            UiLog.Error("demo build failed: " + e);
            DestroyScope();
        }
    }

    private void BuildMain() {
        var scope = _scope;
        if (scope == null) return;
        UiText? status = null;

        _main = scope.Custom.Window(w => {
            w.Title("UiKit demo")
             .Size(620f, 640f)
             .Position(80f, 80f)
             .Draggable()
             .Fullscreenable()
             .FullscreenButton()
             .Closable()
             .Ornament(OrnamentStyle.Border);
            w.OnClose(Close);

            w.Body.Scroll(body => {
                body.Paragraph(
                    "This is the library's built-in smoke test. It uses the same public API as a mod. " +
                    "The widgets below are native clones when their game templates are available, otherwise managed fallbacks. " +
                    "Use Rebuild after entering a match to re-scan the native templates.");
                body.Text($"Template policy: {_runtime.Options.Templates}");

                status = body.Text("Ready.");
                status?.Color(new Color(0.95f, 0.83f, 0.55f, 1f));

                body.Separator(3f);
                body.Text("Buttons");
                var buttonRow = body.Row(row => {
                    row.Button("Click me", () => { if (status != null) status.Value = "Text button clicked."; });
                    var disabled = row.Button("Disabled");
                    disabled?.Interactable(false);
                    row.IconButton("hammer", () => { if (status != null) status.Value = "Icon button clicked."; }, "UiKit demo icon button");

                    var hammer = _runtime.Theme.Icon("hammer");
                    if (hammer != null) {
                        row.IconButton(hammer, () => { if (status != null) status.Value = "Sprite icon button clicked."; },
                            "UiKit demo sprite icon");
                        row.Button("Sprite icon", () => { if (status != null) status.Value = "Button sprite icon clicked."; })
                           ?.Icon(hammer);
                    }
                });
                buttonRow?.Spacing(10f, 0f);

                body.Text("Toggle, slider and dropdown");
                body.Toggle("Toggle me", true, value => { if (status != null) status.Value = "Toggle: " + value; });
                var slider = body.Slider(0f, 100f, 50f, value => { if (status != null) status.Value = "Slider: " + value.ToString("0"); });
                slider?.Format("0");
                body.Dropdown(new[] { "First option", "Second option", "Third option" }, 0,
                              index => { if (status != null) status.Value = "Dropdown index: " + index; });

                body.Text("Grid");
                body.Grid(3, 90f, 32f, grid => {
                    for (int i = 0; i < 6; i++) {
                        int number = i;
                        grid.Button(number.ToString(), () => { if (status != null) status.Value = "Grid button " + number; });
                    }
                }).Height(72f);

                body.Text("Flexible grid");
                var flexible = body.Grid(grid => {
                    for (int i = 0; i < 4; i++) {
                        int number = i;
                        grid.Button("Auto " + number, () => { if (status != null) status.Value = "Flexible grid button " + number; });
                    }
                });
                flexible?.Height(48f);
                flexible?.Spacing(10f, 4f);

                body.Text("Image pointer Move and wheel");
                var picture = body.Image(Texture2D.whiteTexture);
                picture?.Height(90f).FillWidth();
                picture?.Tint(new Color(0.35f, 0.55f, 0.9f, 1f)).UvRect(new Rect(0f, 0f, 1f, 1f));
                picture?.OnPointer(e => {
                    if (status != null && e.Kind == UiPointerKind.Move)
                        status.Value = $"Image Move uv=({e.Uv.x:0.00}, {e.Uv.y:0.00}) size={picture?.Size.x:0}x{picture?.Size.y:0}";
                });
                picture?.OnScroll(notches => {
                    if (status != null) status.Value = "Image wheel: " + notches.ToString("0.##");
                });

                body.Text("Fixed and weighted layout");
                body.Row(row => {
                    row.Column(fixedWidth => {
                        fixedWidth.Text("Fixed 120");
                    })?.FixedW(120f);

                    row.Column(one => {
                        one.Text("Weight 1");
                    })?.WeightW(1f);

                    row.Column(two => {
                        two.Text("Weight 2");
                    })?.WeightW(2f);
                })?.FixedH(48f);

                body.Text("Window ornament");
                body.Row(row => {
                    row.Button("Border", () => w.Ornament(OrnamentStyle.Border));
                    row.Button("Corners", () => w.Ornament(OrnamentStyle.BorderWithFourCorners));
                    row.Button("Classic", () => w.Ornament(OrnamentStyle.Classic));
                    row.Button("None", () => w.Ornament(OrnamentStyle.None));
                });

                body.Separator();
                body.Button("Rebuild (re-scan native skins)", Rebuild);
                body.Button("Hide demo", Hide);

                body.Separator();
                body.Text("Scroll content");
                for (int i = 1; i <= 30; i++) body.Text("Scroll row " + i);
            }).FillHeight();
        });

        if (_main != null) _windows.Add(_main);
    }

    private void BuildDemoSettings() {
        var scope = _scope;
        if (scope == null) return;
        scope.Native.Settings.AddTab("uikit.demo", "UiKit", tab => {
            tab.AddHeader("UiKit native settings");
            tab.AddToggle("Demo toggle", true, value => UiLog.Info("demo settings toggle: " + value));
            tab.AddSlider("Demo slider", 0f, 100f, 50f, value => UiLog.Info("demo settings slider: " + value.ToString("0")), "0");
            tab.AddDropdown("Demo dropdown", new[] { "First", "Second", "Third" }, 0,
                            index => UiLog.Info("demo settings dropdown: " + index));
            tab.AddButton("Demo button", "Click", () => UiLog.Info("demo settings button clicked"));
            tab.AddInfo("Info rows wrap and can be updated through SetText.");
            tab.AddStepper("Stepper", () => "3", delta => UiLog.Info("demo settings step: " + delta));
            tab.AddButtons("Buttons", ("One", () => UiLog.Info("demo settings buttons: one")),
                ("Two", () => UiLog.Info("demo settings buttons: two")));
        });

        int gameCount = 3;
        SettingsRowHandle? gameInfo = null;
        scope.Native.Settings.AddBlock(new SettingsBlockSpec {
            Id = "uikit.demo.builtin.game",
            Page = SettingsPage.Game,
            Title = "UiKit demo block (Game)",
            Order = 10,
            Build = block => {
                gameInfo = block.AddInfo("Stable Info row on the game's own Game page.");
                block.AddToggle("Demo toggle", true,
                    value => gameInfo?.SetText("Game-page toggle: " + value));
                block.AddSlider("Demo slider", 0f, 10f, 4f,
                    value => gameInfo?.SetText("Game-page slider: " + value.ToString("0")), "0", wholeNumbers: true);
                block.AddDropdown("Demo dropdown", new[] { "First", "Second", "Third" }, 0,
                    index => gameInfo?.SetText("Game-page dropdown: " + index));
                block.AddStepper("Demo counter", () => gameCount.ToString(), delta => {
                    gameCount += delta;
                    gameInfo?.SetText("Game-page counter: " + gameCount);
                });
                block.AddButtons("Demo buttons",
                    ("Reset", () => {
                        gameCount = 3;
                        gameInfo?.SetText("Game-page counter reset.");
                    }),
                    ("Notify", () => gameInfo?.SetText("Game-page button clicked.")));
            },
        });

        int lobbyCount = 1;
        SettingsRowHandle? lobbyInfo = null;
        scope.Native.Settings.AddBlock(new SettingsBlockSpec {
            Id = "uikit.demo.builtin.lobby",
            Page = SettingsPage.Lobby,
            Title = "UiKit demo block (Lobby)",
            Order = 10,
            Build = block => {
                lobbyInfo = block.AddInfo("This block is appended to the match-start settings box.");
                block.AddToggle("Lobby toggle", true,
                    value => lobbyInfo?.SetText("Lobby toggle: " + value));
                block.AddStepper("Lobby counter", () => lobbyCount.ToString(), delta => {
                    lobbyCount += delta;
                    lobbyInfo?.SetText("Lobby counter: " + lobbyCount);
                });
                block.AddButtons("Lobby buttons",
                    ("Reset", () => {
                        lobbyCount = 1;
                        lobbyInfo?.SetText("Lobby counter reset.");
                    }));
            },
        });
    }

    private void BuildNativeDemo(UiScope scope) {
        _scoreboard = scope.Native.Scoreboard.AddColumn("uikit.demo.score", "UiKit demo column");
        RefreshDemoScoreboard();

        var tree = FindDemoTree();
        if (tree == null) {
            UiLog.Info("demo: no game tech tree is available, the Rules civilization demo is skipped");
            return;
        }

        #pragma warning disable CS0618 // the demo shows the standalone path
scope.Native.Rules.AddCivilization("uikit.demo.civ", "UiKit civilization", () => tree, 0);
#pragma warning restore CS0618
    }

    private void RefreshDemoScoreboard() {
        var column = _scoreboard;
        if (column == null || column.IsDisposed) return;

        var values = new Dictionary<GameIsland, string>();
        try {
            foreach (var island in UnityEngine.Object.FindObjectsOfType<GameIsland>()) {
                if (island == null || Staging.IsOurs(island.transform)) continue;
                values[island] = (Math.Abs(island.GetInstanceID()) % 100).ToString();
            }
        }
        catch (Exception e) {
            UiLog.OnceError("demo:scoreboard", "demo scoreboard values failed: " + e);
        }
        column.SetValues(values);
    }

    private static GameTechTree? FindDemoTree() {
        try {
            foreach (var candidate in UnityEngine.Resources.FindObjectsOfTypeAll(Il2CppType.Of<GameTechnologies>())) {
                var technologies = candidate?.TryCast<GameTechnologies>();
                if (technologies == null) continue;
                return technologies.TechTree(GameCivilization.EUROPE);
            }
        }
        catch (Exception e) {
            UiLog.OnceError("demo:tech-tree", "could not read a game tech tree: " + e);
        }
        return null;
    }

    private void BuildProbe(Layer layer, string text, Vector2 position, OrnamentStyle ornament) {
        var scope = _scope;
        if (scope == null) return;
        var probe = scope.Custom.Window(w => {
            w.Title("Layer." + layer)
             .Size(340f, 240f)
             .Position(position.x, position.y)
             .Layer(layer)
             .Draggable()
             .Closable()
             .Ornament(ornament);

            w.Body.Paragraph(text);
            w.Body.Button("Bring to front", w.BringToFront);
        });

        if (probe != null) _windows.Add(probe);
    }

    private void Close() {
        _requested = false;
        DestroyScope();
    }

    private void SetVisible(bool visible) {
        _visible = visible;
        foreach (var window in _windows) {
            if (window == null || !window.IsAlive) continue;
            if (visible) window.Show();
            else window.Hide();
        }
    }

    private void DestroyScope() {
        _built = false;
        _visible = false;
        _main = null;
        _scoreboard = null;
        _windows.Clear();

        var scope = _scope;
        _scope = null;
        if (scope == null) return;

        try { scope.Dispose(); }
        catch (Exception e) { UiLog.Error("demo scope dispose failed: " + e); }
    }
}
