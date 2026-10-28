using System;
using HarmonyLib;
using GameCivilization = DiceKingdoms.Interface.Civilization;
using GameInterface = DiceKingdoms.Interface.Interface;
using GameMainMenu = DiceKingdoms.Meta.MainMenu;
using GamePlayerSkins = DiceKingdoms.Game.PlayerSkins;
using GameRules = DiceKingdoms.Meta.Rules;
using GameScoreboard = DiceKingdoms.Interface.Scoreboard;
using GameScoreboardRow = DiceKingdoms.Interface.ScoreboardRow;
using GameSettingSet = DiceKingdoms.Meta.SettingSetInterface;
using GameSettings = DiceKingdoms.Meta.SettingSetsInterface;
using GameTechnologies = DiceKingdoms.Game.Technologies;
using GameTechTree = DiceKingdoms.Game.TechTree;

namespace DiceModders.UiKit.Native;

/// <summary>
/// The few game methods the native layer must observe or answer. Every hook contains its own error boundary: a failure
/// here must never change the game's own behaviour.
/// </summary>
internal static class NativeHooks {
    private static Harmony? _harmony;
    private static UiKitRuntime? _runtime;

    public static void Install(UiKitRuntime runtime) {
        if (_harmony != null) return;
        _runtime = runtime;

        Harmony? harmony = null;
        try {
            harmony = new Harmony("DiceModders.UiKit.native");
            harmony.PatchAll(typeof(ScoreboardRowStartPatch));
            harmony.PatchAll(typeof(RulesAwakePatch));
            harmony.PatchAll(typeof(RulesEnablePatch));
            _harmony = harmony;

            // Optional groups are installed independently: one game update cannot disable the other integrations.
            TryPatch(harmony, typeof(SettingsGeneratePatch), "settings generate");
            TryPatch(harmony, typeof(SettingsScreenEnablePatch), "settings screen enable");
            TryPatch(harmony, typeof(SettingsScreenDisablePatch), "settings screen disable");
            TryPatch(harmony, typeof(MainMenuStartPatch), "main menu start");
            TryPatch(harmony, typeof(InterfaceStartPatch), "interface start");
            TryPatch(harmony, typeof(ScoreboardRowIslandPatch), "scoreboard island");
            TryPatch(harmony, typeof(ScoreboardStartPatch), "scoreboard start");
            TryPatch(harmony, typeof(ScoreboardPopulatePatch), "scoreboard populate");
            TryPatch(harmony, typeof(ScoreboardEnablePatch), "scoreboard enable");

            UiLog.Info("native game hooks installed");
        }
        catch (Exception e) {
            _runtime = null;
            if (harmony != null) {
                try { harmony.UnpatchSelf(); }
                catch (Exception cleanup) { UiLog.Warn("partial native hooks could not be removed: " + cleanup.Message); }
            }
            UiLog.Error("native game hooks could not be installed: " + e);
        }
    }

    public static void Uninstall() {
        var harmony = _harmony;
        _harmony = null;
        _runtime = null;
        if (harmony == null) return;

        try { harmony.UnpatchSelf(); }
        catch (Exception e) { UiLog.Warn("native game hooks could not be removed: " + e.Message); }
    }

    private static void TryPatch(Harmony harmony, Type type, string label) {
        try { harmony.PatchAll(type); }
        catch (Exception e) { UiLog.Warn($"{label} hooks could not be installed: {e.Message}"); }
    }

    internal static void GameUiStarted() {
        try {
            var runtime = _runtime;
            if (runtime == null) return;
            runtime.NativeUi.Refresh();
            runtime.TryMarkUiReady();
            runtime.RequestReadyProbe();
        }
        catch (Exception e) { UiLog.OnceError("game:started", "game UI start hook failed: " + e); }
    }

    internal static void RowStarted(GameScoreboardRow? row) {
        try { _runtime?.NativeUi.OnScoreboardRowReady(row); }
        catch (Exception e) { UiLog.OnceError("scoreboard:row", "scoreboard row hook failed: " + e); }
    }

    internal static void ScoreboardShown(GameScoreboard? scoreboard) {
        try { _runtime?.NativeUi.OnScoreboardShown(scoreboard); }
        catch (Exception e) { UiLog.OnceError("scoreboard:shown", "scoreboard shown hook failed: " + e); }
    }

