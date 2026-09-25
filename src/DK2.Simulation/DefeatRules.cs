// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;

namespace DK2.Simulation;

/// <summary>What a defeat does — DK2's general consequence arm <c>0x004DAA10</c> (win-and-defeat.md
/// §7), run by the verdict tail every turn a killer is stamped. On the death turn the heart's gold
/// becomes neutral floor piles where it lay, every room but the heart, the hero gates and the hero
/// bridge goes to the neutral player, and claimed ground with no room falls to its destroyed terrain.
/// From the fifth turn on, each turn, a non-worker of the loser is sent leaving and a worker is
/// killed. Not built: the "Dungeon Heart Obstacle" object DK2 adds to each kept room (role unread),
/// the prisoner arm (a virtual call, unread), and the local player's own collapse sequence
/// <c>0x004DAE40</c>, which spawns Destructors instead (research only, §7).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/win-and-defeat.md")]
public static class DefeatRules
{
    private const string SystemName = "defeat";

    /// <summary><c>died + 4 &lt; now</c>: the creatures go from the fifth turn after the death.</summary>
    public const int CreatureDelayTurns = 5;

    /// <summary>The rooms a fallen keeper keeps: the heart — its ruin, ADR-0162 — and room ids 18 to
    /// 23, the hero gates and the hero stone bridge. Everything else, portals included, goes neutral.</summary>
    public static bool IsKept(RoomDef room) =>
        room.RoomId == (byte)RoomTypeId.DungeonHeart
        || (room.RoomId >= (byte)RoomTypeId.HeroGateFinal && room.RoomId <= (byte)RoomTypeId.HeroGate3By1);

    /// <summary>Whether the fallen keeper's consequences run this tick for a heart that died on
    /// <paramref name="died"/>: the death turn itself, and every turn from the fifth after it.</summary>
    public static bool IsDeathTurn(GameTick now, GameTick died) => now.Value == died.Value;

    public static bool CreaturesGo(GameTick now, GameTick died) => now.Value > died.Value + CreatureDelayTurns - 1;

    /// <summary>The death turn's arm, guarded as DK2 guards it: only while the heart tile still
    /// carries a heart room. Gold first (<c>0x004F38C0</c>: one neutral pile per pile, the keeper
    /// debited), then the rooms to neutral (<c>0x004E5650</c>), then every owned tile without a room
    /// to its destroyed terrain owned by neutral (<c>0x0044FC40</c>; an unownable result carries no
    /// owner, ADR-0045). Reinforced walls therefore fall to rock and claimed paths to dirt.</summary>
    public static void OnDeathTurn(SimulationContext context, byte playerId)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        if (ImpSupplyRules.HeartTileOf(world, database, playerId) < 0) return;
        byte heartTerrain = ManaRules.DungeonHeartTerrainId(database)!.Value;

        int gold = 0, piles = 0;
        for (int i = 0; i < world.TileCount; i++)
        {
            ref TileState tile = ref world.TileAtIndex(i);
            if (tile.OwnerPlayerId != playerId || tile.TerrainId != heartTerrain || tile.GoldHeld <= 0) continue;
            int amount = tile.GoldHeld;
            tile.GoldHeld = 0;
            FloorGold.Drop(world, database, i, amount, PlayerIds.Neutral);
            gold += amount;
            piles++;
        }

        int rooms = 0;
        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (room.OwnerPlayerId != playerId || IsKept(room.Definition)) continue;
            foreach (int index in room.TileIndices)
            {
                ref TileState tile = ref world.TileAtIndex(index);
                tile.OwnerPlayerId = PlayerIds.Neutral;
                if (PortalRules.IsPortalTerrain(database, tile.TerrainId)) PortalRules.OnOwnerChanged(ref tile);
            }
            rooms++;
        }

        int fallen = 0;
        for (int i = 0; i < world.TileCount; i++)
        {
            ref TileState tile = ref world.TileAtIndex(i);
            if (tile.OwnerPlayerId != playerId) continue;
            TerrainDef terrain = database.TerrainById(tile.TerrainId);
            if (terrain.IsRoom) continue;
            TerrainDef next = database.TerrainById(terrain.DestroyedTypeTerrainId);
            tile.TerrainId = terrain.DestroyedTypeTerrainId;
            tile.Health = next.StartingHealth;
            tile.BurnTicksRemaining = 0;
            if (!next.IsTaggable) tile.TaggedBy = 0;
            tile.OwnerPlayerId = next.IsOwnable ? PlayerIds.Neutral : PlayerIds.Unowned;
            fallen++;
        }

        context.Emit(SystemName, "dungeon_fell",
            $"player={playerId} gold_scattered={gold} piles={piles} rooms_to_neutral={rooms} tiles_fallen={fallen}");
    }

    /// <summary>The creature arm, each turn from the fifth: a creature already down, held, or in a
    /// prison is left alone; a worker is killed outright with the killer as source; anyone else not
    /// already leaving drops its fight, job and bed and sets off for the nearest entrance of
    /// anyone (<see cref="LeavingRules"/>), the loser's own having gone neutral on the death turn.</summary>
    public static void ScatterCreatures(SimulationContext context, byte playerId)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        for (int i = world.Creatures.Count - 1; i >= 0; i--)
        {
            CreatureState state = world.Creatures[i];
            if (state.OwnerPlayerId != playerId) continue;
            if (CreatureDeathRules.IsDown(state) || HandRules.IsHeld(state) || PrisonRules.IsImprisoned(state)) continue;
            CreatureDef creature = database.CreatureById(state.CreatureTypeId);

            if (creature.Flags.HasFlag(CreatureFlags.IsWorker))
            {
                CreatureHealthRules.Apply(database, creature, state, -state.Health);
                context.Emit(SystemName, "worker_killed", $"creature={state.Id} type={creature.Name} player={playerId}");
                continue;
            }

            if (state.Activity != CreatureActivity.Leaving)
            {
                if (!LeavingRules.Start(context, state, SystemName)) continue;
                CombatRules.Disengage(world, state);
                state.ClearJob();
                LairRules.ReleaseBed(world, database, state);
                state.Activity = CreatureActivity.Leaving;
                context.Emit(SystemName, "creature_leaving", $"creature={state.Id} type={creature.Name} player={playerId}");
            }

            LeavingRules.Step(context, creature, state, SystemName, out _);
        }
    }
}
