using System;
using System.Collections.Generic;
using System.Globalization;
using DiceModders.UiKit.Custom;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GameLabel = DiceKingdoms.Interface.Label;
using GameSettingEntry = DiceKingdoms.Meta.SettingEntryInterface;

namespace DiceModders.UiKit.Native;

/// <summary>
/// Builds and owns the native row objects of one settings page. Both a mod-created tab and a mod block inside a built-in
/// tab use the same builder, so all row kinds behave identically everywhere.
/// </summary>
internal sealed class SettingsRowHost {
    private const float LabelInputGap = 40f;
    private const float RowSpacing = 6f;
    private const float InfoRightMargin = 14f;
    private const float ScrollbarReserve = 24f;
    private const float StepButtonWidth = 32f;
    private const float StepValueWidth = 64f;
    private const float DescriptionHeightMinimum = 20f;

    private readonly UiScope _scope;
    private readonly UiKitRuntime _runtime;
    private readonly float _contentLeft;
    private readonly List<SettingsRowView> _views = new();
    private int _batchDepth;

    public SettingsRowHost(UiScope scope, RectTransform holder, GameSettingEntry entryPrefab, GameLabel? headerPrefab,
                           float contentLeft = 0f) {
        _scope = scope;
        _runtime = scope.Runtime;
        Holder = holder;
        EntryPrefab = entryPrefab;
        HeaderPrefab = headerPrefab;
        _contentLeft = Mathf.Max(0f, UiGuard.Finite(contentLeft, 0f));
    }

    public RectTransform Holder { get; }
    public GameSettingEntry EntryPrefab { get; }
    public GameLabel? HeaderPrefab { get; }
    public bool IsAlive => Holder != null && EntryPrefab != null;
    public IReadOnlyList<SettingsRowView> Views => _views;

    /// <summary>Builds a group of rows with one layout rebuild at the start and one at the end.</summary>
    public void BeginBatch() {
        if (_batchDepth++ == 0) RebuildLayout();
    }

    public void EndBatch() {
        if (_batchDepth <= 0) return;
        _batchDepth--;
        if (_batchDepth == 0) RebuildLayout();
    }

    private void RebuildLayout() {
        if (Holder != null) LayoutRebuilder.ForceRebuildLayoutImmediate(Holder);
    }

    public SettingsRowView? Build(SettingsRowHandle row) {
        if (row == null || !IsAlive) return null;

        if (row.Kind == SettingsRowKind.Header) return BuildHeader(row);

        var entryGo = NewEntry(row, row.Kind == SettingsRowKind.Info ? string.Empty : row.Label,
            row.Kind != SettingsRowKind.Info);
        if (entryGo == null) return null;

        RectTransform? content = null;
        try {
            var entry = entryGo.GetComponent<GameSettingEntry>();
            content = entry != null ? entry.contentHolder : null;
            if (content == null) {
                Destroy(entryGo);
                UiLog.OnceError("settings:no-content", "settings entry prefab has no content holder");
                return null;
            }

            var view = new SettingsRowView { Go = entryGo };
            switch (row.Kind) {
                case SettingsRowKind.Toggle:
                    BuildToggle(row, content, view);
                    break;
                case SettingsRowKind.Slider:
                    BuildSlider(row, content, view);
                    break;
                case SettingsRowKind.Dropdown:
                    BuildDropdown(row, content, view);
                    break;
                case SettingsRowKind.Button:
                    BuildButton(row, content, view);
                    break;
                case SettingsRowKind.Buttons:
                    BuildButtons(row, content, view);
                    break;
                case SettingsRowKind.Stepper:
                    BuildStepper(row, content, view);
                    break;
                case SettingsRowKind.Info:
                    BuildInfo(row, entryGo, content, view);
                    break;
            }

            return view;
        }
        catch {
            Destroy(entryGo);
            throw;
        }
    }

    public void Add(SettingsRowView view) {
        if (view != null) _views.Add(view);
    }

    public void Dispose() {
        foreach (var view in _views) {
            try { view.Destroy(); }
            catch (Exception e) { UiLog.Error("settings row cleanup failed: " + e.Message); }
        }
        _views.Clear();
    }

