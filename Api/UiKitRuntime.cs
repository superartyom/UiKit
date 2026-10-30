using System;
using System.Collections.Generic;
using BepInEx.Logging;
using DiceModders.UiKit.Custom;
using DiceModders.UiKit.Native;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace DiceModders.UiKit;

/// <summary>
/// One process-wide runtime shared by every mod: asset cache, theme, layer canvases, scene hooks and the one-time startup
/// readiness point. Scopes are cheap ownership tokens; they never create canvases or scan assets.
/// </summary>
internal sealed class UiKitRuntime {
    private readonly List<UiScope> _scopes = new();
    private readonly List<ReadyRegistration> _readyCallbacks = new();
    private UnityAction<Scene, LoadSceneMode>? _sceneLoaded;
    private UnityAction<Scene>? _sceneUnloaded;
    private DemoController? _demo;
    private int _windowIndex;
    private int _scopeIndex;
    private bool _disposed;
    private bool _uiReady;
    private bool _uiReadyCandidate;

    internal UiKitRuntime(ManualLogSource? log, UiOptions? options) {
        Options = options ?? new UiOptions();
        UiLog.SetLogger(log);

        Assets = new AssetCatalog();
        Templates = new TemplateCatalog();
        Theme = new UiTheme(Assets);
        Layers = new LayerCanvases(this);
        NativeUi = new NativeUiHost();

        RegisterComponents();
        LayoutOnce.Begin();
        LayoutEvents.Activate();
        LayoutEvents.CanvasChanged += OnCanvasChanged;
        NativeHooks.Install(this);
        NativeTooltip.Attach(this);

        try {
            Action<Scene, LoadSceneMode> loaded = OnSceneLoaded;
            Action<Scene> unloaded = OnSceneUnloaded;
            _sceneLoaded = DelegateSupport.ConvertDelegate<UnityAction<Scene, LoadSceneMode>>(loaded);
            _sceneUnloaded = DelegateSupport.ConvertDelegate<UnityAction<Scene>>(unloaded);
            SceneManager.sceneLoaded += _sceneLoaded;
            SceneManager.sceneUnloaded += _sceneUnloaded;
        }
        catch (Exception e) {
            UiLog.Warn("could not subscribe to scene changes: " + e.Message);
        }

        if (Options.EnableDemo) {
            UiGuard.Run("demo.create", () => {
                _demo = new DemoController(this);
                _demo.Request();
                _demo.OnSceneLoaded();
            }, "runtime");

            if (_demo == null) UiLog.Error("the built-in demo could not be created");
        }

        // The lifecycle hooks normally announce the top bar; this is the late-plugin fallback and also performs the
        // second consecutive readiness check across one layout pass.
        UiGuard.Run("startup.ready", TryMarkUiReady, "runtime");
        RequestReadyProbe();

        UiLog.Info("UI kit runtime created");
    }

    internal AssetCatalog Assets { get; }
    internal TemplateCatalog Templates { get; }
    internal UiTheme Theme { get; }
    internal LayerCanvases Layers { get; }
    internal NativeUiHost NativeUi { get; }
    internal UiOptions Options { get; }
    internal bool IsDisposed => _disposed;
    internal bool IsUiReady => _uiReady;

    internal IReadOnlyList<UiScope> Scopes => _scopes;
    internal bool IsDemoVisible => _demo != null && _demo.IsVisible;

    /// <summary>Shows the library's built-in test screens, creating them now if necessary.</summary>
    internal void ShowDemo() {
        if (_disposed) return;
        UiGuard.Run("demo.show", () => {
            _demo ??= new DemoController(this);
            _demo.Show();
        }, "runtime");
    }

    /// <summary>Hides the built-in test screens without disposing them.</summary>
    internal void HideDemo() {
        if (_disposed) return;
        UiGuard.Run("demo.hide", () => _demo?.Hide(), "runtime");
    }

    internal void ToggleDemo() {
        if (_disposed) return;
        UiGuard.Run("demo.toggle", () => {
            _demo ??= new DemoController(this);
            _demo.Toggle();
        }, "runtime");
    }

