using System;
using System.Collections.Generic;
using GameTechTree = DiceKingdoms.Game.TechTree;

namespace DiceModders.UiKit.Native;

/// <summary>
/// Adds civilizations to the game's Rules screen. The library owns the stable ids and the game hooks that make the
/// civilization's tech tree and borrowed biome work.
/// </summary>
public sealed class RulesApi {
    private readonly UiScope _scope;
    private readonly NativeUiHost _host;
    private readonly Dictionary<string, RulesCivilizationHandle> _items = new(StringComparer.Ordinal);

    internal RulesApi(UiScope scope) {
        _scope = scope;
        _host = scope.Runtime.NativeUi;
    }

    private const string ObsoleteMessage =
        "May be removed in a future version. Prefer AddCivilizationEntry and let the system that owns civilization " +
        "numbers (ids, tech trees, skins, network sync) register them, so two systems never allocate ids or patch the same game code.";

    /// <summary>
    /// Adds an entry to the Rules drop-down for a civilization number that another system owns (for example a
    /// mod library that allocates and synchronizes the numbers). The library only draws and removes the entry: it does
    /// not allocate the number, provide a tech tree or change the civilization's skins.
    /// </summary>
    /// <param name="civilizationId">Number above the built-in civilizations (0..4) that nothing else shows here.</param>
    /// <param name="name">Text shown in the drop-down.</param>
    public RulesCivilizationHandle? AddCivilizationEntry(int civilizationId, string? name) {
        if (!_scope.IsAlive) {
            UiLog.OnceError("rules:disposed-scope", "cannot add a civilization entry to a disposed scope");
            return null;
        }
        if (civilizationId <= 4 || string.IsNullOrWhiteSpace(name)) {
            UiLog.OnceError("rules:bad-entry:" + civilizationId,
                "a civilization entry needs a number above the built-in ones (0..4) and a display name");
            return null;
        }

        string key = "entry:" + civilizationId;
        if (_items.TryGetValue(key, out var existing) && !existing.IsDisposed) {
            UiLog.OnceError("rules:duplicate:" + key, $"civilization entry {civilizationId} already exists in this scope");
            return existing;
        }
        _items.Remove(key);

        if (_host.FindCivilizationById(civilizationId) != null) {
            UiLog.OnceError("rules:duplicate-global:" + key,
                $"civilization number {civilizationId} is already used by another Rules entry");
            return null;
        }

        RulesCivilizationHandle? handle = null;
        try {
            handle = new RulesCivilizationHandle(_scope, _host, key, civilizationId, name.Trim(), null, 0,
                value => _items.Remove(value.Key)) { EntryOnly = true };
            if (!SkinPolicy.AllowNative(_scope.Runtime)) {
                handle.MarkUnsupported("TemplatePolicy.ManagedOnly disables native Rules-screen additions");
            }
            _items[key] = handle;
            return _host.Register(handle);
        }
        catch (Exception e) {
            UiGuard.Report("rules.add-entry", e, _scope.Id);
            UiGuard.Run("rules.rollback", () => handle?.Dispose(), _scope.Id);
            _items.Remove(key);
            return null;
        }
    }
    /// <summary>Adds a civilization described by <paramref name="spec"/>.</summary>
    [Obsolete(ObsoleteMessage)]
    public RulesCivilizationHandle? Add(RulesCivilizationSpec? spec)
    {
#pragma warning disable CS0618
        return spec == null ? null : AddCivilization(spec.Key, spec.Name, spec.TechTree, spec.SkinsLike);
#pragma warning restore CS0618
    }

    /// <summary>
    /// Adds a civilization to the Rules drop-down. The key is stable across machines; the library derives the
    /// civilization number from it, so two mods never need to coordinate ids.
    /// </summary>
    [Obsolete(ObsoleteMessage)]
    public RulesCivilizationHandle? AddCivilization(string? key, string? name, Func<GameTechTree?>? techTree = null,
                                                     int skinsLike = 0) {
        if (!_scope.IsAlive) {
            UiLog.OnceError("rules:disposed-scope", "cannot add a civilization to a disposed scope");
            return null;
        }
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name)) {
            UiLog.OnceError("rules:missing-name", "a civilization needs a stable key and a display name");
            return null;
        }

        string stableKey = key.Trim();
        if (_items.TryGetValue(stableKey, out var existing) && !existing.IsDisposed) {
            UiLog.OnceError("rules:duplicate:" + stableKey,
                $"civilization '{stableKey}' already exists in this scope");
            return existing;
        }
        _items.Remove(stableKey);

        if (_host.FindCivilizationByKey(stableKey) is { } other) {
            UiLog.OnceError("rules:duplicate-global:" + stableKey,
                $"civilization '{stableKey}' is already registered by another scope");
            return null;
        }

        int id = _host.AllocateCivilizationId(stableKey);
        RulesCivilizationHandle? handle = null;
        try {
            handle = new RulesCivilizationHandle(_scope, _host, stableKey, id, name.Trim(), techTree, skinsLike,
                value => _items.Remove(value.Key));
            if (!SkinPolicy.AllowNative(_scope.Runtime)) {
                handle.MarkUnsupported("TemplatePolicy.ManagedOnly disables native Rules-screen additions");
            }
            _items[stableKey] = handle;
            return _host.Register(handle);
        }
        catch (Exception e) {
            UiGuard.Report("rules.add", e, _scope.Id);
            UiGuard.Run("rules.rollback", () => handle?.Dispose(), _scope.Id);
            _items.Remove(stableKey);
            return null;
        }
    }
}
