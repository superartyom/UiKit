using System;
using UnityEngine;
using GameConfirmModal = DiceKingdoms.Interface.ConfirmModal;

namespace DiceModders.UiKit.Native;

/// <summary>
/// The game's own modal dialog (<c>ConfirmModal</c>): the yes/no question the game asks before going back to the menu, and its information box.
/// Text may contain the game's icon tokens (<c>{r.gold}</c>, <c>{o.question-mark}</c> ...). The dialog exists on the match screen; elsewhere the calls
/// return false and nothing opens.
/// </summary>
public sealed class DialogsApi {
    private const string DefaultIcon = "{o.question-mark}";
    private readonly UiScope _scope;

    internal DialogsApi(UiScope scope) => _scope = scope;

    /// <summary>True while the game's dialog is open (ours or the game's own).</summary>
    public bool IsOpen => UiGuard.Value("dialogs.open", () => Find() is { } modal && modal.Opened, false, _scope.Id);

    /// <summary>
    /// Asks a yes/no question with the game's dialog. <paramref name="onConfirm"/> runs on the confirm button, <paramref name="onCancel"/> on cancel.
    /// <paramref name="dontShowAgainId"/> adds the game's "don't show again" box: once ticked, later calls with the same id confirm at once without asking
    /// (for this game session). Returns false when the dialog is not available or the scope is disposed.
    /// </summary>
    public bool Confirm(string? text, Action? onConfirm, Action? onCancel = null, string? icon = null, string? dontShowAgainId = null) {
        if (!_scope.IsAlive) return false;
        return UiGuard.Value("dialogs.confirm", () => {
            var modal = Find();
            if (modal == null) return false;
            modal.OpenConfirm(GameTextFactory.Plain(icon ?? DefaultIcon), GameTextFactory.Plain(text),
                Wrap(modal, onCancel, "dialogs.cancel"), Wrap(modal, onConfirm, "dialogs.confirm-click"), dontShowAgainId);
            return true;
        }, false, _scope.Id);
    }

    /// <summary>Shows an information box with one button. <paramref name="onClose"/> runs when it is closed. Returns false when not available.</summary>
    public bool Info(string? text, Action? onClose = null, string? icon = null, string? dontShowAgainId = null) {
        if (!_scope.IsAlive) return false;
        return UiGuard.Value("dialogs.info", () => {
            var modal = Find();
            if (modal == null) return false;
            modal.OpenInformation(GameTextFactory.Plain(icon ?? DefaultIcon), GameTextFactory.Plain(text),
                Wrap(modal, onClose, "dialogs.close"), dontShowAgainId);
            return true;
        }, false, _scope.Id);
    }

    /// <summary>The game's callbacks hold our delegate; it is kept alive with the dialog object and does nothing once the scope is disposed.</summary>
    private Il2CppSystem.Action? Wrap(GameConfirmModal modal, Action? callback, string operation) {
        var scope = _scope;
        Action safe = () => { if (scope.IsAlive) UiGuard.Callback(operation, callback); };
        var native = Il2CppDelegates.ToIl2CppAction(safe);
        Il2CppDelegates.Hold(modal, new object?[] { safe, native });
        return native;
    }

    /// <summary>The dialog of the loaded scene (it may be inactive until opened).</summary>
    private static GameConfirmModal? Find() {
        foreach (var o in Resources.FindObjectsOfTypeAll<GameConfirmModal>())
            if (o != null && o.gameObject.scene.IsValid()) return o;
        return null;
    }
}
