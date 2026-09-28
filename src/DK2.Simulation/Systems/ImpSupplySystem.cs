// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Systems;

/// <summary>Makes imps: free ones to keep a keeper above the floor, bought ones when
/// a keeper casts Create Imp.</summary>
/// <remarks>The free supply is CONFIRMED and the data names it twice: a keeper below
/// <c>MinimumImpThreshold</c> (4) gets one imp every
/// <c>TimeBeforeFreeImpGeneratedSeconds</c> (5) until they are back up to it, and the
/// owner established the behaviour by dropping every imp into a dungeon heart and
/// watching them return at roughly five-second intervals. Not implemented: imps cost
/// mana to keep, about 7 a second each — the red figure under a keeper's mana orb —
/// which is in neither the balance table nor the CREATURES record, so it is recorded
/// rather than hardcoded (Q53, docs/research/imps-and-mana.md). Until it lands, a
/// dungeon full of imps is free to run here and is not in the original.</remarks>
[Dk2Behavior(Confidence.Confirmed, "docs/research/imps-and-mana.md")]
public sealed class ImpSupplySystem : ISimulationSystem
{
    private const string SystemName = "ImpSupply";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        foreach (PlayerCommand command in context.Input.All)
        {
            if (command is CreateImpCommand create) Buy(context, create.PlayerId, create.At);
        }

