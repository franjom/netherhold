// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>A creature these tests place as if it had just stepped onto its tile, so its first fight
/// scan is due at once. A creature the game makes holds its creation tile instead (<c>0x004872F0</c>)
/// and waits for its scan phase (ADR-0645); the combat tests are about what a scan finds, not when.</summary>
internal static class ArrivedCreatures
{
    public static CreatureState SpawnArrived(
        this WorldState world, GameDatabase database, byte creatureTypeId, PlayerId ownerPlayerId, TilePos at)
    {
        CreatureState creature = world.SpawnCreature(database, creatureTypeId, ownerPlayerId, at);
        creature.LastFightScanTileIndex = -1;
        return creature;
    }
}