    /// <summary>Recreates the demo windows and re-scans native templates for their widgets.</summary>
    internal void RebuildDemo() {
        if (_disposed) return;
        UiGuard.Run("demo.rebuild", () => {
            _demo ??= new DemoController(this);
            _demo.Rebuild();
        }, "runtime");
    }

    internal UiScope? CreateScope(string? id) {
        if (_disposed) {
            UiLog.OnceError("runtime:disposed", "cannot create a UiKit scope after the runtime has been disposed");
            return null;
        }

        string scopeId = NormalizeScopeId(id);
        try {
            var scope = new UiScope(this, scopeId);
            _scopes.Add(scope);
            return scope;
        }
        catch (Exception e) {
            UiLog.Error($"could not create UiKit scope '{scopeId}': {e}");
            return null;
        }
    }

    private string NormalizeScopeId(string? id) {
        if (!string.IsNullOrWhiteSpace(id)) return id.Trim();
        return "scope-" + (++_scopeIndex);
    }

    internal void RemoveScope(UiScope scope) {
        if (scope != null) _scopes.Remove(scope);
    }

    internal void RegisterWhenReady(UiScope scope, Action<UiScope> build) {
        if (_disposed || scope == null || build == null) return;
        if (_uiReady) {
            UiGuard.Run("scope.ready", () => build(scope), scope.Id);
            return;
        }
        _readyCallbacks.Add(new ReadyRegistration(scope, build));
    }

    internal void RemoveReady(UiScope scope) {
        if (scope == null) return;
        for (int i = _readyCallbacks.Count - 1; i >= 0; i--) {
            if (ReferenceEquals(_readyCallbacks[i].Scope, scope)) _readyCallbacks.RemoveAt(i);
        }
    }

    /// <summary>
    /// Checks whether the first game UI is usable for content registration. Two consecutive successful checks are
    /// required so a top bar that appears during scene construction is not announced before the screen has settled.
    /// </summary>
    internal void TryMarkUiReady() {
        if (_disposed || _uiReady) return;

        if (!HasUsableGameUi()) {
            _uiReadyCandidate = false;
            return;
        }

        if (!_uiReadyCandidate) {
            _uiReadyCandidate = true;
            return;
        }

        MarkUiReady();
    }

    /// <summary>Schedules one more readiness check after layout; it never repeats by itself.</summary>
    internal void RequestReadyProbe() {
        if (_disposed || _uiReady) return;
        LayoutOnce.Request(TryMarkUiReady);
    }

    private bool HasUsableGameUi() {
        var bar = UiGuard.Func("ready.bar", GameUi.FindActiveBar, "runtime");
        if (bar == null) return false;

        var layer = UiGuard.Func("ready.layers", () => Layers.Get(Options.DefaultLayer), "runtime");
        if (layer == null) return false;

        return UiGuard.Value("ready.layer-refresh", () => {
            Layers.Refresh();
            return true;
        }, false, "runtime");
    }

    private void MarkUiReady() {
        _uiReady = true;
        _uiReadyCandidate = false;
        UiLog.Info("game UI is ready; opening the content registration point");

        UiGuard.Run("ready.native", NativeUi.Refresh, "runtime");

        var callbacks = _readyCallbacks.ToArray();
        _readyCallbacks.Clear();

        int fired = 0;
        for (int i = 0; i < callbacks.Length; i++) {
            var registration = callbacks[i];
            var scope = registration.Scope;
            if (scope == null || !scope.IsAlive) continue;
            UiGuard.Run("scope.ready", () => registration.Build(scope), scope.Id);
            fired++;
        }

        if (fired > 0) UiLog.Info($"content registration point fired for {fired} callback(s)");
    }

    internal int NextWindowIndex() => _windowIndex++;

    private static void RegisterComponents() {
        Register<ClickRelay>();
        Register<SliderRelay>();
        Register<WindowDragHandler>();
        Register<PointerReceiver>();
        Register<AutoHeightText>();
        Register<WidthWatcher>();
        Register<ScrollPin>();
        Register<CanvasLayoutWatcher>();
        Register<DelegateLifetime>();
        Register<ScoreboardRowLifetime>();
        Register<SettingsScreenLifetime>();
        Register<TooltipDriver>();
    }