        TopUp(context);
    }

    /// <summary>Hands a short-handed keeper a free imp when one is due.</summary>
    /// <remarks>The due time is a deadline and it moves only when an imp is made:
    /// DK2 keeps one field on the keeper — <see cref="PlayerState.NextFreeImpTick"/> —
    /// and its keeper update writes it in exactly one place, right after the spawn, as
    /// <c>now + TimeBeforeFreeImpGeneratedSeconds</c>. A keeper at full strength is
    /// never touched, so the deadline slides into the past and the next loss is
    /// replaced on the spot: the owner filmed thirty seconds at exactly four imps,
    /// sacrificed one, and the replacement came in about 1.4 seconds rather than 5,
    /// with the following ones five apart (Q56). Two more details are the original's:
    /// the deadline advances whether or not the spawn succeeds, since the original
    /// does not test the cast's return value; and a free imp skips the
    /// spell-availability check, the mana check and the deduction.</remarks>
    private static void TopUp(SimulationContext context)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        int minimum = ImpSupplyRules.MinimumImps(database);
        int interval = ImpSupplyRules.FreeImpIntervalTicks(database);
        GameTick now = context.Tick;

        for (int i = 0; i < world.PlayerCount; i++)
        {
            ref PlayerState player = ref world.PlayerAtIndex(i);
            byte playerId = player.PlayerId;

            // Only a keeper is topped up (Q91). Nobody, the hero side and the
            // neutral player own real terrain but keep no imp-staffed dungeon -
            // a rule nothing else enforces, so a level that gave the hero side a
            // heart would have staffed it. The deadline is deliberately NOT
            // cleared for anyone declined, which is what makes a full-strength
            // keeper's next replacement immediate.
            if (!PlayerIds.IsKeeper(playerId)) continue;

            if (ImpSupplyRules.ImpCount(world, database, playerId) >= minimum) continue;

            if (now < player.NextFreeImpTick) continue;

            player.NextFreeImpTick = new GameTick(now.Value + interval);

            // No heart, nowhere to put it — so nothing happens and the interval
            // is burned anyway, because the original advances the deadline
            // without looking at whether the cast worked. A keeper without a
            // heart has lost (Q40); what this avoids is searching the map for
            // one on every tick.
            if (Spawn(context, playerId) is not { } imp) continue;

            context.Emit(SystemName, "imp_generated",
                $"player={playerId} imp={imp.Id} at=({imp.Tile.X},{imp.Tile.Y}) " +
                $"free (below MinimumImpThreshold {minimum})");
        }
    }

    /// <summary>
    /// Casts Create Imp. All or nothing: a keeper who cannot afford it gets
    /// nothing and is told why, because a half-paid imp would be a worse bug
    /// than a refused one — the same rule <see cref="GoldStore.TryWithdraw"/>
    /// follows for rooms.
    /// </summary>
    private static void Buy(SimulationContext context, byte playerId, TilePos? at)
    {
        WorldState world = context.World;
        if (!world.HasPlayer(playerId))
        {
            context.Emit(SystemName, "create_imp_refused",
                $"player={playerId} reason=no_such_player");
            return;
        }

        // Create Imp is a keeper spell, so a non-keeper cannot cast it however
        // much mana the id happens to be holding (Q91). Refused before the cost
        // is even looked up, and refused *distinctly* from "no such player":
        // Neutral exists, owns terrain, and still may not have imps.
        if (!PlayerIds.IsKeeper(playerId))
        {
            context.Emit(SystemName, "create_imp_refused",
                $"player={playerId} reason=not_a_keeper");
            return;
        }

        int cost = ImpSupplyRules.CreateImpCost(world, context.Database, playerId);
        ref PlayerState player = ref world.Player(playerId);

        if (player.Mana < cost)
        {
            context.Emit(SystemName, "create_imp_refused",
                $"player={playerId} cost={cost} mana={player.Mana} reason=not_enough_mana");
            return;
        }

        if (Spawn(context, playerId, at) is not { } imp)
        {
            context.Emit(SystemName, "create_imp_refused",
                $"player={playerId} cost={cost} reason={(at is null ? "no_dungeon_heart" : "outside_the_map")}");
            return;
        }

        player.Mana -= cost;
        context.Emit(SystemName, "imp_created",
            $"player={playerId} imp={imp.Id} at=({imp.Tile.X},{imp.Tile.Y}) " +
            $"cost={cost} mana={player.Mana}");
    }

    /// <summary>Puts a new imp on the cast's tile, or on this keeper's dungeon heart when the
    /// cast names none (free imps and <see cref="CreateImpCommand"/> without a tile). Returns
    /// null for a tile outside the map, or with no tile, for a keeper without a heart.</summary>
    /// <remarks>The type is found by name, <c>Imp</c> — not by id, and not by the
    /// worker flag, because the Dwarf carries it too and is not what Create Imp
    /// makes.</remarks>
    private static CreatureState? Spawn(SimulationContext context, byte playerId, TilePos? at = null)
    {
        WorldState world = context.World;
        TilePos tile;
        if (at is { } cast)
        {
            if (!world.Contains(cast)) return null;
            tile = cast;
        }
        else
        {
            int heart = ImpSupplyRules.HeartTileOf(world, context.Database, playerId);
            if (heart < 0) return null;
            tile = new TilePos(heart % world.Width, heart / world.Width);
        }

        CreatureDef? imp = context.Database.CreatureByName("Imp");
        if (imp is null) return null;

        CreatureState made = world.SpawnCreature(context.Database, imp.CreatureId, playerId, tile);

        // The shot's second datum is the level: 1 normally, 4 once research has upgraded
        // Create Imp (bonus shot 60, data (1, 4)). Free imps take the same cast and the same
        // branch (0x004BBD30), so they arrive upgraded too (research-tree.md §5).
        int level = ImpLevel(context, playerId);
        if (level != made.Level)
        {
            made.Level = level;
            made.Health = CreatureLevelRules.MaxHealth(context.Database, imp, level);
        }

        return made;
    }

    /// <summary>The level a Create Imp cast makes for <paramref name="playerId"/>: the second
    /// datum of the spell's shot, or of its bonus shot when the keeper's row is upgraded. Level
    /// 1 for a database without the spell.</summary>
    public static int ImpLevel(SimulationContext context, byte playerId)
    {
        if (context.Database.KeeperSpell(KeeperSpellName.CreateImp) is not { } spell)
            return CreatureLevelRules.FirstLevel;

        bool upgraded = context.World.IsUpgraded(
            playerId, ResearchTreeRules.AvailabilityKindOf(ResearchTreeRules.KindSpell), spell.KeeperSpellId);
        return CreatureLevelRules.Clamp(spell.Shot(upgraded).Data2);
    }
}
