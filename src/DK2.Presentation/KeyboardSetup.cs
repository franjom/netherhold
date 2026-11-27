// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Settings;

namespace DK2.Presentation;

public readonly record struct KeyboardAction(GameAction Action, uint TextId);

/// <summary>The 63-row native roster at 0x006BCE50, in UI order; unsupported actions remain visible.</summary>
public static class KeyboardSetup
{
    public static IReadOnlyList<KeyboardAction> Actions { get; } = Array.AsReadOnly(
    [
        new KeyboardAction(GameAction.Sniper, 1748u),
        new KeyboardAction(GameAction.PickLock, 1729u),
        new KeyboardAction(GameAction.Fire, 2044u),
        new KeyboardAction(GameAction.Creep, 1730u),
        new KeyboardAction(GameAction.Run, 1732u),
        new KeyboardAction(GameAction.Melee, 1733u),
        new KeyboardAction(GameAction.Spell1, 1734u),
        new KeyboardAction(GameAction.Spell2, 1735u),
        new KeyboardAction(GameAction.Spell3, 1736u),
        new KeyboardAction(GameAction.PrimaryAbility, 1737u),
        new KeyboardAction(GameAction.SecondaryAbility, 1738u),
        new KeyboardAction(GameAction.SelectAlly, 1739u),
        new KeyboardAction(GameAction.RemoveAlly, 1740u),
        new KeyboardAction(GameAction.ZoomIn, 124u),
        new KeyboardAction(GameAction.ZoomOut, 125u),
        new KeyboardAction(GameAction.ScrollUp, 118u),
        new KeyboardAction(GameAction.ScrollDown, 119u),
        new KeyboardAction(GameAction.ScrollLeft, 106u),
        new KeyboardAction(GameAction.ScrollRight, 107u),
        new KeyboardAction(GameAction.CameraModifier, 120u),
        new KeyboardAction(GameAction.FastScroll, 121u),
        new KeyboardAction(GameAction.RotateLeft, 122u),
        new KeyboardAction(GameAction.RotateRight, 123u),
        new KeyboardAction(GameAction.Options, 1727u),
        new KeyboardAction(GameAction.Screenshot, 1728u),
        new KeyboardAction(GameAction.ObjectsOnly, 1744u),
        new KeyboardAction(GameAction.Broadcast, 2831u),
        new KeyboardAction(GameAction.MessageAllies, 2830u),
        new KeyboardAction(GameAction.MessageAlly1, 2832u),
        new KeyboardAction(GameAction.MessageAlly2, 2833u),
        new KeyboardAction(GameAction.MessageAlly3, 2834u),
        new KeyboardAction(GameAction.MessageAlly4, 2835u),
        new KeyboardAction(GameAction.Action63, 2848u),
        new KeyboardAction(GameAction.AmbientUp, 1749u),
        new KeyboardAction(GameAction.AmbientDown, 1750u),
        new KeyboardAction(GameAction.Isometric, 1741u),
        new KeyboardAction(GameAction.TopDown, 1742u),
        new KeyboardAction(GameAction.Oblique, 1743u),
        new KeyboardAction(GameAction.UserCamera1, 1486u),
        new KeyboardAction(GameAction.UserCamera2, 1487u),
        new KeyboardAction(GameAction.UserCamera3, 1488u),
        new KeyboardAction(GameAction.Allies, 1751u),
        new KeyboardAction(GameAction.NextFight, 126u),
        new KeyboardAction(GameAction.TogglePanel, 1745u),
        new KeyboardAction(GameAction.Heart, 1298u),
        new KeyboardAction(GameAction.PlayerInformation, 1752u),
        new KeyboardAction(GameAction.FullMap, 533u),
        new KeyboardAction(GameAction.Portal, 1299u),
        new KeyboardAction(GameAction.MouseRotate, 1746u),
        new KeyboardAction(GameAction.MouseZoom, 1747u),
        new KeyboardAction(GameAction.GammaUp, 1761u),
        new KeyboardAction(GameAction.GammaDown, 1762u),
        new KeyboardAction(GameAction.PitchUp, 1757u),
        new KeyboardAction(GameAction.PitchDown, 1758u),
        new KeyboardAction(GameAction.RollLeft, 1756u),
        new KeyboardAction(GameAction.RollRight, 1755u),
        new KeyboardAction(GameAction.YawLeft, 1759u),
        new KeyboardAction(GameAction.YawRight, 1760u),
        new KeyboardAction(GameAction.PickHighest, 1763u),
        new KeyboardAction(GameAction.PickLowest, 1764u),
        new KeyboardAction(GameAction.QuickLoad, 1753u),
        new KeyboardAction(GameAction.QuickSave, 1754u),
        new KeyboardAction(GameAction.Restart, 1269u),
    ]);
    /// <summary>The actions a key reaches. Of the possession keys, PickLock and the two abilities are not built.</summary>
    public static bool IsBuilt(GameAction action)
    {
        return action is >= GameAction.ZoomIn and <= GameAction.RotateRight
        or GameAction.Sniper or (>= GameAction.Fire and <= GameAction.Spell3) or GameAction.SelectAlly or GameAction.RemoveAlly
        or GameAction.Options or GameAction.ObjectsOnly or GameAction.AmbientUp or GameAction.AmbientDown
        or GameAction.NextFight or GameAction.TogglePanel or GameAction.Heart or GameAction.FullMap
        or GameAction.Portal or GameAction.MouseRotate or GameAction.MouseZoom;
    }
}