    private static void Register<T>() where T : class {
        try { ClassInjector.RegisterTypeInIl2Cpp<T>(); }
        catch (Exception e) { UiLog.Warn($"could not register {typeof(T).Name}: {e.Message}"); }
    }

    private void OnCanvasChanged() {
        if (_disposed) return;
        UiGuard.Run("layout.windows", UiWindow.FitAllToScreen, "runtime");
        UiGuard.Run("layout.native", NativeUi.OnCanvasLayoutChanged, "runtime");
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
        if (_disposed) return;
        if (!_uiReady) _uiReadyCandidate = false;

        UiGuard.Run("scene.assets", () => {
            Assets.Invalidate();
            Templates.Invalidate();
        }, "runtime");
        UiGuard.Run("scene.delegates", Il2CppDelegates.Sweep, "runtime");
        UiGuard.Run("scene.layers", Layers.Refresh, "runtime");
        UiGuard.Run("scene.native", NativeUi.Refresh, "runtime");
        UiGuard.Run("scene.demo", () => _demo?.OnSceneLoaded(), "runtime");
        UiGuard.Run("scene.ready", TryMarkUiReady, "runtime");
        RequestReadyProbe();
    }

    private void OnSceneUnloaded(Scene scene) {
        if (_disposed) return;
        if (!_uiReady) _uiReadyCandidate = false;

        UiGuard.Run("scene.assets", () => {
            Assets.Invalidate();
            Templates.Invalidate();
        }, "runtime");
        UiGuard.Run("scene.delegates", Il2CppDelegates.Sweep, "runtime");
        UiGuard.Run("scene.native", NativeUi.Detach, "runtime");
        // An additive/ordered load can deliver sceneLoaded before the old sceneUnloaded. Re-scan once on the next
        // layout pass so native targets that survived the unload are not left detached.
        UiGuard.Run("scene.native.refresh", NativeUi.RequestRefresh, "runtime");
    }

    internal void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _uiReady = false;
        _uiReadyCandidate = false;

        UiGuard.Run("runtime.unsubscribe", () => {
            if (_sceneLoaded != null) SceneManager.sceneLoaded -= _sceneLoaded;
            if (_sceneUnloaded != null) SceneManager.sceneUnloaded -= _sceneUnloaded;
            LayoutEvents.CanvasChanged -= OnCanvasChanged;
            _sceneLoaded = null;
            _sceneUnloaded = null;
        }, "runtime");
        UiGuard.Run("runtime.layout", () => {
            LayoutOnce.Clear();
            LayoutEvents.Clear();
        }, "runtime");

        var demo = _demo;
        _demo = null;
        if (demo != null) UiGuard.Run("demo.dispose", demo.Dispose, "runtime");

        UiGuard.Run("native.hooks", NativeHooks.Uninstall, "runtime");
        UiGuard.Run("native.tooltips", NativeTooltip.Detach, "runtime");
        UiGuard.Run("native.dispose", NativeUi.Dispose, "runtime");

        _readyCallbacks.Clear();
        var scopes = _scopes.ToArray();
        _scopes.Clear();
        for (int i = scopes.Length - 1; i >= 0; i--) {
            UiGuard.Run("scope.dispose", scopes[i].Dispose, scopes[i].Id);
        }

        UiGuard.Run("runtime.pointer", PointerTracker.Clear, "runtime");
        UiGuard.Run("runtime.templates", Templates.Invalidate, "runtime");
        UiGuard.Run("runtime.assets", Assets.Invalidate, "runtime");
        UiGuard.Run("runtime.delegates", Il2CppDelegates.Clear, "runtime");
        UiGuard.Run("runtime.staging", Staging.Reset, "runtime");
        UiGuard.Run("runtime.layers", Layers.Dispose, "runtime");

        UiLog.Info("UI kit runtime disposed");
    }

    private sealed class ReadyRegistration {
        internal ReadyRegistration(UiScope scope, Action<UiScope> build) {
            Scope = scope;
            Build = build;
        }

        internal UiScope Scope { get; }
        internal Action<UiScope> Build { get; }
    }
}
