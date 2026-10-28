using System;
using System.Collections.Generic;
using UnityEngine;
using GameRules = DiceKingdoms.Meta.Rules;
using GameScoreboard = DiceKingdoms.Interface.Scoreboard;
using GameScoreboardRow = DiceKingdoms.Interface.ScoreboardRow;
using GameSettingSet = DiceKingdoms.Meta.SettingSetInterface;
using GameSettings = DiceKingdoms.Meta.SettingSetsInterface;
using GameTechTree = DiceKingdoms.Game.TechTree;

namespace DiceModders.UiKit.Native;

/// <summary>
/// Owns every registration that inserts objects into the game's own UI. Module APIs create stable handles; this host
/// decides when they are attached, reattached or retired as scenes and game screens change.
/// </summary>
internal sealed class NativeUiHost {
    private readonly List<NativeHandle> _bindings = new();
    private readonly List<GameScoreboardRow> _scoreboardRows = new();
    private GameSettings? _settingsScreen;
    private GameRules? _rulesScreen;
    private GameScoreboard? _scoreboardScreen;
    private bool _settingsSyncQueued;
    private bool _scoreboardSyncQueued;
    private bool _scoreboardRefitQueued;
    private bool _refreshQueued;
    private bool _disposed;
    private int _topBarSequence;
    private int _settingsBlockSequence;

    internal bool HasRegistrations => _bindings.Count > 0;

    internal int NextTopBarSequence() => ++_topBarSequence;
    internal int NextSettingsBlockSequence() => ++_settingsBlockSequence;

    /// <summary>Puts every attached mod top-bar button before the game's own buttons, ordered by spec order then sequence.</summary>
    internal void ReorderTopBar(Transform? bar) {
        if (bar == null) return;

        var handles = new List<TopBarButtonHandle>();
        var bindings = _bindings.ToArray();
        for (int i = 0; i < bindings.Length; i++) {
            if (bindings[i] is TopBarButtonHandle button && !button.IsDisposed && button.IsInBar(bar)) {
                handles.Add(button);
            }
        }

        handles.Sort((left, right) => {
            int order = left.Order.CompareTo(right.Order);
            return order != 0 ? order : left.Sequence.CompareTo(right.Sequence);
        });

        for (int i = 0; i < handles.Count; i++) {
            var handle = handles[i];
            UiGuard.Run("topbar.order", () => handle.MoveToIndex(i), handle.OwnerId);
        }
    }

    internal bool HasScoreboardColumns {
        get {
            for (int i = 0; i < _bindings.Count; i++) {
                if (_bindings[i] is ScoreboardColumnHandle column &&
                    !column.IsDisposed && column.State != NativeState.Unsupported) return true;
            }
            return false;
        }
    }

    internal bool HasSettingsBlocks {
        get {
            for (int i = 0; i < _bindings.Count; i++) {
                if (_bindings[i] is SettingsBlockHandle block &&
                    !block.IsDisposed && block.State != NativeState.Unsupported) return true;
            }
            return false;
        }
    }

    internal bool HasAttachedSettingsRows(SettingsPage page, SettingsBlockHandle? except = null) {
        var bindings = _bindings.ToArray();
        for (int i = 0; i < bindings.Length; i++) {
            if (ReferenceEquals(bindings[i], except)) continue;
            if (bindings[i] is SettingsBlockHandle block && !block.IsDisposed &&
                block.State != NativeState.Unsupported && block.Page == page && block.HasAttachedRows) {
                return true;
            }
        }
        return false;
    }

    /// <summary>Registers a stable handle and makes the first attach attempt immediately.</summary>
    internal T Register<T>(T handle) where T : NativeHandle {
        if (handle == null) throw new ArgumentNullException(nameof(handle));
        if (_disposed) {
            handle.MarkUnsupported("UiKit is shutting down");
            return handle;
        }
        if (!_bindings.Contains(handle)) {
            _bindings.Add(handle);
            try { handle.Scope.Owned.Add(handle); }
            catch (Exception e) { UiLog.OnceError("native:register", "could not track a native registration: " + e); }
        }

        UiGuard.Run("native.register", () => RefreshOne(handle, CaptureScene()), handle.OwnerId);
        return handle;
    }

    internal void Unregister(NativeHandle handle) {
        if (handle != null) _bindings.Remove(handle);
    }

