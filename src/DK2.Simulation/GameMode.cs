// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation;

/// <summary>The kind of game being played: DK2's mode byte <c>[0x0075AD4B]</c>, which the front end
/// sets before the level loads (front-end-menus.md). The verdict reads it: the last keeper
/// standing wins only outside the campaign (<c>0x004BB39A</c>), and allies end games together, and
/// a lapsed time limit ends every keeper's, only in skirmish and multiplayer (win-and-defeat.md §7).</summary>
public enum GameMode : byte
{
    Campaign = 1,
    Skirmish = 2,
    Multiplayer = 3,
    MyPetDungeon = 4,
}
