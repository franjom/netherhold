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
        new KeyboardAction((GameAction)1, 1748u),
        new KeyboardAction((GameAction)2, 1729u),
        new KeyboardAction((GameAction)3, 2044u),
        new KeyboardAction((GameAction)4, 1730u),
        new KeyboardAction((GameAction)5, 1732u),
        new KeyboardAction((GameAction)6, 1733u),
        new KeyboardAction((GameAction)7, 1734u),
        new KeyboardAction((GameAction)8, 1735u),
        new KeyboardAction((GameAction)9, 1736u),
        new KeyboardAction((GameAction)10, 1737u),
        new KeyboardAction((GameAction)11, 1738u),
        new KeyboardAction((GameAction)12, 1739u),
        new KeyboardAction((GameAction)13, 1740u),
        new KeyboardAction((GameAction)14, 124u),
        new KeyboardAction((GameAction)15, 125u),
        new KeyboardAction((GameAction)16, 118u),
        new KeyboardAction((GameAction)17, 119u),
        new KeyboardAction((GameAction)18, 106u),
        new KeyboardAction((GameAction)19, 107u),
        new KeyboardAction((GameAction)20, 120u),
        new KeyboardAction((GameAction)21, 121u),
        new KeyboardAction((GameAction)22, 122u),
        new KeyboardAction((GameAction)23, 123u),
        new KeyboardAction((GameAction)24, 1727u),
        new KeyboardAction((GameAction)25, 1728u),
        new KeyboardAction((GameAction)26, 1744u),
        new KeyboardAction((GameAction)27, 2831u),
        new KeyboardAction((GameAction)58, 2830u),
        new KeyboardAction((GameAction)59, 2832u),
        new KeyboardAction((GameAction)60, 2833u),
        new KeyboardAction((GameAction)61, 2834u),
        new KeyboardAction((GameAction)62, 2835u),
        new KeyboardAction((GameAction)63, 2848u),
        new KeyboardAction((GameAction)28, 1749u),
        new KeyboardAction((GameAction)29, 1750u),
        new KeyboardAction((GameAction)30, 1741u),
        new KeyboardAction((GameAction)31, 1742u),
        new KeyboardAction((GameAction)32, 1743u),
        new KeyboardAction((GameAction)33, 1486u),
        new KeyboardAction((GameAction)34, 1487u),
        new KeyboardAction((GameAction)35, 1488u),
        new KeyboardAction((GameAction)36, 1751u),
        new KeyboardAction((GameAction)37, 126u),
        new KeyboardAction((GameAction)38, 1745u),
        new KeyboardAction((GameAction)39, 1298u),
        new KeyboardAction((GameAction)40, 1752u),
        new KeyboardAction((GameAction)41, 533u),
        new KeyboardAction((GameAction)42, 1299u),
        new KeyboardAction((GameAction)43, 1746u),
        new KeyboardAction((GameAction)44, 1747u),
        new KeyboardAction((GameAction)45, 1761u),
        new KeyboardAction((GameAction)46, 1762u),
        new KeyboardAction((GameAction)47, 1757u),
        new KeyboardAction((GameAction)48, 1758u),
        new KeyboardAction((GameAction)49, 1756u),
        new KeyboardAction((GameAction)50, 1755u),
        new KeyboardAction((GameAction)51, 1759u),
        new KeyboardAction((GameAction)52, 1760u),
        new KeyboardAction((GameAction)53, 1763u),
        new KeyboardAction((GameAction)54, 1764u),
        new KeyboardAction((GameAction)55, 1753u),
        new KeyboardAction((GameAction)56, 1754u),
        new KeyboardAction((GameAction)57, 1269u),
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
