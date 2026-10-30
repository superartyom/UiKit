using GameStructuredText = DiceKingdoms.Interface.StructuredText;

namespace DiceModders.UiKit.Native;

/// <summary>
/// Turns a plain string into the game's <c>StructuredText</c>. The string becomes one hole of a format string, so the game reads it as text and still
/// turns tokens such as <c>{r.gold}</c> or <c>{o.question-mark}</c> inside it into icons.
/// </summary>
internal static class GameTextFactory {
    public static GameStructuredText Plain(string? text) {
        var args = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppSystem.Object>(1);
        args[0] = (Il2CppSystem.String)(text ?? string.Empty);
        var formattable = Il2CppSystem.Runtime.CompilerServices.FormattableStringFactory.Create("{0}", args);
        return new GameStructuredText(formattable);
    }
}
