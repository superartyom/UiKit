using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using GameSettingFloat = DiceKingdoms.Meta.SettingInterfaceFloat;

namespace DiceModders.UiKit.Native;

/// <summary>
/// A slider cloned from the game's float setting control (the Frame Rate Limit slider): native track, fill, handle and
/// value text. The setting binding is destroyed before the clone wakes up, so nothing is written to the game's settings.
/// </summary>
internal sealed class NativeSlider {
    private const float ValueWidth = 44f;
    private const float ValueGap = 8f;
    private const float ValuePadding = 10f;

    public RectTransform? Rt { get; private set; }
    public Slider? Slider { get; private set; }
    public TMP_Text? ValueText { get; internal set; }
    public float Height { get; private set; } = 32f;
    public bool IsAlive => Rt != null && Slider != null;
    public float Value => Slider != null ? Slider.value : 0f;

    private Action<float>? _changed;
    private UnityAction<float>? _unity;
    private string _format = "0.##";

    private NativeSlider() { }

    public static NativeSlider? Create(TemplateCatalog? templates, Transform? parent, float min, float max, float value) {
        var prefab = templates?.FloatSetting();
        if (prefab == null) return null;

        var go = UnityEngine.Object.Instantiate(prefab.gameObject, Staging.Root);
        go.name = "DM Slider";
        NativeButton.StripForeign(go);

        var binding = go.GetComponent<GameSettingFloat>();
        var slider = binding != null ? binding.slider : null;
        if (slider == null) slider = go.GetComponentInChildren<Slider>(true);
        TMP_Text? label = binding != null && binding.label != null ? binding.label.visual : null;
        if (label == null) label = go.GetComponentInChildren<TMP_Text>(true);

        if (binding != null) UnityEngine.Object.DestroyImmediate(binding);
        if (slider == null) {
            UnityEngine.Object.Destroy(go);
            return null;
        }
        NativeButton.RepairBaseButtons(go);

        var group = go.GetComponent<HorizontalOrVerticalLayoutGroup>();
        if (group != null) group.enabled = false;

        var rt = go.GetComponent<RectTransform>();
        float height = rt != null ? rt.rect.height : 0f;
        if (height <= 1f && rt != null) height = rt.sizeDelta.y;
        height = Mathf.Clamp(height > 1f ? height : 32f, 24f, 48f);

        go.transform.SetParent(parent, false);
        go.SetActive(true);

        slider.onValueChanged = new Slider.SliderEvent();
        slider.wholeNumbers = false;
        slider.minValue = min;
        slider.maxValue = max > min ? max : min + 1f;
        slider.SetValueWithoutNotify(Mathf.Clamp(value, slider.minValue, slider.maxValue));
        LayoutParts(label, slider);

        var result = new NativeSlider {
            Rt = rt,
            Slider = slider,
            ValueText = label,
            Height = height,
        };
        result.UpdateLabel();
        result.FitValueColumn();
        return result;
    }

    public void SetChanged(Action<float>? changed) {
        _changed = changed;
        if (Slider == null) return;

        if (_unity != null) Slider.onValueChanged.RemoveListener(_unity);

        if (changed == null) {
            _unity = null;
            return;
        }

        Action<float> handler = OnValueChanged;
        _unity = Il2CppDelegates.ToUnityActionFloat(handler);
        if (_unity == null) return;
        Slider.onValueChanged.AddListener(_unity);
        Il2CppDelegates.Hold(Slider.gameObject, new object?[] { handler, _unity });
    }

    public void SetFormat(string? format) {
        _format = string.IsNullOrEmpty(format) ? "0.##" : format;
        FitValueColumn();
        UpdateLabel();
    }

    public void SetWholeNumbers(bool on) {
        if (Slider == null) return;
        Slider.wholeNumbers = on;
        if (on) {
            Slider.minValue = Mathf.Round(Slider.minValue);
            Slider.maxValue = Mathf.Round(Slider.maxValue);
            Slider.SetValueWithoutNotify(Mathf.Round(Slider.value));
            FitValueColumn();
        }
        UpdateLabel();
    }

