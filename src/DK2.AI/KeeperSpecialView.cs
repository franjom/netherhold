// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.AI;

/// <summary>One of the keeper's own objects whose definition carries <c>TypeSpecial</c>, as the
/// specials step walks them (<c>0x00500520</c>): its tile and whether a creature holds it
/// (object <c>+0x146</c> bit 1, set by take-hold <c>0x0049C250</c>, cleared by the drop <c>0x004B7260</c>).</summary>
public readonly record struct KeeperSpecialView(int ObjectId, byte ObjectTypeId, int X, int Y, bool Carried);