    internal static void RulesOpened(GameRules? rules) {
        try { _runtime?.NativeUi.OnRulesScreen(rules); }
        catch (Exception e) { UiLog.OnceError("rules:screen", "Rules screen hook failed: " + e); }
    }

    internal static void SettingsGenerated(GameSettingSet? set) {
        try { _runtime?.NativeUi.OnSettingsGenerated(set); }
        catch (Exception e) { UiLog.OnceError("settings:generated", "Settings generate hook failed: " + e); }
    }

    internal static void SettingsScreenOpened(GameSettings? settings) {
        try { _runtime?.NativeUi.OnSettingsScreen(settings); }
        catch (Exception e) { UiLog.OnceError("settings:screen-open", "Settings screen open hook failed: " + e); }
    }

    internal static void SettingsScreenClosed(GameSettings? settings) {
        try { _runtime?.NativeUi.OnSettingsScreenHidden(settings); }
        catch (Exception e) { UiLog.OnceError("settings:screen-close", "Settings screen close hook failed: " + e); }
    }

    internal static void RowIslandChanged(GameScoreboardRow? row) {
        try { _runtime?.NativeUi.OnScoreboardRowChanged(row); }
        catch (Exception e) { UiLog.OnceError("scoreboard:island", "scoreboard island hook failed: " + e); }
    }
}

[HarmonyPatch(typeof(GameScoreboardRow), "Start")]
internal static class ScoreboardRowStartPatch {
    [HarmonyPostfix]
    public static void Postfix(GameScoreboardRow __instance) => NativeHooks.RowStarted(__instance);
}

[HarmonyPatch(typeof(GameScoreboard), "Start")]
internal static class ScoreboardStartPatch {
    [HarmonyPostfix]
    public static void Postfix(GameScoreboard __instance) => NativeHooks.ScoreboardShown(__instance);
}

[HarmonyPatch(typeof(GameScoreboard), "Populate")]
internal static class ScoreboardPopulatePatch {
    [HarmonyPostfix]
    public static void Postfix(GameScoreboard __instance) => NativeHooks.ScoreboardShown(__instance);
}

[HarmonyPatch(typeof(GameScoreboard), "OnEnable")]
internal static class ScoreboardEnablePatch {
    [HarmonyPostfix]
    public static void Postfix(GameScoreboard __instance) => NativeHooks.ScoreboardShown(__instance);
}

[HarmonyPatch(typeof(GameRules), "Awake")]
internal static class RulesAwakePatch {
    [HarmonyPostfix]
    public static void Postfix(GameRules __instance) => NativeHooks.RulesOpened(__instance);
}

[HarmonyPatch(typeof(GameRules), "OnEnable")]
internal static class RulesEnablePatch {
    [HarmonyPostfix]
    public static void Postfix(GameRules __instance) => NativeHooks.RulesOpened(__instance);
}

[HarmonyPatch(typeof(GameSettingSet), "GenerateElements")]
internal static class SettingsGeneratePatch {
    [HarmonyPostfix]
    public static void Postfix(GameSettingSet __instance) => NativeHooks.SettingsGenerated(__instance);
}

[HarmonyPatch(typeof(GameSettings), "OnEnable")]
internal static class SettingsScreenEnablePatch {
    [HarmonyPostfix]
    public static void Postfix(GameSettings __instance) => NativeHooks.SettingsScreenOpened(__instance);
}

[HarmonyPatch(typeof(GameSettings), "OnDisable")]
internal static class SettingsScreenDisablePatch {
    [HarmonyPostfix]
    public static void Postfix(GameSettings __instance) => NativeHooks.SettingsScreenClosed(__instance);
}

[HarmonyPatch(typeof(GameMainMenu), "Start")]
internal static class MainMenuStartPatch {
    [HarmonyPostfix]
    public static void Postfix() => NativeHooks.GameUiStarted();
}

[HarmonyPatch(typeof(GameInterface), "Start")]
internal static class InterfaceStartPatch {
    [HarmonyPostfix]
    public static void Postfix() => NativeHooks.GameUiStarted();
}

[HarmonyPatch(typeof(GameScoreboardRow), "Island", MethodType.Setter)]
internal static class ScoreboardRowIslandPatch {
    [HarmonyPostfix]
    public static void Postfix(GameScoreboardRow __instance) => NativeHooks.RowIslandChanged(__instance);
}