    private SettingsRowView? BuildHeader(SettingsRowHandle row) {
        if (HeaderPrefab == null) {
            UiLog.OnceError("settings:no-header", "settings header template is unavailable");
            return null;
        }

        GameObject? go = null;
        try {
            go = UnityEngine.Object.Instantiate(HeaderPrefab.gameObject, Staging.Root);
            go.name = "DM Settings Header";
            NativeButton.StripForeign(go);
            DestroyLabel(go);
            SetText(go, row.Label);
            go.transform.SetParent(Holder, false);
            go.SetActive(true);
            return new SettingsRowView { Go = go };
        }
        catch {
            if (go != null) Destroy(go);
            throw;
        }
    }

    private GameObject? NewEntry(SettingsRowHandle row, string label, bool addLabelGap) {
        var entryGo = UnityEngine.Object.Instantiate(EntryPrefab.gameObject, Staging.Root);
        try {
            entryGo.name = "DM Settings Row";
            NativeButton.StripForeign(entryGo);
            DestroyLabel(entryGo);
            SetText(entryGo, label);
            if (addLabelGap) AddLabelGap(entryGo);
            entryGo.transform.SetParent(Holder, false);
            entryGo.SetActive(true);
            if (_batchDepth == 0) RebuildLayout();
            return entryGo;
        }
        catch {
            Destroy(entryGo);
            throw;
        }
    }

    private void BuildToggle(SettingsRowHandle row, RectTransform content, SettingsRowView view) {
        var controlRow = FixedRow(content);
        if (controlRow == null) return;

        NativeToggle? toggle = null;
        toggle = SkinPolicy.TryNative("settings toggle", () =>
            NativeToggle.Create(_runtime.Templates, controlRow, row.BoolValue, value => {
                row.BoolValue = value;
                try { row.BoolChanged?.Invoke(value); }
                catch (Exception e) { UiGuard.Report("settings.toggle", e, _scope.Id); }
            }));
        if (toggle == null) {
            UiLog.OnceError("settings:no-toggle", "native toggle template is unavailable");
            throw new InvalidOperationException("native toggle template is unavailable");
        }

        view.Toggle = toggle;
        var toggleRt = toggle.Go != null ? toggle.Go.GetComponent<RectTransform>() : null;
        SetControlSize(toggleRt, 28f, 28f);
        AlignControlLeft(toggleRt);
    }

    private void BuildSlider(SettingsRowHandle row, RectTransform content, SettingsRowView view) {
        var controlRow = FixedRow(content);
        if (controlRow == null) return;

        NativeSlider? slider = null;
        slider = SkinPolicy.TryNative("settings slider", () =>
            NativeSlider.Create(_runtime.Templates, controlRow, row.Min, row.Max, row.FloatValue));
        if (slider == null) {
            UiLog.OnceError("settings:no-slider", "native slider template is unavailable");
            throw new InvalidOperationException("native slider template is unavailable");
        }

        slider.SetFormat(row.Format);
        slider.SetWholeNumbers(row.WholeNumbers);
        slider.SetChanged(value => {
            row.FloatValue = value;
            try { row.FloatChanged?.Invoke(value); }
            catch (Exception e) { UiGuard.Report("settings.slider", e, _scope.Id); }
        });
        view.Slider = slider;

        // Follow the row's real width: a width measured at build time is wrong while the row is not laid out yet,
        // and a slider that starts after the label column then runs over the scrollbar.
        SetControlSize(slider.Rt, 120f, slider.Height);
        StretchControlRight(slider.Rt, slider.Height);
    }

    private void BuildDropdown(SettingsRowHandle row, RectTransform content, SettingsRowView view) {
        var controlRow = FixedRow(content);
        if (controlRow == null) return;

        NativeDropdown? dropdown = null;
        dropdown = SkinPolicy.TryNative("settings dropdown", () =>
            NativeDropdown.Create(_runtime.Templates, controlRow, row.Options, row.Index));
        if (dropdown == null) {
            UiLog.OnceError("settings:no-dropdown", "native dropdown template is unavailable");
            throw new InvalidOperationException("native dropdown template is unavailable");
        }

        dropdown.SetChanged(index => {
            row.Index = index;
            try { row.IntChanged?.Invoke(index); }
            catch (Exception e) { UiGuard.Report("settings.dropdown", e, _scope.Id); }
        });
        view.Dropdown = dropdown;
        SetControlSize(dropdown.Rt, 220f, dropdown.Height);
        AlignControlLeft(dropdown.Rt);
    }

