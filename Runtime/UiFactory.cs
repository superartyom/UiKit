using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DiceModders.UiKit;

internal static class UiFactory {
    private static readonly Il2CppReferenceArray<Il2CppSystem.Type> RectTypes = BuildRectTypes();

    private static Il2CppReferenceArray<Il2CppSystem.Type> BuildRectTypes() {
        var types = new Il2CppReferenceArray<Il2CppSystem.Type>(1);
        types[0] = Il2CppType.Of<RectTransform>();
        return types;
    }

    public static RectTransform NewRect(string name, Transform? parent) {
        var go = new GameObject(name, RectTypes);
        var rect = go.GetComponent<RectTransform>();
        if (parent != null) {
            rect.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
        }
        else {
            go.layer = 5;
        }
        return rect;
    }

    public static void Stretch(RectTransform rect) {
        if (rect == null) return;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    public static Image AddImage(RectTransform? parent, string name, Sprite? sprite, Color? color = null, bool raycast = false) {
        var rect = NewRect(name, parent);
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.color = color ?? Color.white;
        image.raycastTarget = raycast;
        return image;
    }

    public static TextMeshProUGUI AddText(RectTransform? parent, string name, string? value, float size,
                                          TextAlignmentOptions align, UiTheme? theme, bool raycast = false) {
        var rect = NewRect(name, parent);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyFont(text, theme);
        text.text = value ?? string.Empty;
        text.fontSize = size;
        text.alignment = align;
        text.color = theme != null ? theme.TextColor : AssetCatalog.DefaultTextColor;
        text.raycastTarget = raycast;
        return text;
    }

    public static void ApplyFont(TMP_Text? text, UiTheme? theme) {
        if (text == null || theme == null) return;

        // Font and material are set together from one checked pair: a material that samples another copy of the font's atlas draws garbage.
        if (!theme.TryGetFontPair(out var font, out var material) || font == null) return;
        text.font = font;
        text.fontSharedMaterial = material != null ? material : font.material;
    }

    public static LayoutElement? Layout(RectTransform? rect) {
        if (rect == null) return null;
        return rect.gameObject.GetComponent<LayoutElement>()
            ?? rect.gameObject.AddComponent<LayoutElement>();
    }

    public static void Destroy(Object obj) {
        if (obj != null) Object.Destroy(obj);
    }

    public static void Destroy(GameObject go) {
        if (go != null) Object.Destroy(go);
    }
}