    /// <summary>Scene load: forget old targets, discover the new scene and attach every registration.</summary>
    internal void Refresh() {
        if (_disposed) return;

        UiGuard.Run("scoreboard.reset", ScoreboardLayout.Reset, "runtime");
        _settingsScreen = UiGuard.Func("native.find-settings", GameUi.FindSettings);
        _rulesScreen = UiGuard.Func("native.find-rules", GameUi.FindRules);
        _scoreboardRows.Clear();
        var rows = UiGuard.Func("native.find-scoreboard", GameUi.FindScoreboardRows);
        if (rows != null) _scoreboardRows.AddRange(rows);
        TrackSceneTargets();
        RefreshAll();
    }

    /// <summary>Scene unload: drop live objects but keep every registration and its cached data.</summary>
    internal void Detach() {
        var bindings = _bindings.ToArray();
        for (int i = 0; i < bindings.Length; i++) {
            var handle = bindings[i];
            if (handle == null) continue;
            UiGuard.Run("native.detach", () => handle.Detach(NativeDetachReason.SceneChanged), handle.OwnerId);
        }

        _scoreboardRows.Clear();
        _settingsScreen = null;
        _rulesScreen = null;
        _scoreboardScreen = null;
        UiGuard.Run("scoreboard.reset", ScoreboardLayout.Reset, "runtime");
        UiGuard.Run("settings.chrome.reset", SettingsPageChrome.Reset, "runtime");
    }

    /// <summary>Applies one post-layout scoreboard fit and Settings chrome refresh after a canvas resize.</summary>
    internal void OnCanvasLayoutChanged() {
        if (_disposed) return;
        RetryWaiting();
        if (HasScoreboardColumns) RequestScoreboardRefit();
        if (HasSettingsBlocks) SettingsPageChrome.RequestRefresh();
    }

    internal void OnSettingsScreen(GameSettings? settings) {
        if (_disposed || settings == null) return;
        _settingsScreen = settings;
        RequestSettingsSync();
    }

    private void RequestSettingsSync() {
        if (_disposed || _settingsSyncQueued) return;
        _settingsSyncQueued = true;
        LayoutOnce.Request(SyncSettingsScreen);
    }

    private void SyncSettingsScreen() {
        _settingsSyncQueued = false;
        if (_disposed) return;

        var settings = _settingsScreen;
        if (settings == null) return;

        EnsureSettingsLifetime(settings);
        WatchRoot(settings.transform);
        RefreshMatching(handle => handle is SettingsTabHandle or SettingsBlockHandle);
        SettingsPageChrome.RequestRefresh();
    }

    internal void OnSettingsScreenHidden(GameSettings? settings) {
        if (_disposed) return;

        bool current = settings == null;
        if (!current) {
            var tracked = _settingsScreen;
            if (tracked == null) current = true;
            else {
                try { current = tracked.Pointer == settings!.Pointer; }
                catch { current = true; }
            }
        }
        if (!current) return;

        _settingsScreen = null;
        SettingsPageChrome.OnScreenHidden();
    }

    internal void OnSettingsScreenDestroyed(GameSettings? settings) {
        if (_disposed) return;
        OnSettingsScreenHidden(settings);
    }

    /// <summary>The game rebuilt one of its own settings pages; append/re-append the blocks that belong to it.</summary>
    internal void OnSettingsGenerated(GameSettingSet? set) {
        if (_disposed || set == null) return;

        SettingsPage? page;
        try { page = SettingsPages.PageOf(set); }
        catch (Exception e) {
            UiLog.OnceError("settings:page-of", "could not identify a settings page: " + e.Message);
            return;
        }
        if (page == null) return;

        WatchRoot(set.transform);

        var blocks = new List<SettingsBlockHandle>();
        var bindings = _bindings.ToArray();
        for (int i = 0; i < bindings.Length; i++) {
            if (bindings[i] is not SettingsBlockHandle block || block.IsDisposed ||
                block.State == NativeState.Unsupported || block.Page != page.Value) continue;
            blocks.Add(block);
        }

        blocks.Sort((left, right) => {
            int order = left.Order.CompareTo(right.Order);
            return order != 0 ? order : left.Sequence.CompareTo(right.Sequence);
        });

        for (int i = 0; i < blocks.Count; i++) {
            var block = blocks[i];
            UiGuard.Run("settings.block.generated", () => block.OnPageGenerated(set), block.OwnerId);
        }

        bool hasRows = HasAttachedSettingsRows(page.Value);
        UiGuard.Run("settings.chrome", () => SettingsPageChrome.Apply(set, hasRows), "runtime");
        SettingsPageChrome.RequestRefresh();
    }