    private void BuildButton(SettingsRowHandle row, RectTransform content, SettingsRowView view) {
        var controlRow = FixedRow(content);
        if (controlRow == null) return;

        NativeButton? button = null;
        button = SkinPolicy.TryNative("settings button", () =>
            NativeButton.CreateLabel(_runtime.Templates, controlRow, "DM Settings Button", row.ButtonText, 16f,
                () => {
                    try { row.Clicked?.Invoke(); }
                    catch (Exception e) { UiGuard.Report("settings.button", e, _scope.Id); }
                }));
        if (button == null) {
            UiLog.OnceError("settings:no-button", "native button template is unavailable");
            throw new InvalidOperationException("native button template is unavailable");
        }

        button.SetSize(110f, NativeButton.SlicedFit);
        AlignControlLeft(button.Rt);
        button.SetInteractable(row.Enabled);
        view.Button = button;
    }

    private void BuildButtons(SettingsRowHandle row, RectTransform content, SettingsRowView view) {
        var controlRow = EnsureControlRow(content);
        if (controlRow == null) return;

        int count = Mathf.Max(1, row.Buttons.Count);
        float share = Mathf.Max(80f, (AvailableWidth(content) - RowSpacing * (count - 1)) / count);
        for (int i = 0; i < row.Buttons.Count; i++) {
            var definition = row.Buttons[i];
            string text = string.IsNullOrWhiteSpace(definition.Text) ? "OK" : definition.Text;
            Action? click = definition.Clicked;
            var button = SkinPolicy.TryNative("settings button", () =>
                NativeButton.CreateLabel(_runtime.Templates, controlRow, "DM Settings Button", text, 16f,
                    () => {
                        try { click?.Invoke(); }
                        catch (Exception e) { UiGuard.Report("settings.button", e, _scope.Id); }
                    }));
            if (button == null) {
                UiLog.OnceError("settings:no-button", "native button template is unavailable");
                continue;
            }

            float natural = Mathf.Max(80f, 14f * text.Length + 24f);
            button.SetSize(Mathf.Min(natural, share), NativeButton.SlicedFit);
            button.SetInteractable(row.Enabled);
            view.Buttons.Add(button);
        }

        if (view.Buttons.Count == 0) {
            UiLog.OnceError("settings:no-button", "native button template is unavailable");
            throw new InvalidOperationException("native button template is unavailable");
        }
    }

    private void BuildStepper(SettingsRowHandle row, RectTransform content, SettingsRowView view) {
        var controlRow = FixedRow(content);
        if (controlRow == null) return;

        NativeButton? minus = null;
        NativeButton? plus = null;
        TMP_Text? value = null;

        minus = SkinPolicy.TryNative("settings stepper", () =>
            NativeButton.CreateLabel(_runtime.Templates, controlRow, "DM Settings Minus", "-", 20f,
                () => Step(row, -1)));
        plus = SkinPolicy.TryNative("settings stepper", () =>
            NativeButton.CreateLabel(_runtime.Templates, controlRow, "DM Settings Plus", "+", 20f,
                () => Step(row, +1)));
        string initial = row.ReadText();
        value = UiFactory.AddText(controlRow, "DM Settings Value", initial, 16f,
            TextAlignmentOptions.Center, _runtime.Theme);

        if (minus == null || plus == null || value == null) {
            UiLog.OnceError("settings:no-stepper", "native stepper controls are unavailable");
            throw new InvalidOperationException("native stepper controls are unavailable");
        }

        minus.SetSize(StepButtonWidth, NativeButton.SlicedFit);
        Place(minus.Rt, _contentLeft);
        view.Buttons.Add(minus);

        Place(value.rectTransform, _contentLeft + StepButtonWidth + RowSpacing, StepValueWidth);

        plus.SetSize(StepButtonWidth, NativeButton.SlicedFit);
        Place(plus.Rt, _contentLeft + StepButtonWidth + StepValueWidth + RowSpacing * 2f);
        view.Buttons.Add(plus);

        view.Text = value;
    }

    private void BuildInfo(SettingsRowHandle row, GameObject entryGo, RectTransform content, SettingsRowView view) {
        var layout = UiFactory.Layout(entryGo.GetComponent<RectTransform>());
        var text = UiFactory.AddText(content, "DM Settings Info", row.ReadText(), 16f,
            TextAlignmentOptions.TopLeft, _runtime.Theme);
        text.enableWordWrapping = true;
        text.margin = new Vector4(0f, 0f, InfoRightMargin, 0f);

        var rect = text.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(_contentLeft, 0f);
        rect.offsetMax = new Vector2(-(ScrollbarReserve + InfoRightMargin), 0f);

        if (layout != null) {
            layout.minHeight = layout.preferredHeight = DescriptionHeightMinimum;
            layout.flexibleHeight = 0f;
            var auto = text.gameObject.AddComponent<AutoHeightText>();
            auto.Bind(text, layout);
            AutoHeightRegistry.SetAfterMeasure(auto, SettingsPageChrome.RequestRefresh);
            auto.SetEnabled(true);
            view.AutoHeight = auto;
        }

        view.Text = text;
    }

