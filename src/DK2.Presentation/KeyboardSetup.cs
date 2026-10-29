// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Settings;

namespace DK2.Presentation;

public readonly record struct KeyboardAction(GameAction Action, uint TextId);

/// <summary>The 63-row native roster at 0x006BCE50, in UI order; unsupported actions remain visible.</summary>
public static class KeyboardSetup
{
    public static IReadOnlyList<KeyboardAction> Actions { get; } = Array.AsReadOnly(new KeyboardAction[]
    {
        new((GameAction)1, 1748u),
        new((GameAction)2, 1729u),
        new((GameAction)3, 2044u),
        new((GameAction)4, 1730u),
        new((GameAction)5, 1732u),
        new((GameAction)6, 1733u),
        new((GameAction)7, 1734u),
        new((GameAction)8, 1735u),
        new((GameAction)9, 1736u),
        new((GameAction)10, 1737u),
        new((GameAction)11, 1738u),
        new((GameAction)12, 1739u),
        new((GameAction)13, 1740u),
        new((GameAction)14, 124u),
        new((GameAction)15, 125u),
        new((GameAction)16, 118u),
        new((GameAction)17, 119u),
        new((GameAction)18, 106u),
        new((GameAction)19, 107u),
        new((GameAction)20, 120u),
        new((GameAction)21, 121u),
        new((GameAction)22, 122u),
        new((GameAction)23, 123u),
        new((GameAction)24, 1727u),
        new((GameAction)25, 1728u),
        new((GameAction)26, 1744u),
        new((GameAction)27, 2831u),
        new((GameAction)58, 2830u),
        new((GameAction)59, 2832u),
        new((GameAction)60, 2833u),
        new((GameAction)61, 2834u),
        new((GameAction)62, 2835u),
        new((GameAction)63, 2848u),
        new((GameAction)28, 1749u),
        new((GameAction)29, 1750u),
        new((GameAction)30, 1741u),
        new((GameAction)31, 1742u),
        new((GameAction)32, 1743u),
        new((GameAction)33, 1486u),
        new((GameAction)34, 1487u),
        new((GameAction)35, 1488u),
        new((GameAction)36, 1751u),
        new((GameAction)37, 126u),
        new((GameAction)38, 1745u),
        new((GameAction)39, 1298u),
        new((GameAction)40, 1752u),
        new((GameAction)41, 533u),
        new((GameAction)42, 1299u),
        new((GameAction)43, 1746u),
        new((GameAction)44, 1747u),
        new((GameAction)45, 1761u),
        new((GameAction)46, 1762u),
        new((GameAction)47, 1757u),
        new((GameAction)48, 1758u),
        new((GameAction)49, 1756u),
        new((GameAction)50, 1755u),
        new((GameAction)51, 1759u),
        new((GameAction)52, 1760u),
        new((GameAction)53, 1763u),
        new((GameAction)54, 1764u),
        new((GameAction)55, 1753u),
        new((GameAction)56, 1754u),
        new((GameAction)57, 1269u),
    });
    public static bool IsBuilt(GameAction action) => action is >= GameAction.ZoomIn and <= GameAction.RotateRight
        or GameAction.Options or GameAction.ObjectsOnly or GameAction.AmbientUp or GameAction.AmbientDown
        or GameAction.NextFight or GameAction.TogglePanel or GameAction.Heart or GameAction.FullMap
        or GameAction.Portal or GameAction.MouseRotate or GameAction.MouseZoom;
}
