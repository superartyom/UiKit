using System;
using System.Collections.Generic;

namespace DiceModders.UiKit.Native;

/// <summary>
/// Adds entries to the game's Rules civilization drop-down. The library only draws and removes the entry: the system
/// that owns a civilization (its number, tech tree, skins and network sync) registers it elsewhere and passes the
/// number here.
/// </summary>
public sealed class RulesApi {
    private readonly UiScope _scope;
    private readonly NativeUiHost _host;
    private readonly Dictionary<int, RulesCivilizationHandle> _items = new();

    internal RulesApi(UiScope scope) {
        _scope = scope;
        _host = scope.Runtime.NativeUi;
    }

    /// <summary>
    /// Adds an option to the Rules drop-down for a civilization number that another system owns. The library does
    /// not allocate the number, provide a tech tree or change the civilization's skins.
    /// </summary>
    /// <param name="civilizationId">Number above the built-in civilizations (0..4).</param>
    /// <param name="name">Text shown in the drop-down.</param>
    public RulesCivilizationHandle? AddCivilization(int civilizationId, string? name) {
        if (!_scope.IsAlive) {
            UiLog.OnceError("rules:disposed-scope", "cannot add a civilization to a disposed scope");
            return null;
        }
        if (civilizationId <= 4 || string.IsNullOrWhiteSpace(name)) {
            UiLog.OnceError("rules:bad-entry:" + civilizationId,
                "a civilization needs a number above the built-in ones (0..4) and a display name");
            return null;
        }

        if (_items.TryGetValue(civilizationId, out var existing) && !existing.IsDisposed) {
            UiLog.OnceError("rules:duplicate:" + civilizationId, $"civilization {civilizationId} already exists in this scope");
            return existing;
        }
        _items.Remove(civilizationId);

        if (_host.FindCivilizationById(civilizationId) != null) {
            UiLog.OnceError("rules:duplicate-global:" + civilizationId,
                $"civilization {civilizationId} is already in the Rules drop-down from another scope");
            return null;
        }

        RulesCivilizationHandle? handle = null;
        try {
            handle = new RulesCivilizationHandle(_scope, _host, "civilization:" + civilizationId, civilizationId, name.Trim(),
                value => _items.Remove(value.CivilizationId));
            if (!SkinPolicy.AllowNative(_scope.Runtime)) {
                handle.MarkUnsupported("TemplatePolicy.ManagedOnly disables native Rules-screen additions");
            }
            _items[civilizationId] = handle;
            return _host.Register(handle);
        }
        catch (Exception e) {
            UiGuard.Report("rules.add", e, _scope.Id);
            UiGuard.Run("rules.rollback", () => handle?.Dispose(), _scope.Id);
            _items.Remove(civilizationId);
            return null;
        }
    }
}