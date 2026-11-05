// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Presentation;

/// <summary>The GUI button a bottom-panel click posts as DK2's action 0x72 (<c>0x00422600</c>), which
/// fires player trigger event 53 (ADR-0670).</summary>
public static class GuiButtonPairs
{
    /// <summary>A primary click that selects: the room panel's <c>0x00410860</c> (not for a room at
    /// availability 4, which clears the tool), the spell panel's <c>0x00411140</c>, the Workshop's
    /// <c>0x00410BC0</c> (door 3, trap 4).</summary>
    public static GuiButtonTarget? ForSelection(BottomPanelChoice choice) =>
        ForSelection(choice.Kind, choice.Key.Value, choice.ClearsToolOnClick);

    public static GuiButtonTarget? ForSelection(BottomPanelChoiceKind kind, byte id, bool clearsTool) => kind switch
    {
        BottomPanelChoiceKind.Room when !clearsTool => GuiButtonTarget.Room((RoomTypeId)id),
        BottomPanelChoiceKind.Spell => GuiButtonTarget.Spell(id),
        BottomPanelChoiceKind.Door => GuiButtonTarget.Door(id),
        BottomPanelChoiceKind.Trap => GuiButtonTarget.Trap(id),
        _ => null,
    };

    /// <summary>A tab click (<c>0x00416C20</c>) posts its widget, before any expansion (<c>0x00416C36</c>).</summary>
    public static GuiButtonTarget ForTab(BottomPanelCategory tab) => tab switch
    {
        BottomPanelCategory.Creatures => GuiButtonTarget.CreaturesTab,
        BottomPanelCategory.Rooms => GuiButtonTarget.RoomsTab,
        BottomPanelCategory.Spells => GuiButtonTarget.SpellsTab,
        _ => GuiButtonTarget.WorkshopTab,
    };

    /// <summary>A right click on a visible slot: the room panel's <c>0x004118B0</c> posts the room;
    /// the Workshop's cycles post the trap category for both, the door cycle <c>0x00416150</c> too
    /// (push 4 at <c>0x00416180</c>), so a door of kind k fires the event of trap k. DK2's quirk, kept.</summary>
    public static GuiButtonTarget? ForSecondary(BottomPanelChoice choice) =>
        ForSecondary(choice.Kind, choice.Key.Value);

    public static GuiButtonTarget? ForSecondary(BottomPanelChoiceKind kind, byte id) => kind switch
    {
        BottomPanelChoiceKind.Room => GuiButtonTarget.Room((RoomTypeId)id),
        BottomPanelChoiceKind.Door or BottomPanelChoiceKind.Trap => GuiButtonTarget.Trap(id),
        _ => null,
    };
}
