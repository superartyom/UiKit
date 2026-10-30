using System;
using DiceModders.UiKit.Native;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UButton = UnityEngine.UI.Button;
using UIImage = UnityEngine.UI.Image;

namespace DiceModders.UiKit.Custom;

/// <summary>
/// A button dressed with the game's own button skin when it is available, or a managed fallback when the template is
/// missing. The public API is identical for both; use <see cref="Interactable"/> and <see cref="Selected"/> to drive it.
/// </summary>
public sealed class UiButton : UiElement {
    private readonly UiTheme _theme;
    private readonly UButton? _button;
    private readonly UIImage? _background;
    private readonly TMP_Text? _label;
    private readonly NativeButton? _native;
    private UIImage? _icon;
    private ClickRelay? _relay;
    private bool _selected = true;
    private string? _tooltip;

    internal UiButton(RectTransform root, UiTheme theme, UButton? button, UIImage? background,
                    TMP_Text? label, UIImage? icon, NativeButton? native = null) : base(root) {
        _theme = theme;
        _button = button;
        _background = background;
        _label = label;
        _icon = icon;
        _native = native;
    }

    internal static UiButton? Create(UiContainer parent, string? text, Action? onClick, string? iconName, bool fixedSize,
                                   string? tooltip = null, Sprite? icon = null) {
        var runtime = parent.Runtime;
        var theme = parent.Theme;
        NativeButton? native = null;

        try {
            if (SkinPolicy.AllowNative(runtime)) {
                var sprite = icon ?? (string.IsNullOrEmpty(iconName) ? null : theme.Icon(iconName));

                if (string.IsNullOrEmpty(iconName) && sprite == null) {
                    native = SkinPolicy.TryNative("button", () =>
                        NativeButton.CreateLabel(runtime.Templates, parent.Root, "DM Button", text, 16f, onClick));
                }
                else if (sprite != null) {
                    native = SkinPolicy.TryNative("button", () =>
                        NativeButton.CreateIcon(runtime.Templates, parent.Root, "DM IconButton", sprite, onClick, null));
                }
                else {
                    native = SkinPolicy.TryNative("button", () =>
                        NativeButton.CreateLabel(runtime.Templates, parent.Root, "DM IconButton", "?", 16f, onClick, null));
                }

                if (native != null) {
                    var result = FromNative(parent, native, text, fixedSize);
                    if (result == null) {
                        native.Destroy();
                        return null;
                    }
                    if (!string.IsNullOrWhiteSpace(tooltip)) result.Tooltip(tooltip);
                    return result;
                }

                if (!SkinPolicy.MissingNative(runtime, "button"))
                    return null;
            }

            return CreateManaged(parent, text, onClick, iconName, fixedSize, icon);
        }
        catch (Exception e) {
            UiGuard.Report("button.create", e);
            if (native != null) UiGuard.Run("button.cleanup", native.Destroy);
            return null;
        }
    }

    private static UiButton? FromNative(UiContainer parent, NativeButton native, string? text, bool fixedSize) {
        var rt = native.Rt;
        if (rt == null) return null;
        var label = native.Text;
        var result = new UiButton(rt, parent.Theme, native.UnityButton, native.Face, label,
                                null, native);

        var layout = UiFactory.Layout(rt);
        if (layout != null) {
            layout.minHeight = layout.preferredHeight = NativeButton.SlicedFit;
            layout.flexibleHeight = 0f;

            if (fixedSize) {
                layout.minWidth = layout.preferredWidth = NativeButton.SlicedFit;
                layout.flexibleWidth = 0f;
                native.SetSize(NativeButton.SlicedFit, NativeButton.SlicedFit);
                native.FitIcon(NativeButton.SlicedFit);
            }
            else if (parent.TextSizedButtons) {
                float preferredWidth = 0f;
                if (label != null &&
                    TmpTextSafety.TryPreferredValues(label, text ?? string.Empty, 0f, 0f, out var preferred)) {
                    preferredWidth = preferred.x;
                }
                float width = Mathf.Max(64f, Mathf.Ceil(preferredWidth) + 28f);
                layout.minWidth = layout.preferredWidth = width;
                layout.flexibleWidth = 0f;
                native.SetSize(width, NativeButton.SlicedFit);
            }
            else {
                layout.minWidth = layout.preferredWidth = 0f;
                layout.flexibleWidth = 1f;
            }
        }

        return result;
    }

