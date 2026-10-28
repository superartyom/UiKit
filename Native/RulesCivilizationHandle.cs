using System;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine;
using GameCivilization = DiceKingdoms.Interface.Civilization;
using GameDropdown = DiceKingdoms.Interface.Dropdown;
using GameRules = DiceKingdoms.Meta.Rules;

namespace DiceModders.UiKit.Native;

/// <summary>
/// A civilization appended to the game's Rules drop-down. The mod's stable <see cref="Key"/> is exposed as
/// <see cref="NativeHandle.Id"/>; <see cref="CivilizationId"/> is the stable numeric value derived from it.
/// </summary>
public sealed class RulesCivilizationHandle : NativeHandle {
    private readonly Action<RulesCivilizationHandle>? _onDispose;
    private string _name;
    private GameRules? _rules;
    private GameDropdown? _dropdown;
    private TMP_Dropdown? _tmp;
    private Il2CppSystem.Object? _value;
    private int _optionIndex = -1;
    private int _valueIndex = -1;

    internal RulesCivilizationHandle(UiScope scope, NativeUiHost host, string key, int civilizationId, string name,
                                     Action<RulesCivilizationHandle>? onDispose)
        : base(scope, host, key) {
        _onDispose = onDispose;
        Key = key;
        CivilizationId = civilizationId;
        _name = name;
    }

    /// <summary>Stable key supplied by the mod.</summary>
    public string Key { get; }

    /// <summary>Stable civilization number derived from <see cref="Key"/>.</summary>
    public int CivilizationId { get; }

    /// <summary>Text currently shown in the Rules drop-down.</summary>
    public string Name => _name;

    /// <summary>Changes the drop-down label without changing the stable id.</summary>
    public void SetName(string? name) {
        if (IsDisposed || string.IsNullOrWhiteSpace(name)) return;

        var tmp = _tmp;
        var dropdown = _dropdown;
        int index = dropdown != null ? LocateValue(dropdown.values) : -1;
        _name = name.Trim();

        UiGuard.Run("rules.set-name", () => {
            if (tmp == null || index < 0 || index >= tmp.options.Count) return;
            tmp.options[index].text = _name;
            tmp.RefreshShownValue();
            _optionIndex = index;
            _valueIndex = index;
        }, OwnerId);
    }

    internal override void Refresh(NativeScene scene) {
        if (IsDisposed || State == NativeState.Unsupported) return;

        var rules = scene.RulesScreen;
        if (rules == null) {
            DetachCore();
            MarkWaiting("waiting for the Rules screen");
            return;
        }

        if (_rules == rules && _dropdown != null && _tmp != null && OptionStillPresent()) {
            MarkAttached("Rules screen");
            return;
        }

        DetachCore();
        try { Attach(rules); }
        catch (Exception e) {
            UiGuard.Report("rules.attach", e, OwnerId);
            DetachCore();
            MarkFailed("civilization could not be added to the Rules screen");
            return;
        }

        if (OptionStillPresent()) MarkAttached("Rules screen");
        else MarkFailed(Status ?? "the Rules drop-down is unavailable");
    }

    private void Attach(GameRules rules) {
        var dropdown = rules.civilizationDropdown;
        var tmp = dropdown != null ? dropdown.dropdown : null;
        if (dropdown == null || tmp == null) {
            MarkFailed("the Rules screen has no civilization drop-down");
            UiLog.OnceError("rules:no-dropdown:" + Key, "the Rules screen has no civilization drop-down; retrying");
            return;
        }

        int optionIndex = tmp.options.Count;
        tmp.options.Add(new TMP_Dropdown.OptionData(_name));

        var old = dropdown.values;
        try {
            int oldLength = old != null ? old.Length : 0;
            var values = new Il2CppReferenceArray<Il2CppSystem.Object>(optionIndex + 1);
            for (int i = 0; i < optionIndex && i < oldLength; i++) values[i] = old![i];
            var boxed = Il2CppSystem.Enum.ToObject(Il2CppType.Of<GameCivilization>(), CivilizationId);
            values[optionIndex] = boxed;
            dropdown.values = values;
            tmp.RefreshShownValue();
            _value = boxed;
        }
        catch {
            // Do not leave a half-added option behind when the values array or the refresh fails.
            try { dropdown.values = old; }
            catch { /* the Rules screen is already broken; the outer guard reports the failure */ }
            try {
                if (optionIndex < tmp.options.Count) tmp.options.RemoveAt(optionIndex);
            }
            catch { /* cleanup must not hide the original attach failure */ }
            throw;
        }

        _rules = rules;
        _dropdown = dropdown;
        _tmp = tmp;
        _optionIndex = optionIndex;
        _valueIndex = optionIndex;
    }

