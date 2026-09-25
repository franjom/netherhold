// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>The condition types a creature's trigger tree sees as <b>events</b>: DK2 fires
/// them through the creature event filter <c>0x0049AA30(creature, type, 0, 0)</c> at the
/// moment of the event, and the filter runs the children of every active top-level record
/// of that type. Names are the level editor's; numbers are the record's type byte; the
/// firing site of each is tabled in trigger-walkers.md, "Who fires what". <see cref="Leaves"/>
/// has no site in DKII.EXE and no shipped record.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/trigger-walkers.md")]
public enum CreatureTriggerEvent : byte
{
    Created = 3,
    Killed = 4,
    Slapped = 5,
    Attacked = 6,
    Imprisoned = 7,
    Tortured = 8,
    Converted = 9,
    Claimed = 10,
    Angry = 11,
    Afraid = 12,
    Steals = 13,
    Leaves = 14,
    Stunned = 15,
    Dying = 16,
    HungerSated = 54,
    Sacked = 63,
    PickedUp = 65,
}

/// <summary>The condition types a player's tree sees as events, fired through the player event
/// filter <c>0x004C7A50(player, type, targetType, targetId)</c>; the pair matters only to
/// <see cref="GuiButtonPressed"/>, whose record must carry the same two bytes. Sites in
/// trigger-walkers.md, "Who fires what".</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/trigger-walkers.md")]
public enum PlayerTriggerEvent : byte
{
    PayDay = 46,
    GuiButtonPressed = 53,
    DungeonBreached = 56,
    EnemyBreached = 57,
    CreaturePickedUp = 58,
    CreatureDropped = 59,
    CreatureSlapped = 60,
    CreatureSacked = 61,
}