    private static UiButton? CreateManaged(UiContainer parent, string? text, Action? onClick, string? iconName,
                                         bool fixedSize, Sprite? iconSprite = null) {
        var theme = parent.Theme;
        var rect = UiFactory.NewRect("DM Button", parent.Root);

        try {
            var background = rect.gameObject.AddComponent<UIImage>();
            var button = rect.gameObject.AddComponent<UButton>();
            button.targetGraphic = background;

            var normal = theme.Button(AssetCatalog.ButtonVariant.Regular, AssetCatalog.ButtonState.Ready);
            var hover = theme.Button(AssetCatalog.ButtonVariant.Regular, AssetCatalog.ButtonState.Hovered);
            var pressed = theme.Button(AssetCatalog.ButtonVariant.Regular, AssetCatalog.ButtonState.Pressed);
            var disabled = theme.Button(AssetCatalog.ButtonVariant.Regular, AssetCatalog.ButtonState.Disabled);

            if (normal != null) {
                background.sprite = normal;
                background.type = UIImage.Type.Sliced;
                background.color = Color.white;

                if (hover != null && pressed != null) {
                    button.transition = Selectable.Transition.SpriteSwap;
                    var state = button.spriteState;
                    state.highlightedSprite = hover;
                    state.pressedSprite = pressed;
                    state.selectedSprite = normal;
                    state.disabledSprite = disabled ?? normal;
                    button.spriteState = state;
                }
            }
            else {
                background.color = new Color(0.20f, 0.17f, 0.13f, 0.95f);
                button.transition = Selectable.Transition.ColorTint;
                var colors = button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1f, 0.96f, 0.88f, 1f);
                colors.pressedColor = new Color(0.82f, 0.76f, 0.66f, 1f);
                colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
                button.colors = colors;
            }

            var label = UiFactory.AddText(rect, "DM Label", text ?? string.Empty, 16f,
                                          TextAlignmentOptions.Center, theme);
            UiFactory.Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(8f, 0f);
            label.rectTransform.offsetMax = new Vector2(-8f, 0f);

            UIImage? icon = null;
            var sprite = iconSprite ?? (string.IsNullOrEmpty(iconName) ? null : theme.Icon(iconName));
            if (sprite != null || !string.IsNullOrEmpty(iconName)) {
                if (sprite != null) {
                    icon = UiFactory.AddImage(rect, "DM Icon", sprite, Color.white, false);
                    icon.type = UIImage.Type.Simple;
                    icon.preserveAspect = true;
                    var iconRect = icon.rectTransform;
                    iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
                    iconRect.pivot = new Vector2(0f, 0.5f);
                    iconRect.sizeDelta = new Vector2(24f, 24f);
                    iconRect.anchoredPosition = new Vector2(8f, 0f);
                    label.rectTransform.offsetMin = new Vector2(36f, 0f);
                }
                else if (string.IsNullOrEmpty(text)) {
                    label.text = "?";
                }
            }

            var layout = UiFactory.Layout(rect);
            if (layout != null) {
                layout.minHeight = layout.preferredHeight = 32f;
                layout.flexibleHeight = 0f;
            }

            if (layout != null) {
                if (fixedSize) {
                    layout.minWidth = layout.preferredWidth = 32f;
                    layout.flexibleWidth = 0f;
                }
                else if (parent.TextSizedButtons) {
                    float preferredWidth = 0f;
                    if (TmpTextSafety.TryPreferredValues(label, text ?? string.Empty, 0f, 0f, out var preferred)) {
                        preferredWidth = preferred.x;
                    }
                    float width = Mathf.Max(32f, preferredWidth + 24f);
                    layout.minWidth = layout.preferredWidth = width;
                    layout.flexibleWidth = 0f;
                }
                else {
                    layout.minWidth = layout.preferredWidth = 0f;
                    layout.flexibleWidth = 1f;
                }
            }

            var result = new UiButton(rect, theme, button, background, label, icon);
            result.OnClick(onClick);
            return result;
        }
        catch (Exception e) {
            UiGuard.Report("button.managed", e);
            UiFactory.Destroy(rect.gameObject);
            return null;
        }
    }

    public UiButton Text(string? value) {
        if (_native != null) UiGuard.Run("button.text", () => _native.SetText(value));
        else if (_label != null) UiGuard.Run("button.text", () => _label.text = value ?? string.Empty);
        return this;
    }

    public UiButton Icon(string? spriteName) => SetIcon(_theme.Icon(spriteName));

    /// <summary>Uses a caller-supplied sprite, for example a texture created at runtime.</summary>
    public UiButton Icon(Sprite? sprite) => SetIcon(sprite);

    private UiButton SetIcon(Sprite? sprite) {
        if (!IsAlive || sprite == null) return this;

        if (_native != null) {
            UiGuard.Run("button.icon", () => _native.SetIcon(sprite));
            return this;
        }

        UiGuard.Run("button.icon", () => {
            if (Root == null) return;
            if (_icon == null) {
                _icon = UiFactory.AddImage(Root, "DM Icon", sprite, Color.white, false);
                _icon.type = UIImage.Type.Simple;
                _icon.preserveAspect = true;
                var iconRect = _icon.rectTransform;
                iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
                iconRect.pivot = new Vector2(0f, 0.5f);
                iconRect.sizeDelta = new Vector2(24f, 24f);
                iconRect.anchoredPosition = new Vector2(8f, 0f);
                if (_label != null) _label.rectTransform.offsetMin = new Vector2(36f, 0f);
            }
            else {
                _icon.sprite = sprite;
                _icon.enabled = true;
            }
        });
        return this;
    }

    public UiButton Tooltip(string? text) {
        _tooltip = text;
        UiGuard.Run("button.tooltip", () => NativeTooltip.Set(Root != null ? Root.gameObject : null, text));
        return this;
    }

    public UiButton Selected(bool selected = true) {
        _selected = selected;
        if (_native != null) UiGuard.Run("button.selected", () => _native.SetSelected(selected));
        else UiGuard.Run("button.selected", ApplySelected);
        return this;
    }

    public UiButton Interactable(bool on = true) {
        if (_native != null) UiGuard.Run("button.interactable", () => _native.SetInteractable(on));
        else if (_button != null) UiGuard.Run("button.interactable", () => _button.interactable = on);
        return this;
    }

    public UiButton OnClick(Action? action) {
        if (!IsAlive) return this;
        if (_native != null) {
            UiGuard.Run("button.click", () => _native.SetClick(action));
            return this;
        }

        if (_button == null || Root == null) return this;
        var root = Root;
        UiGuard.Run("button.click", () => {
            if (_relay == null) _relay = root.gameObject.GetComponent<ClickRelay>() ?? root.gameObject.AddComponent<ClickRelay>();
            ClickRelayRegistry.Bind(_relay, _button, action);
        });
        return this;
    }

    private void ApplySelected() {
        if (_button == null || _background == null) return;

        if (_button.transition == Selectable.Transition.SpriteSwap) {
            _background.color = _selected ? Color.white : new Color(0.55f, 0.55f, 0.55f, 1f);
            return;
        }

        var colors = _button.colors;
        colors.normalColor = _selected ? Color.white : new Color(0.55f, 0.55f, 0.55f, 1f);
        _button.colors = colors;
    }
}
