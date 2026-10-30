namespace DiceModders.UiKit;

/// <summary>
/// Where a custom window lives relative to the game's own UI.
/// </summary>
public enum Layer {
    /// <summary>Below the game's UI (a background panel, an ambient overlay). The game's HUD lies on top and takes the clicks, so windows here are for display only.</summary>
    BehindGame,

    /// <summary>
    /// At the game's own sorting order: windows feel like part of the HUD and stay below the game's
    /// higher-order popups.
    /// </summary>
    Game,

    /// <summary>Above the game's UI (the default for custom windows).</summary>
    AboveGame,
}