    public void SetValueSilently(float value) {
        if (Slider == null) return;
        Slider.SetValueWithoutNotify(Mathf.Clamp(value, Slider.minValue, Slider.maxValue));
        UpdateLabel();
    }

    public void SetRange(float min, float max) {
        if (Slider == null) return;
        Slider.minValue = min;
        Slider.maxValue = max > min ? max : min + 1f;
        Slider.SetValueWithoutNotify(Mathf.Clamp(Slider.value, Slider.minValue, Slider.maxValue));
        FitValueColumn();
        UpdateLabel();
    }

    /// <summary>
    /// Widens the value column when the text it shows (the range's ends and middle) is wider than the default column,
    /// so a long value never runs into the track. It never shrinks below the default and is measured when the format
    /// or range changes, not while dragging, so the track does not move under the handle. Sizes are canvas units, so
    /// the result follows the game's canvas scaling at any resolution or aspect ratio.
    /// </summary>
    private void FitValueColumn() {
        var label = ValueText;
        var slider = Slider;
        if (label == null || slider == null) return;

        float width = ValueWidth;
        try {
            float min = slider.minValue, max = slider.maxValue;
            foreach (float sample in new[] { min, max, (min + max) * 0.5f }) {
                string text = sample.ToString(_format, CultureInfo.InvariantCulture);
                width = Mathf.Max(width, label.GetPreferredValues(text).x + ValuePadding);
            }
        }
        catch {
            width = ValueWidth;   // text measuring can fail while TMP is mid-rebuild; keep the default column
        }

        var labelRt = label.rectTransform;
        labelRt.sizeDelta = new Vector2(width, labelRt.sizeDelta.y);

        var sliderRt = slider.GetComponent<RectTransform>();
        if (sliderRt == null) return;
        var offset = sliderRt.offsetMin;
        sliderRt.offsetMin = new Vector2(width + ValueGap, offset.y);
    }

    private void OnValueChanged(float value) {
        UpdateLabel();
        try { _changed?.Invoke(value); }
        catch (Exception e) { UiLog.Error("native slider: " + e); }
        NativeButton.Deselect();
    }

    /// <summary>
    /// The donor prefab overlays its value label on the track. Lay the label out as a fixed column to the left of the
    /// track instead, so the value reads before the slider and long values do not cover the handle.
    /// </summary>
    private static void LayoutParts(TMP_Text? label, Slider? slider) {
        if (label != null) {
            var labelRt = label.rectTransform;
            labelRt.anchorMin = labelRt.anchorMax = new Vector2(0f, 0.5f);
            labelRt.pivot = new Vector2(0f, 0.5f);
            labelRt.anchoredPosition = Vector2.zero;
            labelRt.sizeDelta = new Vector2(ValueWidth, 32f);
            label.alignment = TextAlignmentOptions.Left;
        }

        var sliderRt = slider != null ? slider.GetComponent<RectTransform>() : null;
        if (sliderRt == null) return;

        float height = sliderRt.rect.height;
        if (height <= 1f) height = sliderRt.sizeDelta.y;
        height = Mathf.Clamp(height > 1f ? height : 20f, 16f, 32f);

        sliderRt.anchorMin = new Vector2(0f, 0.5f);
        sliderRt.anchorMax = new Vector2(1f, 0.5f);
        sliderRt.pivot = new Vector2(0.5f, 0.5f);
        sliderRt.offsetMin = new Vector2(label != null ? ValueWidth + ValueGap : 0f, -height * 0.5f);
        sliderRt.offsetMax = new Vector2(0f, height * 0.5f);
    }

    private void UpdateLabel() {
        if (ValueText == null || Slider == null) return;

        try { ValueText.text = Slider.value.ToString(_format, CultureInfo.InvariantCulture); }
        catch {
            _format = "0.##";
            ValueText.text = Slider.value.ToString(_format, CultureInfo.InvariantCulture);
        }
    }
}