    /// <summary>Keeps block rows contiguous and ordered by <see cref="SettingsBlockHandle.Order"/> inside one page.</summary>
    internal void ReorderSettingsBlocks(GameSettingSet? set) {
        if (set == null || _disposed) return;

        var blocks = new List<SettingsBlockHandle>();
        var bindings = _bindings.ToArray();
        for (int i = 0; i < bindings.Length; i++) {
            if (bindings[i] is not SettingsBlockHandle block || block.IsDisposed ||
                !block.IsPageAlive || !block.IsOnPage(set)) continue;
            blocks.Add(block);
        }
        if (blocks.Count == 0) return;

        blocks.Sort((left, right) => {
            int order = left.Order.CompareTo(right.Order);
            return order != 0 ? order : left.Sequence.CompareTo(right.Sequence);
        });

        for (int i = 0; i < blocks.Count; i++) {
            var rows = blocks[i].Rows;
            for (int j = 0; j < rows.Count; j++) {
                var go = rows[j].View?.Go;
                if (go != null) go.transform.SetAsLastSibling();
            }
        }
    }

    internal void OnSettingsBlockDisposed(SettingsBlockHandle? block) {
        if (_disposed || block == null) return;

        var set = SettingsPages.Find(block.Page);
        if (set == null) return;

        bool remains = HasAttachedSettingsRows(block.Page, block);
        if (remains) UiGuard.Run("settings.chrome.keep", () => SettingsPageChrome.Apply(set, true), "runtime");
        else UiGuard.Run("settings.chrome.restore", () => SettingsPageChrome.Restore(set), "runtime");
    }

    /// <summary>The game built or showed its scoreboard; row-start can precede that event, so rescan once after layout.</summary>
    internal void OnScoreboardShown(GameScoreboard? scoreboard) {
        if (_disposed || scoreboard == null) return;
        _scoreboardScreen = scoreboard;
        RequestScoreboardSync();
    }

    internal void OnScoreboardRowReady(GameScoreboardRow? row) {
        if (_disposed || row == null) return;
        if (!_scoreboardRows.Contains(row)) _scoreboardRows.Add(row);
        EnsureScoreboardLifetime(row);
        WatchRoot(row.transform);
        RefreshMatching(handle => handle is ScoreboardColumnHandle);
        RequestScoreboardRefit();
    }

    internal void OnScoreboardRowDestroyed(GameScoreboardRow? row) {
        if (row == null || _disposed) return;
        _scoreboardRows.Remove(row);
        RefreshMatching(handle => handle is ScoreboardColumnHandle);
        RequestScoreboardRefit();
    }

    internal void OnScoreboardRowChanged(GameScoreboardRow? row) {
        if (row == null || _disposed) return;
        var bindings = _bindings.ToArray();
        for (int i = 0; i < bindings.Length; i++) {
            if (bindings[i] is not ScoreboardColumnHandle column || column.IsDisposed) continue;
            UiGuard.Run("scoreboard.reapply", () => column.ReapplyRow(row), column.OwnerId);
        }
        RequestScoreboardRefit();
    }

    internal void OnRulesScreen(GameRules? rules) {
        if (_disposed || rules == null) return;
        _rulesScreen = rules;
        RefreshMatching(handle => handle is RulesCivilizationHandle);
    }

    internal RulesCivilizationHandle? FindCivilizationByKey(string key) {
        for (int i = 0; i < _bindings.Count; i++) {
            if (_bindings[i] is RulesCivilizationHandle civ && !civ.IsDisposed &&
                string.Equals(civ.Key, key, StringComparison.Ordinal)) return civ;
        }
        return null;
    }

    /// <summary>Stable id from a key, advanced past ids used by other keys.</summary>
    internal int AllocateCivilizationId(string key) {
        uint hash = 2166136261;
        for (int i = 0; i < key.Length; i++) {
            hash ^= key[i];
            hash *= 16777619;
        }

        int id = 1000 + (int)(hash % 1_000_000_000u);
        while (FindCivilizationById(id) is { } taken && !string.Equals(taken.Key, key, StringComparison.Ordinal)) {
            id++;
        }
        return id;
    }

    internal bool TryGetRulesTree(int id, out GameTechTree? tree) {
        tree = null;
        for (int i = 0; i < _bindings.Count; i++) {
            if (_bindings[i] is not RulesCivilizationHandle civ || civ.IsDisposed || civ.EntryOnly || civ.CivilizationId != id) continue;
            if (civ.TryGetTree(out var candidate) && candidate != null) {
                tree = candidate;
                return true;
            }
        }
        return false;
    }

