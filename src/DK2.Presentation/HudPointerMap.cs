// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary>The in-game HUD's panels that keep the pointer from the world, in the order <see cref="HudPointerMap"/> asks them.</summary>
public enum HudPointerPanel : byte
{
    Minimap,
    HoverHud,
    ResourceStrip,
    GameOptions,
    BottomPanel,
    MessageTabs,
    BattleTab,
    CreatureOverview,
    OptionsMenu,
    /// <summary>Asked last, and only behind <see cref="HudPointerMap.Over{TPanels}"/>'s query gate.</summary>
    CreatureQuery,
}

/// <summary>An adapter's hit test for one of its panels at the pointer it is asking about; a panel it does not have, or has
/// not built yet, answers false. Implement it on a struct holding the adapter and the pointer, so a query does not allocate.</summary>
public interface IHudPointerPanels
{
    bool Contains(HudPointerPanel panel);
}

/// <summary>Whether the pointer is over the in-game HUD rather than the world: any panel under it, the open full map (which
/// holds the whole screen), or the creature query details while a creature is queried and no menu is modal. Unity asks it
/// for world hover, world clicks and the camera's wheel; Godot, with no 3D world yet, only for the HUD's hover text.</summary>
public static class HudPointerMap
{
    public static bool Over<TPanels>(TPanels panels, bool fullMapVisible, bool queryHasTarget, bool modal)
        where TPanels : IHudPointerPanels
    {
        for (HudPointerPanel panel = HudPointerPanel.Minimap; panel < HudPointerPanel.CreatureQuery; panel++)
            if (panels.Contains(panel)) return true;
        if (fullMapVisible) return true;
        return queryHasTarget && !modal && panels.Contains(HudPointerPanel.CreatureQuery);
    }
}
