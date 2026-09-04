// Copyright (c) 2026 Franjo Misetic
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

namespace DK2.Data.Model;

/// <summary>
/// DK2's room type identifiers.
///
/// Base list CONFIRMED from Flame `src/dk2/room_type.h` (derived from DKII.EXE
/// v1.70). Flame left ids 2, 7, 18, 19, 20, 22 and 23 as `unk_N`; those names
/// are filled in here from the reference install's own Rooms.kwd, where each
/// record carries its name string. Verified by
/// `DK2.Data.Tests.KwdTerrainRoomsTests.RoomIds_MatchAndExtendFlameNaming`.
/// See docs/research/kwd-terrain-rooms.md.
/// </summary>
public enum RoomTypeId : byte
{
    Treasury = 1,
    Lair = 2,                    // Flame: unk_2 — named from Rooms.kwd
    Portal = 3,                  // Flame calls this Entrance
    Hatchery = 4,
    DungeonHeart = 5,
    Library = 6,
    TrainingRoom = 7,            // Flame: unk_7 — named from Rooms.kwd
    WoodenBridge = 8,
    GuardRoom = 9,               // Flame calls this GuardPost
    Workshop = 10,
    Prison = 11,
    TortureChamber = 12,
    Temple = 13,
    Graveyard = 14,
    Casino = 15,
    CombatPit = 16,
    StoneBridge = 17,
    HeroGateFinal = 18,          // Flame: unk_18
    HeroGateTile = 19,           // Flame: unk_19 (marked "not used" in data)
    HeroGate2By2 = 20,           // Flame: unk_20
    HeroGateFrontEnd = 21,
    HeroStoneBridge = 22,        // Flame: unk_22
    HeroGate3By1 = 23,           // Flame: unk_23
    MercenaryGate = 24,          // marked "not used" in data
    HeroPortal = 25,
    Crypt = 26,
}
