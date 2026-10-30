using DiceModders.UiKit.Native;

namespace DiceModders.UiKit;

/// <summary>The native-screen half of a scope: top bar, Settings, scoreboard, Rules screen and the game's dialog.</summary>
public sealed class NativeApi {
    internal NativeApi(UiScope scope) {
        TopBar = new TopBarApi(scope);
        Settings = new SettingsApi(scope);
        Scoreboard = new ScoreboardApi(scope);
        Rules = new RulesApi(scope);
        Dialogs = new DialogsApi(scope);
    }

    public TopBarApi TopBar { get; }
    public SettingsApi Settings { get; }
    public ScoreboardApi Scoreboard { get; }
    public RulesApi Rules { get; }
    /// <summary>The game's own yes/no and information dialog.</summary>
    public DialogsApi Dialogs { get; }
}
