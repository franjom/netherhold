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