    internal bool TryGetSkinsLike(int id, out int skinsLike) {
        for (int i = 0; i < _bindings.Count; i++) {
            if (_bindings[i] is not RulesCivilizationHandle civ || civ.IsDisposed || civ.EntryOnly || civ.CivilizationId != id) continue;
            skinsLike = civ.SkinsLike;
            return true;
        }
        skinsLike = 0;
        return false;
    }

    internal RulesCivilizationHandle? FindCivilizationById(int id) {
        for (int i = 0; i < _bindings.Count; i++) {
            if (_bindings[i] is RulesCivilizationHandle civ && !civ.IsDisposed && civ.CivilizationId == id) return civ;
        }
        return null;
    }

    internal void OnScoreboardColumnDisposed() {
        if (_disposed) return;
        RequestScoreboardRefit();
    }

    /// <summary>Re-scans native targets after a scene unload, in case the next scene was already loaded when Detach ran.</summary>
    internal void RequestRefresh() {
        if (_disposed || _refreshQueued) return;
        _refreshQueued = true;
        LayoutOnce.Request(FlushRefresh);
    }

    private void FlushRefresh() {
        _refreshQueued = false;
        Refresh();
    }

    private void RequestScoreboardSync() {
        if (_disposed || _scoreboardSyncQueued) return;
        _scoreboardSyncQueued = true;
        LayoutOnce.Request(SyncScoreboard);
    }

    private void SyncScoreboard() {
        _scoreboardSyncQueued = false;
        if (_disposed) return;

        var rows = UiGuard.Func("native.find-scoreboard", GameUi.FindScoreboardRows);
        if (rows != null) {
            _scoreboardRows.Clear();
            _scoreboardRows.AddRange(rows);
        }

        var scoreboard = _scoreboardScreen;
        if (scoreboard != null) WatchRoot(scoreboard.transform);
        RefreshMatching(handle => handle is ScoreboardColumnHandle);
        RequestScoreboardRefit();
    }

    private void RequestScoreboardRefit() {
        if (_disposed || _scoreboardRefitQueued) return;
        _scoreboardRefitQueued = true;
        LayoutOnce.Request(FlushScoreboardRefit);
    }

    private void FlushScoreboardRefit() {
        _scoreboardRefitQueued = false;
        RefitScoreboard();
    }

    /// <summary>Retries registrations that are still waiting after a canvas layout change; attached handles are untouched.</summary>
    private void RetryWaiting() {
        bool waiting = false;
        var bindings = _bindings.ToArray();
        for (int i = 0; i < bindings.Length; i++) {
            var state = bindings[i].State;
            if (state is NativeState.Pending or NativeState.Failed or NativeState.Detached) {
                waiting = true;
                break;
            }
        }
        if (!waiting) return;

        var scene = UiGuard.Func("native.capture", CaptureScene);
        if (scene == null) return;
        for (int i = 0; i < bindings.Length; i++) {
            var handle = bindings[i];
            if (handle.State is NativeState.Pending or NativeState.Failed or NativeState.Detached)
                RefreshOne(handle, scene);
        }
    }

    private void RefitScoreboard() {
        if (_disposed) return;

        int cells = 0;
        bool hasColumns = false;
        Transform? sample = null;
        var bindings = _bindings.ToArray();
        for (int i = 0; i < bindings.Length; i++) {
            if (bindings[i] is not ScoreboardColumnHandle column || column.IsDisposed ||
                column.State == NativeState.Unsupported) continue;
            hasColumns = true;
            cells += UiGuard.Value("scoreboard.layout", column.PrepareLayout, 0, column.OwnerId);
            sample ??= UiGuard.Func("scoreboard.sample", () => column.FirstCell, column.OwnerId);
        }

        UiGuard.Run("scoreboard.fit", () => ScoreboardLayout.FitBoard(hasColumns ? cells : 0, sample), "runtime");
    }

    private void TrackSceneTargets(Transform? topBar = null) {
        topBar ??= UiGuard.Func("native.find-bar", GameUi.FindActiveBar);
        WatchRoot(topBar);
        if (_settingsScreen != null) EnsureSettingsLifetime(_settingsScreen);
        WatchRoot(_settingsScreen?.transform);
        WatchRoot(_rulesScreen?.transform);

        for (int i = 0; i < _scoreboardRows.Count; i++) {
            var row = _scoreboardRows[i];
            if (row == null) continue;
            EnsureScoreboardLifetime(row);
            WatchRoot(row.transform);
        }
    }

