// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Commands;

/// <summary>What a GUI button's id names: the FlashButton target's category byte (tab-alerts.md).</summary>
public enum GuiButtonCategory : byte { Widget = 0, Room = 1, Creature = 2, Door = 3, Trap = 4, Spell = 5 }

/// <summary>A GUI button: a category and an id within it, a room, door, trap or spell id, or for
/// <see cref="GuiButtonCategory.Widget"/> the pressed HUD widget's <c>+0x74</c> (record dword +1).</summary>
public readonly record struct GuiButtonTarget(GuiButtonCategory Category, byte Id)
{
    public static GuiButtonTarget Room(RoomTypeId room) => new(GuiButtonCategory.Room, (byte)room);
    public static GuiButtonTarget Door(byte doorId) => new(GuiButtonCategory.Door, doorId);
    public static GuiButtonTarget Trap(byte trapId) => new(GuiButtonCategory.Trap, trapId);
    public static GuiButtonTarget Spell(byte spellId) => new(GuiButtonCategory.Spell, spellId);

    /// <summary>The Sell tool (record <c>0x690ED8</c>, <c>0x004163F0</c>).</summary>
    public static readonly GuiButtonTarget Sell = new(GuiButtonCategory.Widget, 0xFF);
    /// <summary>The query tool (record <c>0x690E88</c>, <c>0x0041A2B0</c>).</summary>
    public static readonly GuiButtonTarget Query = new(GuiButtonCategory.Widget, 1);
    /// <summary>The minimap magnifier, either click (record <c>0x690F28</c>: primary <c>0x00416480</c>
    /// zooms in, secondary <c>0x00416580</c> out).</summary>
    public static readonly GuiButtonTarget MinimapZoom = new(GuiButtonCategory.Widget, 0xFD);
    /// <summary>The Resize Panel button (record <c>0x690D98</c>, <c>0x004167D0</c>).</summary>
    public static readonly GuiButtonTarget ResizePanel = new(GuiButtonCategory.Widget, 0xF7);
    /// <summary>The tabs (<c>0x00416C20</c>, records <c>0x690C58</c>-<c>0x690D48</c>).</summary>
    public static readonly GuiButtonTarget CreaturesTab = new(GuiButtonCategory.Widget, 0xF8),
        RoomsTab = new(GuiButtonCategory.Widget, 0xFB), SpellsTab = new(GuiButtonCategory.Widget, 0xFA),
        WorkshopTab = new(GuiButtonCategory.Widget, 0xF9);
}

/// <summary>
/// A HUD button was pressed: DK2's game action 0x72, which <c>0x00422600</c> posts from the HUD's
/// click callbacks and whose handler <c>0x00513EB0</c> fires player trigger event 53 with the
/// target (<see cref="PlayerTriggerEvent.GuiButtonPressed"/>, ADR-0670). It travels in the turn's
/// packet like any other command, so scripts see it on that turn.
/// </summary>
public sealed record GuiButtonCommand(PlayerId PlayerId, GuiButtonTarget Target) : PlayerCommand(PlayerId)
{
    public override void HashInto(ref Hash64 hash)
    {
        base.HashInto(ref hash);
        hash.Add((byte)Target.Category);
        hash.Add(Target.Id);
    }
}
