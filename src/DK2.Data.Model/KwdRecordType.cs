// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>
/// Top-level record types of the KWD/KLD container format.
///
/// CONFIRMED twice over:
///  - OpenKeeper `MapDataTypeEnum` and ufdada/dk2-tools binary templates agree.
///  - Verified directly against this project's reference installation:
///    `Data/editor/Creatures.kwd` begins 0xAA (=170, Creatures) and
///    `Data/editor/GlobalVariables.kwd` begins 0xE6 (=230, Variables).
///    (docs/research/kwd-container.md)
/// </summary>
public enum KwdRecordType
{
    Globals = 0,
    Map = 100,
    Terrain = 110,
    Rooms = 120,
    Traps = 130,
    Doors = 140,
    KeeperSpells = 150,
    CreatureSpells = 160,
    Creatures = 170,
    Players = 180,
    Things = 190,
    Triggers = 210,
    Level = 220,
    Variables = 230,
    Objects = 240,
    EffectElements = 250,
    Shots = 260,
    Effects = 270,
}