    private void Step(SettingsRowHandle row, int delta) {
        try { row.IntChanged?.Invoke(delta); }
        catch (Exception e) { UiGuard.Report("settings.stepper", e, _scope.Id); }
        row.RefreshText();
    }

    private RectTransform? EnsureControlRow(RectTransform content) {
        var row = FixedRow(content);
        if (row == null) return null;

        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = RowSpacing;
        layout.padding = new RectOffset((int)_contentLeft, 0, 0, 0);
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return row;
    }

    private RectTransform? FixedRow(RectTransform content) {
        var row = UiFactory.NewRect("DM Settings Control Row", content);
        row.anchorMin = Vector2.zero;
        row.anchorMax = Vector2.one;
        row.offsetMin = Vector2.zero;
        row.offsetMax = Vector2.zero;

        var layout = UiFactory.Layout(row);
        if (layout == null) return row;
        layout.minHeight = layout.preferredHeight = NativeButton.Height;
        layout.flexibleWidth = 1f;
        return row;
    }

    private static void Place(RectTransform? rt, float x, float? width = null) {
        if (rt == null) return;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.sizeDelta = new Vector2(width ?? StepButtonWidth, NativeButton.Height);
        rt.anchoredPosition = new Vector2(x, 0f);
    }

    private void SetControlSize(RectTransform? rt, float width, float height) {
        if (rt == null) return;
        var layout = UiFactory.Layout(rt);
        if (layout != null) {
            layout.minWidth = layout.preferredWidth = width;
            layout.minHeight = layout.preferredHeight = height;
            layout.flexibleWidth = 0f;
            layout.flexibleHeight = 0f;
        }
        rt.sizeDelta = new Vector2(width, height);
    }

    /// <summary>Stretches a control from the content's left edge to the right edge minus the scrollbar reserve.</summary>
    private void StretchControlRight(RectTransform? rt, float height) {
        if (rt == null) return;
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.offsetMin = new Vector2(_contentLeft, -height * 0.5f);
        rt.offsetMax = new Vector2(-ScrollbarReserve, height * 0.5f);
    }

    private void AlignControlLeft(RectTransform? rt) {
        if (rt == null) return;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(_contentLeft, 0f);
    }

    private float AvailableWidth(RectTransform? content) {
        float width = content != null ? content.rect.width : 0f;
        if (width <= 1f) width = ColumnWidth - _contentLeft;
        return Mathf.Max(80f, width - _contentLeft);
    }

    private static void AddLabelGap(GameObject entryGo) {
        if (entryGo == null) return;

        var row = entryGo.GetComponent<HorizontalLayoutGroup>();
        var labelGo = entryGo.transform.Find("Label");
        var label = labelGo != null ? labelGo.GetComponent<RectTransform>() : null;
        if (row == null || label == null) return;

        float oldSpacing = row.spacing;
        float gap = Mathf.Max(LabelInputGap, oldSpacing);
        float shrink = gap - oldSpacing;
        if (shrink <= 0f || label.sizeDelta.x <= shrink + 80f) return;

        label.sizeDelta = new Vector2(label.sizeDelta.x - shrink, label.sizeDelta.y);
        row.spacing = gap;
    }

    private static void DestroyLabel(GameObject go) {
        if (go == null) return;
        foreach (var label in go.GetComponentsInChildren<GameLabel>(true)) {
            if (label != null) UnityEngine.Object.DestroyImmediate(label);
        }
    }

    private static void SetText(GameObject go, string? text) {
        if (go == null) return;
        var label = go.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.text = text ?? string.Empty;
    }

    private static void Destroy(GameObject go) {
        if (go != null) UnityEngine.Object.Destroy(go);
    }

    private float ColumnWidth {
        get {
            float width = Holder != null ? Holder.rect.width : 0f;
            if (width <= 50f) width = 500f;
            return Mathf.Max(200f, width);
        }
    }
}
