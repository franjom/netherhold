// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Systems;

/// <summary>
/// Makes imps: free ones to keep a keeper above the floor, bought ones when a
/// keeper casts Create Imp.
///
/// <para>This closes a gap every scenario run has reported since workers
/// existed — "nothing generates imps: scenario files and tests place them" — and
/// it is the first thing in the simulation that <b>spends</b> mana rather than
/// only earning it.</para>
///
/// <para><b>The free supply is CONFIRMED and the data names it twice.</b> A
/// keeper below <c>MinimumImpThreshold</c> (4) gets one imp every
/// <c>TimeBeforeFreeImpGeneratedSeconds</c> (5) until they are back up to it.
/// The owner established the behaviour by dropping every imp into a dungeon
/// heart and watching them come back at roughly five-second intervals; the two
/// variables carry exactly those numbers and say exactly that in their names.
/// It is rare for an observation and a variable name to meet this cleanly.</para>
///
/// <para><b>Nothing here is ours any more, and Q56 is why that sentence exists.</b>
/// This class used to hold a countdown that was pinned to zero while a keeper
/// was up to strength, on the reasoning that the alternative "differs only in
/// how fast the first replacement arrives after a long spell at full strength".
/// That difference is precisely what the owner filmed on 2026-09-05, and the
/// original does the other thing: it keeps a <b>deadline</b> that only ever
/// moves when an imp is made, so the first replacement after a long spell at
/// four arrives with no wait at all. See <see cref="TopUp"/>, and
/// docs/research/imps-and-mana.md for the read.</para>
///
/// <para><b>Not implemented, and it is the interesting one:</b> imps cost mana
/// to keep — about 7 a second each, which is what the red figure under a
/// keeper's mana orb is. That number is in neither the balance table nor the
/// CREATURES record, so it is recorded rather than hardcoded. See Q53 and
/// docs/research/imps-and-mana.md; until it lands, a dungeon full of imps is
/// free to run here and is not in the original.</para>
/// </summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/imps-and-mana.md")]
public sealed class ImpSupplySystem : ISimulationSystem
{
    private const string SystemName = "ImpSupply";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        foreach (PlayerCommand command in context.Input.All)
        {
            if (command is CreateImpCommand create) Buy(context, create.PlayerId);
        }

        TopUp(context);
    }

    /// <summary>
    /// Hands a short-handed keeper a free imp when one is due.
    ///
    /// <para><b>The due time is a deadline, and it moves only when an imp is
    /// made.</b> DK2 keeps one field on the keeper — read here as
    /// <see cref="PlayerState.NextFreeImpTick"/> — and the original's keeper
    /// update writes it in exactly one place, right after the spawn, as
    /// <c>now + TimeBeforeFreeImpGeneratedSeconds</c>. A keeper at full strength
    /// is never touched, so their deadline slides into the past and the next
    /// loss is replaced <b>on the spot</b>.</para>
    ///
    /// <para><b>This is a correction, and it was caught by watching.</b> Q56 was
    /// answered "ours" for three weeks: a countdown pinned to zero while the
    /// keeper was up to strength, restarted from full when they fell short. The
    /// owner recorded thirty seconds at exactly four imps, sacrificed one, and
    /// the replacement appeared in about 1.4 seconds rather than 5 — and only
    /// the further replacements came five seconds apart. The binary then said
    /// why. Both halves of the old comment were wrong: the alternative was not
    /// "indistinguishable in the steady state", it was the observable, and the
    /// thing it differed on was the case the owner filmed.</para>
    ///
    /// <para>Two details are deliberate and both are read out of the original.
    /// The deadline is advanced <b>whether or not the spawn succeeds</b> — the
    /// original does not test the cast's return value — so a keeper with no
    /// heart burns its interval rather than retrying every tick. And a free imp
    /// is genuinely free: the free flag makes the original skip both the
    /// spell-availability check and the mana check, and skip the deduction.</para>
    /// </summary>
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
            // neutral player are all real player ids that own real terrain —
            // 167 of the campaign's portals belong to Neutral — and none of
            // them keeps a dungeon reinforced by imps.
            //
            // This was harmless on shipped data because none of the three has a
            // dungeon heart, so Spawn returned null and the clock simply ran
            // again. Harmless-by-accident is the shape of defect this project
            // has been bitten by before: the rule holds because of a fact about
            // the levels rather than because anything checks it, and a level
            // that gave the hero side a heart would have quietly staffed it.
            //
            // The deadline is NOT cleared here, and that is the correction: the
            // original leaves it alone for everybody it declines to serve, which
            // is exactly why a keeper who has been at full strength replaces
            // their next loss immediately.
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
    private static void Buy(SimulationContext context, byte playerId)
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

        if (Spawn(context, playerId) is not { } imp)
        {
            context.Emit(SystemName, "create_imp_refused",
                $"player={playerId} cost={cost} reason=no_dungeon_heart");
            return;
        }

        player.Mana -= cost;
        context.Emit(SystemName, "imp_created",
            $"player={playerId} imp={imp.Id} at=({imp.Tile.X},{imp.Tile.Y}) " +
            $"cost={cost} mana={player.Mana}");
    }

    /// <summary>
    /// Puts a new imp on this keeper's dungeon heart, or returns null if they
    /// have not got one.
    ///
    /// The creature type is found by flag rather than by id — the same rule
    /// every other creature lookup follows — and by name among the workers,
    /// because the Dwarf is a worker too and is not what Create Imp makes.
    /// </summary>
    private static CreatureState? Spawn(SimulationContext context, byte playerId)
    {
        WorldState world = context.World;
        int heart = ImpSupplyRules.HeartTileOf(world, context.Database, playerId);
        if (heart < 0) return null;

        CreatureDef? imp = context.Database.CreatureByName("Imp");
        if (imp is null) return null;

        return world.SpawnCreature(
            context.Database, imp.CreatureId, playerId,
            new TilePos(heart % world.Width, heart / world.Width));
    }
}
