// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Media;

namespace DK2.Presentation;

/// <summary>A widget's sound slot: the record number of the GUI sound table <c>0x006AC8C4</c>
/// (docs/research/sound-effects-interface.md section 1). Only the slots our widgets use are named; zero is the table's silent record.</summary>
public enum GuiSoundSlot : byte
{
    None = 0, TabRooms = 1, TabSpells = 2, TabWorkshop = 3, TabCreature = 4, Icon = 5, IconLarge = 6, Size = 7,
}

public static class GuiSoundSlots
{
    /// <summary>The category the table names for the slot; <see cref="GuiSoundSlot.None"/> has none, so a request in it plays nothing.</summary>
    public static SoundCategory CategoryOf(GuiSoundSlot slot) => slot switch
    {
        GuiSoundSlot.TabRooms => new SoundCategory("gui_button_tab_rooms"),
        GuiSoundSlot.TabSpells => new SoundCategory("gui_button_tab_spells"),
        GuiSoundSlot.TabWorkshop => new SoundCategory("gui_button_tab_workshop"),
        GuiSoundSlot.TabCreature => new SoundCategory("gui_button_tab_creature"),
        GuiSoundSlot.Icon => new SoundCategory("gui_button_icon"),
        GuiSoundSlot.IconLarge => new SoundCategory("gui_button_icon_large"),
        GuiSoundSlot.Size => new SoundCategory("gui_button_size"),
        _ => SoundCategory.None,
    };

    /// <summary>The tab of a bottom-panel category (tabs carry slots 1 to 4: Rooms, Spells, Workshop, Creatures).</summary>
    public static GuiSoundSlot OfTab(BottomPanelCategory category) => category switch
    {
        BottomPanelCategory.Rooms => GuiSoundSlot.TabRooms,
        BottomPanelCategory.Spells => GuiSoundSlot.TabSpells,
        BottomPanelCategory.Workshop => GuiSoundSlot.TabWorkshop,
        BottomPanelCategory.Creatures => GuiSoundSlot.TabCreature,
        _ => GuiSoundSlot.None,
    };
}
