using System;
using DiceKingdoms.Game;
using UnityEngine;

namespace DiceModders.UiKit.Native;

/// <summary>Description of a button to insert into the game's top-right bar.</summary>
public sealed class TopBarButtonSpec {
    /// <summary>Optional diagnostic id. It is reported in logs and does not have to be globally unique.</summary>
    public string? Id { get; init; }

    /// <summary>Name of an icon in the shared UiKit theme. Safe to set before the game's assets exist.</summary>
    public string? IconName { get; init; }

    /// <summary>A caller-supplied icon. Takes precedence over <see cref="IconName"/>.</summary>
    public Sprite? Icon { get; init; }

    public string? Tooltip { get; init; }
    public Action? OnClick { get; init; }
    public bool InitiallyVisible { get; init; } = true;

    /// <summary>Lower values come first among mod buttons. Equal values keep registration order.</summary>
    public int Order { get; init; }

    /// <summary>Initial enabled state. Use <see cref="TopBarButtonHandle.SetInteractable"/> to change it later.</summary>
    public bool InitiallyInteractable { get; init; } = true;
}

/// <summary>Description of a block to append to one of the game's built-in Settings pages.</summary>
public sealed class SettingsBlockSpec {
    /// <summary>Scope-local id. A null or empty id gets a generated diagnostic id.</summary>
    public string? Id { get; init; }

    /// <summary>Built-in page that receives the block.</summary>
    public SettingsPage Page { get; init; } = SettingsPage.Game;

    /// <summary>Optional header shown above the first row.</summary>
    public string? Title { get; init; }

    /// <summary>Lower values come first among mod blocks on the same page.</summary>
    public int Order { get; init; }

    /// <summary>Called once, immediately, while the block is still detached. Add stable rows with the handle.</summary>
    public Action<SettingsBlockHandle>? Build { get; init; }
}

/// <summary>Description of a tab to insert into the game's Settings screen.</summary>
public sealed class SettingsTabSpec {
    /// <summary>Scope-local id. A null or empty id gets a generated diagnostic id.</summary>
    public string? Id { get; init; }

    /// <summary>Text of the tab and the title used in logs.</summary>
    public string? Title { get; init; }

    /// <summary>Called once, immediately, while the tab is still detached. Add rows here.</summary>
    public Action<SettingsTabHandle>? Build { get; init; }
}

/// <summary>Description of a column to insert into every row of the in-match scoreboard.</summary>
public sealed class ScoreboardColumnSpec {
    /// <summary>Stable diagnostic id, unique inside the owning scope.</summary>
    public string? Id { get; init; }

    public string? Tooltip { get; init; }
    public Sprite? Icon { get; init; }
}
