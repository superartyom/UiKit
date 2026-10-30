using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using GameHasToolTip = DiceKingdoms.Interface.HasToolTip;
using GameToolTip = DiceKingdoms.Interface.ToolTip;
using GameToolTips = DiceKingdoms.Interface.ToolTips;
using Object = UnityEngine.Object;

namespace DiceModders.UiKit.Native;

/// <summary>
/// Tooltips for UiKit widgets. They use UiKit's own copy of the game's tooltip (same frame, font, icons, delay and placement code), kept on UiKit's
/// top layer above every window. The game's tooltip cannot be used for widgets in UiKit windows: under HDRP the canvases are drawn by their distance
/// from the camera, and the game's tooltip lies further away than UiKit's top layer, so no sorting order can bring it in front.
/// The game's own tooltip system is not touched.
/// <para>
/// Hover is found by asking the EventSystem what is under the mouse (a raycast when the mouse moves, and a few times a second otherwise). The widgets
/// get no event components, so scrolling and dragging pass through them exactly as before.
/// </para>
/// </summary>
internal static class NativeTooltip {
    private const float Delay = 0.4f;            // the game's ToolTips.delay
    private const float ProbeInterval = 0.2f;    // re-check while the mouse rests: windows may open, close or move under it
    private const int OrderAboveWindows = 10000; // windows sort at layer order + their own order

    private sealed class Target {
        public GameObject? Go;
        public string Text = string.Empty;
    }

    private static readonly Dictionary<int, Target> Targets = new();
    private static UiKitRuntime? _runtime;

    private static GameObject? _holder;
    private static Canvas? _holderCanvas;
    private static GameToolTip? _tip;
    private static bool _noTemplateLogged;

    private static PointerEventData? _pointer;
    private static EventSystem? _pointerSystem;
    private static Il2CppSystem.Collections.Generic.List<RaycastResult>? _hits;

    private static int _hoverId;
    private static float _hoverSince;
    private static int _shownId;
    private static Vector2 _lastMouse;
    private static bool _hasLastMouse;
    private static float _nextProbe;

    public static void Attach(UiKitRuntime runtime) => _runtime = runtime;

    public static void Detach() {
        Targets.Clear();
        Hide();
        _hoverId = 0;
        if (_holder != null) Object.Destroy(_holder);
        _holder = null;
        _holderCanvas = null;
        _tip = null;
        _pointer = null;
        _pointerSystem = null;
        _hits = null;
        _runtime = null;
    }

    /// <summary>Shows <paramref name="text"/> when the pointer rests on <paramref name="go"/> (or anything inside it); null or empty removes it.</summary>
    public static void Set(GameObject? go, string? text) {
        if (go == null) return;
        int id = go.GetInstanceID();

        if (string.IsNullOrWhiteSpace(text)) {
            Targets.Remove(id);
            if (_shownId == id) Hide();
            return;
        }

        // A cloned game control may bring the game's own tooltip, which would show a second copy behind the window.
        var game = go.GetComponent<GameHasToolTip>();
        if (game != null) Object.DestroyImmediate(game);

        Sweep();
        Targets[id] = new Target { Go = go, Text = text! };
        EnsureHoverable(go);
        EnsureHolder();
        if (_shownId == id && _tip != null) _tip.Text = GameTextFactory.Plain(text);
    }

    /// <summary>Called every frame by <see cref="TooltipDriver"/>.</summary>
    internal static void Tick() {
        if (Targets.Count == 0) {
            if (_shownId != 0) Hide();
            _hoverId = 0;
            return;
        }

        var mouse = Mouse.current;
        if (mouse == null) return;
        Vector2 position = mouse.position.ReadValue();
        float now = Time.unscaledTime;

        bool moved = !_hasLastMouse || position != _lastMouse;
        if (moved || now >= _nextProbe) {
            _lastMouse = position;
            _hasLastMouse = true;
            _nextProbe = now + ProbeInterval;

            int hit = Probe(position);
            if (hit != _hoverId) {
                _hoverId = hit;
                _hoverSince = now;
                if (_shownId != 0) Hide();
            }
        }

        if (_hoverId != 0 && _shownId == 0 && now - _hoverSince >= Delay) Show(_hoverId);
        if (_shownId != 0 && _tip != null) _tip.SetScreenPosition(new Unity.Mathematics.float2(position.x, position.y));
    }

    private static int Probe(Vector2 position) {
        var system = EventSystem.current;
        if (system == null) return 0;

        if (_pointer == null || _pointerSystem != system) {
            _pointer = new PointerEventData(system);
            _pointerSystem = system;
        }
        _hits ??= new Il2CppSystem.Collections.Generic.List<RaycastResult>();
        _hits.Clear();
        _pointer.position = position;
        system.RaycastAll(_pointer, _hits);
        if (_hits.Count == 0) return 0;

        // Only the topmost hit counts: a widget covered by another window shows nothing.
        var top = _hits[0].gameObject;
        for (var t = top != null ? top.transform : null; t != null; t = t.parent) {
            int id = t.gameObject.GetInstanceID();
            if (Targets.TryGetValue(id, out var target)) {
                if (target.Go != null) return id;
                Targets.Remove(id);
            }
        }
        return 0;
    }