    private static void WatchRoot(Transform? transform) {
        if (transform == null) return;
        try { LayoutEvents.Watch(transform.root); }
        catch (Exception e) { UiLog.Once("layout:scene-watch", "could not watch a scene canvas: " + e.Message); }
    }

    private void EnsureScoreboardLifetime(GameScoreboardRow row) {
        if (row == null) return;
        try {
            var go = row.gameObject;
            if (go == null) return;
            var lifetime = go.GetComponent<ScoreboardRowLifetime>();
            if (lifetime == null) lifetime = go.AddComponent<ScoreboardRowLifetime>();
            ScoreboardRowLifetimeRegistry.Bind(lifetime, this, row);
        }
        catch (Exception e) {
            UiLog.Once("scoreboard:lifetime-add", "could not watch a scoreboard row: " + e.Message);
        }
    }

    private void EnsureSettingsLifetime(GameSettings settings) {
        if (settings == null) return;
        try {
            var go = settings.gameObject;
            if (go == null) return;
            var lifetime = go.GetComponent<SettingsScreenLifetime>();
            if (lifetime == null) lifetime = go.AddComponent<SettingsScreenLifetime>();
            SettingsScreenLifetimeRegistry.Bind(lifetime, this, settings);
        }
        catch (Exception e) {
            UiLog.Once("settings:lifetime-add", "could not watch the Settings screen: " + e.Message);
        }
    }

    private void RefreshAll() {
        if (_disposed) return;
        var scene = UiGuard.Func("native.capture", CaptureScene);
        if (scene == null) return;
        RefreshMatching(_ => true, scene);
    }

    private void RefreshMatching(Func<NativeHandle, bool> predicate) {
        var scene = UiGuard.Func("native.capture", CaptureScene);
        if (scene == null) return;
        RefreshMatching(predicate, scene);
    }

    private void RefreshMatching(Func<NativeHandle, bool> predicate, NativeScene scene) {
        var bindings = _bindings.ToArray();
        for (int i = 0; i < bindings.Length; i++) {
            var handle = bindings[i];
            if (handle == null || handle.IsDisposed) continue;
            if (!UiGuard.Value("native.match", () => predicate(handle), false, handle.OwnerId)) continue;
            RefreshOne(handle, scene);
        }
    }

    private void RefreshOne(NativeHandle handle, NativeScene scene) {
        UiGuard.Run("native.refresh", () => handle.Refresh(scene), handle.OwnerId);
        if (handle is ScoreboardColumnHandle && !handle.IsDisposed) RequestScoreboardRefit();
    }

    private NativeScene CaptureScene() {
        PruneTargets();

        if (_scoreboardRows.Count == 0) {
            var rows = UiGuard.Func("native.find-scoreboard", GameUi.FindScoreboardRows);
            if (rows != null) _scoreboardRows.AddRange(rows);
        }
        if (_rulesScreen == null) _rulesScreen = UiGuard.Func("native.find-rules", GameUi.FindRules);
        if (_settingsScreen == null) _settingsScreen = UiGuard.Func("native.find-settings", GameUi.FindSettings);

        var topBar = UiGuard.Func("native.find-bar", GameUi.FindActiveBar);
        TrackSceneTargets(topBar);

        return new NativeScene {
            TopBar = topBar,
            Settings = _settingsScreen,
            ScoreboardRows = _scoreboardRows.ToArray(),
            RulesScreen = _rulesScreen,
        };
    }

    private void PruneTargets() {
        for (int i = _scoreboardRows.Count - 1; i >= 0; i--) {
            if (_scoreboardRows[i] == null) _scoreboardRows.RemoveAt(i);
        }
    }

    internal void Dispose() {
        if (_disposed) return;
        _disposed = true;

        var bindings = _bindings.ToArray();
        _bindings.Clear();
        for (int i = bindings.Length - 1; i >= 0; i--) {
            var handle = bindings[i];
            if (handle == null) continue;
            UiGuard.Run("native.dispose", handle.Dispose, handle.OwnerId);
        }

        _scoreboardRows.Clear();
        _settingsScreen = null;
        _rulesScreen = null;
        _scoreboardScreen = null;
        _scoreboardSyncQueued = false;
        _scoreboardRefitQueued = false;
        _refreshQueued = false;
        UiGuard.Run("scoreboard.reset", ScoreboardLayout.Reset, "runtime");
        UiGuard.Run("settings.chrome.reset", SettingsPageChrome.Reset, "runtime");
    }
}