    protected override void DisposeCore() {
        try { _onDispose?.Invoke(this); }
        catch (Exception e) { UiLog.OnceError("rules:dispose-callback", "civilization cleanup failed: " + e); }
    }

    protected override void DetachCore() {
        var tmp = _tmp;
        var dropdown = _dropdown;
        var storedOptionIndex = _optionIndex;
        var storedValueIndex = _valueIndex;
        var name = _name;
        var value = _value;

        try {
            if (tmp == null || dropdown == null) return;

            UiGuard.Run("rules.detach", () => {
                var values = dropdown.values;
                int valueIndex = LocateValue(values);
                if (valueIndex < 0 && storedValueIndex >= 0 &&
                    values != null && storedValueIndex < values.Length &&
                    ReferenceEquals(values[storedValueIndex], value)) valueIndex = storedValueIndex;

                int optionIndex = valueIndex;
                if (optionIndex < 0 && storedOptionIndex >= 0 && storedOptionIndex < tmp.options.Count) {
                    var stored = tmp.options[storedOptionIndex];
                    if (stored != null && string.Equals(stored.text, name, StringComparison.Ordinal))
                        optionIndex = storedOptionIndex;
                }

                if (optionIndex >= 0 && optionIndex < tmp.options.Count) {
                    tmp.options.RemoveAt(optionIndex);
                }

                if (values != null && valueIndex >= 0 && valueIndex < values.Length) {
                    var next = new Il2CppReferenceArray<Il2CppSystem.Object>(Mathf.Max(0, values.Length - 1));
                    int dst = 0;
                    for (int i = 0; i < values.Length; i++) {
                        if (i == valueIndex) continue;
                        if (dst < next.Length) next[dst++] = values[i];
                    }
                    dropdown.values = next;
                }

                if (tmp.options.Count == 0) {
                    tmp.SetValueWithoutNotify(0);
                }
                else if (tmp.value == optionIndex || tmp.value >= tmp.options.Count) {
                    tmp.SetValueWithoutNotify(0);
                }
                tmp.RefreshShownValue();
            }, OwnerId);
        }
        finally {
            _tmp = null;
            _dropdown = null;
            _rules = null;
            _value = null;
            _optionIndex = -1;
            _valueIndex = -1;
        }
    }

    private bool OptionStillPresent() {
        var tmp = _tmp;
        var dropdown = _dropdown;
        if (tmp == null || dropdown == null) return false;

        int index = LocateValue(dropdown.values);
        if (index < 0 || index >= tmp.options.Count) return false;
        var option = tmp.options[index];
        if (option == null || !string.Equals(option.text, _name, StringComparison.Ordinal)) return false;

        _optionIndex = index;
        _valueIndex = index;
        return true;
    }

    /// <summary>Finds our boxed civilization value after other registrations added or removed their own entries.</summary>
    private int LocateValue(Il2CppReferenceArray<Il2CppSystem.Object>? values) {
        var ours = _value;
        if (ours == null || values == null) return -1;

        for (int i = 0; i < values.Length; i++) {
            var candidate = values[i];
            if (candidate == null) continue;
            if (ReferenceEquals(candidate, ours)) return i;
            try {
                if (candidate.Equals(ours)) return i;
            }
            catch {
                // Different IL2CPP wrappers can fail to compare; try the next entry.
            }
        }
        return -1;
    }
}
