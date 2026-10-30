using DiceModders.UiKit.Native;

namespace DiceModders.UiKit.Custom;

/// <summary>
/// Tooltips for every widget. They look and behave like the game's (same frame, font, icons and 0.4 s delay) and always draw above UiKit windows.
/// <c>UiButton.Tooltip</c> keeps working as before.
/// </summary>
public static class UiTooltip {
    /// <summary>Shows <paramref name="text"/> when the pointer rests on the element; null or empty removes it. Icon tokens such as <c>{r.gold}</c> work.</summary>
    public static T? Tooltip<T>(this T? element, string? text) where T : UiElement {
        if (element == null || !element.IsAlive || element.Root == null) return element;
        UiGuard.Run("element.tooltip", () => NativeTooltip.Set(element.Root.gameObject, text));
        return element;
    }
}
