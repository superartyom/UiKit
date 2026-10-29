using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;

namespace DiceModders.UiKit;

/// <summary>
/// One cache of the game's sprites, materials and fonts for the whole process. Mods never scan assets themselves.
/// Lookups are lazy; a missing or destroyed asset triggers at most one rescan every few seconds, and only when something asks for it.
/// </summary>
internal sealed class AssetCatalog {
    public static readonly Color DefaultTextColor = new(0.969f, 0.940f, 0.893f, 1f);

    private readonly Dictionary<string, Sprite> _sprites = new();
    private readonly Dictionary<string, Material> _materials = new();
    private readonly Dictionary<string, TMP_FontAsset> _fonts = new();

    private bool _scanned;
    private float _nextRescan;
    private TMP_FontAsset? _font;
    private Material? _fontMaterial;
    private float _nextFontDonorScan;

    public void Invalidate() {
        _sprites.Clear();
        _materials.Clear();
        _fonts.Clear();
        _scanned = false;
        _nextRescan = 0f;
        _font = null;
        _fontMaterial = null;
        _nextFontDonorScan = 0f;
    }

    private void Scan() {
        _sprites.Clear();
        _materials.Clear();
        _fonts.Clear();

        foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.From(typeof(Sprite))))
            if (o?.TryCast<Sprite>() is { } s && !string.IsNullOrEmpty(s.name))
                _sprites.TryAdd(s.name, s);

        foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.From(typeof(Material))))
            if (o?.TryCast<Material>() is { } m && !string.IsNullOrEmpty(m.name))
                _materials.TryAdd(m.name, m);

        foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.From(typeof(TMP_FontAsset))))
            if (o?.TryCast<TMP_FontAsset>() is { } f && !string.IsNullOrEmpty(f.name))
                _fonts.TryAdd(f.name, f);

        _scanned = true;
        _nextRescan = Time.realtimeSinceStartup + 3f;
    }

    private void EnsureScanned() {
        if (!_scanned) Scan();
    }

    private T? Lookup<T>(Dictionary<string, T> map, string? name) where T : UnityEngine.Object {
        if (string.IsNullOrEmpty(name)) return null;
        EnsureScanned();
        if (map.TryGetValue(name, out var found) && found != null) return found;

        if (Time.realtimeSinceStartup < _nextRescan) return null;
        Scan();
        return map.TryGetValue(name, out found) && found != null ? found : null;
    }

    public Sprite? Sprite(string? name) => Lookup(_sprites, name);
    public Material? Material(string? name) => Lookup(_materials, name);
    public TMP_FontAsset? FontAsset(string? name) => Lookup(_fonts, name);

    public IEnumerable<string> SpriteNames(string? contains = null) {
        EnsureScanned();
        foreach (var pair in _sprites) {
            if (pair.Value == null) continue;
            if (contains == null || pair.Key.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0)
                yield return pair.Key;
        }
    }

    public TMP_FontAsset? Font => EnsureFontPair() ? _font : null;

    public Material? FontShadowMaterial => EnsureFontPair() ? _fontMaterial : null;

    /// <summary>
    /// The game's main font and its shadow material, always as a pair. The game's font is dynamic (glyphs are added to its atlas at runtime and
    /// more atlas pages can be created) and there can be more than one live copy of it. A material that samples a different atlas than the font
    /// wrote its glyphs into draws garbage, so the pair is checked on every use and resolved again when it no longer matches.
    /// </summary>
    public bool TryGetFontPair(out TMP_FontAsset? font, out Material? material) {
        bool ok = EnsureFontPair();
        font = ok ? _font : null;
        material = ok ? _fontMaterial : null;
        return ok;
    }

    private const string FontName = "Main-Font-Normal";
    private const string FontShadowMaterialName = "Main-Font-Normal-Shadow";

    private bool EnsureFontPair() {
        if (_font != null && _fontMaterial != null && (_fontMaterial == _font.material || Matches(_font, _fontMaterial))) return true;

        // 1. A live text of the game: whatever the game draws right now is a working pair by definition.
        if (Time.realtimeSinceStartup >= _nextFontDonorScan) {
            _nextFontDonorScan = Time.realtimeSinceStartup + 1f;
            if (PairFromLiveText(out var liveFont, out var liveMaterial)) {
                _font = liveFont;
                _fontMaterial = liveMaterial;
                return true;
            }
        }

        // 2. By name: the font asset, and the shadow material that samples that font's atlas.
        var font = _font != null ? _font : FontAsset(FontName);   // Unity null check: a destroyed font is looked up again
        if (font == null) {
            try { font = TMP_Settings.defaultFontAsset; } catch { font = null; }
        }
        if (font == null) { _font = null; _fontMaterial = null; return false; }

        _font = font;
        _fontMaterial = ShadowMaterialFor(font) ?? font.material;   // 3. the font's own material always matches
        return _fontMaterial != null;
    }

    private static bool PairFromLiveText(out TMP_FontAsset? font, out Material? material) {
        font = null;
        material = null;
        try {
            foreach (var text in UnityEngine.Object.FindObjectsOfType<TMP_Text>()) {
                if (text == null || !text.gameObject.activeInHierarchy || Native.Staging.IsOurs(text.transform)) continue;
                var f = text.font;
                var m = text.fontSharedMaterial;
                if (f == null || m == null || f.name != FontName || !m.name.StartsWith(FontShadowMaterialName, StringComparison.Ordinal)) continue;
                if (!Matches(f, m)) continue;
                font = f;
                material = m;
                return true;
            }
        }
        catch {
            // The game's UI may not exist yet; the named lookup is used instead.
        }
        return false;
    }

    private static Material? ShadowMaterialFor(TMP_FontAsset font) {
        try {
            foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.From(typeof(Material))))
                if (o?.TryCast<Material>() is { } m && m.name.StartsWith(FontShadowMaterialName, StringComparison.Ordinal) && Matches(font, m))
                    return m;
        }
        catch {
            // fall back to the font's own material
        }
        return null;
    }

    /// <summary>True when <paramref name="material"/> samples the atlas that <paramref name="font"/> writes its glyphs into.</summary>
    private static bool Matches(TMP_FontAsset font, Material material) {
        try {
            var atlas = font.atlasTexture;
            if (atlas == null) return material == font.material;
            var texture = material.HasProperty(ShaderUtilities.ID_MainTex) ? material.GetTexture(ShaderUtilities.ID_MainTex) : material.mainTexture;
            return texture != null && texture.GetInstanceID() == atlas.GetInstanceID();
        }
        catch {
            return false;
        }
    }

    public Sprite? PanelSprite => Sprite("Background-3") ?? Sprite("Background-4");

    public Sprite? ButtonSprite(ButtonVariant variant, ButtonState state)
        => Sprite($"Button-{(int)variant}-{state}");

    public Sprite? Ornament(OrnamentStyle style) => style switch {
        OrnamentStyle.Border => Sprite("Ornament-1"),
        OrnamentStyle.BorderWithTwoCorners => Sprite("Ornament-2"),
        OrnamentStyle.BorderWithFourCorners => Sprite("Ornament-5"),
        OrnamentStyle.Classic => Sprite("Ornament-3"),
        _ => null,
    };

    internal enum ButtonVariant { Regular = 1, Selected = 2 }
    internal enum ButtonState { Ready, Hovered, Pressed, Disabled }
}
