// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

public readonly record struct GraveyardBurialResult(
    int DecomposeValue, int SpawnedCreatureId, byte SpawnedCreatureTypeId);

/// <summary>Per-room corpse intake and Vampire creation for Graveyards.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/creature-death.md")]
public static class GraveyardRules
{
    /// <summary>Removes tallies whose anchor is no longer the anchor of a
    /// Graveyard owned by the same player. The identity policy is ADR-0151.</summary>
    public static void Reconcile(WorldState world, GameDatabase database)
    {
        var current = new HashSet<(int Anchor, byte Owner)>();
        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (room.Definition.TypeId != RoomTypeId.Graveyard) continue;
            current.Add((room.TileIndices[0], room.OwnerPlayerId));
        }

        world.RawGraveyards.RemoveAll(
            state => !current.Contains((state.AnchorTileIndex, state.OwnerPlayerId)));
    }

    public static GraveyardBurialResult Bury(
        WorldState world, GameDatabase database, RoomInstance graveyard,
        int decomposeValue, int burialTileIndex)
    {
        Reconcile(world, database);
        GraveyardState state = world.GraveyardAt(
            graveyard.TileIndices[0], graveyard.OwnerPlayerId);
        state.Add(decomposeValue);

        int needed = database[VariableTypeId.DecomposeValueNeededForVampire];
        byte creatureTypeId = graveyard.Definition.CreatedCreatureId;
        if (needed <= 0 || state.DecomposeValue < needed || creatureTypeId == 0)
            return new GraveyardBurialResult(state.DecomposeValue, -1, 0);

        // UNKNOWN in DK2: ours spends one threshold and raises at most one creature
        // per burial, at the tile which received the last corpse (Q318-Q320).
        state.Spend(needed);
        CreatureState creature = world.SpawnCreature(
            database, creatureTypeId, graveyard.OwnerPlayerId,
            new(burialTileIndex % world.Width, burialTileIndex / world.Width));
        return new GraveyardBurialResult(state.DecomposeValue, creature.Id, creatureTypeId);
    }
}