    private static void Show(int id) {
        if (!Targets.TryGetValue(id, out var target) || target.Go == null) return;
        var tip = EnsureTip();
        if (tip == null || _holder == null) return;

        FitHolder();
        _holder.transform.SetAsLastSibling();
        tip.Text = GameTextFactory.Plain(target.Text);
        if (!tip.gameObject.activeSelf) tip.gameObject.SetActive(true);   // first activation runs ToolTip.Awake under our holder
        _shownId = id;
    }

    private static void Hide() {
        _shownId = 0;
        if (_tip != null && _tip.gameObject.activeSelf) _tip.gameObject.SetActive(false);
    }

    private static void Sweep() {
        List<int>? dead = null;
        foreach (var pair in Targets)
            if (pair.Value.Go == null) (dead ??= new List<int>()).Add(pair.Key);
        if (dead == null) return;
        foreach (var id in dead) Targets.Remove(id);
    }

    /// <summary>
    /// The holder sits on UiKit's top layer with its own canvas sorted above every window there. ToolTip.Awake takes this canvas (the nearest one)
    /// and its parent's size for its placement maths, which reads the canvas <c>sizeDelta</c>; so the holder is centred with an explicit size equal
    /// to the screen canvas, not stretched (a stretched rect has a zero sizeDelta and the tooltip would land in a corner).
    /// </summary>
    private static void EnsureHolder() {
        if (_holder != null) return;
        var above = _runtime?.Layers.Get(Layer.AboveGame);
        if (above == null) return;

        var go = new GameObject("DM Tooltips");
        go.layer = above.gameObject.layer;
        var rect = go.AddComponent<RectTransform>();
        rect.SetParent(above.transform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one;

        var group = go.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        _holderCanvas = go.AddComponent<Canvas>();
        go.AddComponent<TooltipDriver>();
        _holder = go;
        FitHolder();
    }

    private static void FitHolder() {
        if (_holder == null || _holderCanvas == null) return;
        var root = _holderCanvas.rootCanvas;
        var rootRect = root != null ? root.GetComponent<RectTransform>() : null;
        var rect = _holder.GetComponent<RectTransform>();
        if (rect != null && rootRect != null && rect.sizeDelta != rootRect.sizeDelta) rect.sizeDelta = rootRect.sizeDelta;

        int order = Math.Min((_runtime?.Layers.OrderFor(Layer.AboveGame) ?? 0) + OrderAboveWindows, short.MaxValue);
        if (!_holderCanvas.overrideSorting) _holderCanvas.overrideSorting = true;
        if (_holderCanvas.sortingOrder != order) _holderCanvas.sortingOrder = order;
    }

    private static GameToolTip? EnsureTip() {
        if (_tip != null) return _tip;
        EnsureHolder();
        if (_holder == null) return null;

        var template = FindTemplate();
        if (template == null) {
            if (!_noTemplateLogged) {
                _noTemplateLogged = true;
                UiLog.Warn("tooltip: the game's tooltip was not found yet; UiKit tooltips appear once a game screen is loaded");
            }
            return null;
        }

        // Copied under the inactive staging parent so nothing on it wakes up before it is cleaned and moved under the holder.
        var go = Object.Instantiate(template.gameObject, Staging.Root);
        go.name = "DM Tooltip";
        go.SetActive(false);
        Clean(go);
        go.transform.SetParent(_holder.transform, false);

        _tip = go.GetComponent<GameToolTip>();
        if (_tip == null) Object.Destroy(go);
        _noTemplateLogged = false;
        return _tip;
    }

    private static GameToolTip? FindTemplate() {
        var tips = Object.FindObjectOfType<GameToolTips>();
        if (tips != null && tips.toolTip != null) return tips.toolTip;

        // The game's tooltip starts switched off in the scene, and FindObjectOfType skips inactive objects.
        foreach (var t in Resources.FindObjectsOfTypeAll<GameToolTip>()) {
            if (t == null || !t.gameObject.scene.IsValid()) continue;
            if (_holder != null && t.transform.IsChildOf(_holder.transform)) continue;
            return t;
        }
        return null;
    }

    /// <summary>The copy must never catch the pointer (it appears under the mouse) and nothing may re-localize its text.</summary>
    private static void Clean(GameObject go) {
        foreach (var graphic in go.GetComponentsInChildren<Graphic>(true))
            if (graphic != null) graphic.raycastTarget = false;

        foreach (var component in go.GetComponentsInChildren<Component>(true)) {
            if (component == null) continue;
            var name = component.GetIl2CppType().Name;
            if (name.Contains("Localize") || name == nameof(GameHasToolTip)) Object.DestroyImmediate(component);
        }
    }

    /// <summary>The raycast only finds objects with a raycast target; plain texts and images are not by default.</summary>
    private static void EnsureHoverable(GameObject go) {
        foreach (var graphic in go.GetComponentsInChildren<Graphic>(true))
            if (graphic != null && graphic.raycastTarget) return;
        var first = go.GetComponentInChildren<Graphic>(true);
        if (first != null) first.raycastTarget = true;
    }
}

/// <summary>Runs <see cref="NativeTooltip.Tick"/> once per frame; lives on the tooltip holder.</summary>
internal sealed class TooltipDriver : MonoBehaviour {
    public TooltipDriver(IntPtr ptr) : base(ptr) { }

    private bool _failed;

    private void Update() {
        try { NativeTooltip.Tick(); }
        catch (Exception e) {
            if (_failed) return;
            _failed = true;
            UiLog.Warn("tooltip update failed: " + e);
        }
    }
}
