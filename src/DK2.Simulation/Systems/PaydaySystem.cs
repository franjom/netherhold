// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;

namespace DK2.Simulation.Systems;

/// <summary>Payday: the wage bill falls due, every creature is credited, and each
/// walks to a treasury for its own wages (docs/research/payday.md, ADR-0078).</summary>
/// <remarks>The clock runs before any creature's update (<c>0x0050A7BE</c>, before the sweep at
/// <c>0x0050A7D7</c>) and calls the collect decision as it credits, so a creature credited on this
/// tick sets off and takes its first walk step on this tick; the pay branch of the needs routine
/// and states 187-189 run in each creature's update. Being owed charges <c>AngerNoPay</c>
/// (ADR-0079, <see cref="PaydayRules.IsOwedWages"/>). Ours, none of it changing who gets paid:
/// one activity over states 187-189 (ADR-0180). 187's room, tile and point follow the original's
/// search and its two world draws (ADR-0677).</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/payday.md")]
public sealed class PaydaySystem : ICreatureUpdateStep
{
    private const string SystemName = "Payday";

    private readonly IPathfinder _pathfinder;
    private readonly List<TilePos> _route = [];

    public PaydaySystem() : this(new GridPathfinder()) { }

    public PaydaySystem(IPathfinder pathfinder) =>
        _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));

    public string Name => SystemName;

    public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);

    public void BeginTurn(SimulationContext context) => Clock(context);

    // ------------------------------------------------------------- the clock

    /// <summary>Fires any keeper's payday that has come due, credits its creatures,
    /// and reschedules.</summary>
    /// <remarks>Each keeper carries a deadline
    /// (<see cref="PlayerState.NextPaydayTick"/>), armed by its first non-worker and rewritten as
    /// <c>now + PayDayFrequencySeconds</c> each time it fires. A creature is credited
    /// <c>Pay</c> scaled by its level, added to what it is already owed, and its
    /// failed-attempt budget refills; one holding
    /// <see cref="CreatureState.GetPaid"/> is not credited and spends the
    /// ticket.</remarks>
    private void Clock(SimulationContext context)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        long now = context.Tick.Value;

        for (int p = 0; p < world.PlayerCount; p++)
        {
            ref PlayerState player = ref world.PlayerAtIndex(p);

            // Zero is "no payday": the clock is armed by the keeper's first
            // non-worker creature (PaydayRules.ArmOnGain, 0x004BE330), so a keeper
            // with only imps is never billed.
            if (player.NextPaydayTick.Value == 0) continue;
            if (now < player.NextPaydayTick.Value) continue;

            PlayerId playerId = player.PlayerId;
            int billed = 0;
            int creatures = 0;
            int skipped = 0;

            foreach (CreatureState creature in world.Creatures)
            {
                if (creature.OwnerPlayerId != playerId) continue;
                if (CreatureDeathRules.IsDown(creature)) continue;
                if (CreatureDeathRules.IsCorpse(creature)) continue;

                // A captive is not on anybody's payroll. Ours: the original's
                // loop walks the player's own creature chain and a prisoner has
                // been moved off it, which this project has no equivalent of.
                if (PrisonRules.IsImprisoned(creature)) continue;

                creatures++;

                if (creature.GetPaid)
                {
                    // Spent whether or not it was needed, and no wage is
                    // credited at all - the payday does not happen for this
                    // creature, once.
                    creature.GetPaid = false;
                    skipped++;
                    continue;
                }

                CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
                int wage = PaydayRules.Wage(database, definition, creature.Level);

                // Every creature's budget of attempts refills, including one
                // that gave up last time.
                creature.PayAttemptsFailed = 0;
                if (wage > 0)
                {
                    creature.WageOwed += wage;
                    billed += wage;
                }

                // The clock asks the collect decision itself, with no retry stamp: that is the
                // needs routine's (0x00477F69). Ours keeps the needs routine's other gates.
                if (MayCollect(context, creature)
                    && !NeedRules.IsBusy(world, creature, now)) Decide(context, creature, database);
            }

            player.NextPaydayTick = new GameTick(now + PaydayRules.IntervalTicks(database));
            player.LastPaydayTick = context.Tick;

            context.Emit(SystemName, "payday",
                $"player={playerId} creatures={creatures} billed={billed} " +
                $"skipped={skipped} treasury={GoldStore.Total(world, playerId)} " +
                $"next={player.NextPaydayTick.Value}");
        }
    }

    // --------------------------------------------------------- the collecting

    /// <summary>The pay branch of the needs routine (<c>0x00477C20</c>), after hunger and sleep, and
    /// states 187-189, in the creature's update before its state handler.</summary>
    public void Update(SimulationContext context, CreatureState creature)
    {
        if (creature.Activity == CreatureActivity.SeekingWages)
        {
            if (!CreatureEligibility.IsExcluded(creature, CreatureGuards.Sidelined | CreatureGuards.Imprisoned))
                Walking(context, creature);
            return;
        }
        if (!MayCollect(context, creature)) return;

        // Tried every fourth turn: the stamp moves whether or not anything is owed, and
        // before the busy test (0x00477F69, ADR-0202).
        long now = context.Tick.Value;
        if (now < creature.NextPayTryTick) return;
        creature.NextPayTryTick = now + (NeedRules.RetryTurns * SimulationTiming.OriginalTickPeriodTicks);
        if (NeedRules.IsBusy(context.World, creature, now)) return;

        Decide(context, creature, context.Database);
    }

    private static bool MayCollect(SimulationContext context, CreatureState creature)
    {
        if (CreatureEligibility.IsExcluded(creature, CreatureGuards.Sidelined | CreatureGuards.Imprisoned)) return false;
        if (creature.Activity == CreatureActivity.SeekingWages) return false;

        // A creature walking out for good does not stop to be paid - it is
        // leaving BECAUSE of things like this. Same narrowing the eat and
        // sleep systems make, and ours rather than the original's.
        if (creature.Activity == CreatureActivity.Leaving) return false;
        if (Combat.CombatRules.Preoccupied(creature)) return false;

        // An idle creature sets off, and so does one at a job whose state admits
        // Payday: the needs routine's mask test (NeedRules, ADR-0198).
        return NeedRules.MayAsk(creature, CreatureStateCategory.Payday);
    }

    /// <summary>The collect decision <c>0x00472320</c>.</summary>
    private void Decide(SimulationContext context, CreatureState creature, GameDatabase database)
    {
        // SkipNextPayment is spent by this decision whatever it decides: a set flag, nothing
        // owed or two failures all clear it (0x0047237F, hand-drop-earmark.md §9).
        bool skip = creature.SkipNextPayment;
        creature.SkipNextPayment = false;
        if (skip) return;
        if (creature.WageOwed <= 0) return;
        if (creature.PayAttemptsFailed >= PaydayRules.MaxFailedAttempts) return;

        SetOutForTheTreasury(context, creature, database.CreatureById(creature.CreatureTypeId));
    }

    /// <summary>State 187 (<c>0x00472390</c>, ADR-0677): of the keeper's rooms, newest first, a Treasury or an
    /// unruined Heart with gold whose position (<c>+0x2B</c>) the creature can reach (word 0x80) and is
    /// strictly nearest. Then a random chain tile (<c>0x004E4D90(room, 1, 0)</c>, one draw), followed round
    /// the chain to the first that holds a store pile or nothing at all; <c>random(8)</c> picks one of
    /// eight points in it (<c>0x00698C20</c>) and the walk resumes 188 (word 0xE8). No room with gold is
    /// message 0x37's case, the rest 0x2B's; ours records both as events.</summary>
    private void SetOutForTheTreasury(
        SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        RoomInstance? best = null;
        long bestDistance = long.MaxValue;
        int rooms = 0, withGold = 0;
        var scratch = new List<Steering.Fixed16Vector>();
        foreach (RoomInstance room in RoomLists.Of(world, database, creature.OwnerPlayerId))
        {
            if (!PaydayRules.IsPayingRoomOf(room, creature.OwnerPlayerId) || room.TileCount == 0) continue;
            rooms++;
            if (room.Definition.TypeId == RoomTypeId.DungeonHeart && RoomRules.IsRuined(world, room)) continue;
            if (PaydayRules.GoldIn(world, room) <= 0) continue;
            withGold++;
            TilePos position = ChickenRules.Position(world, room);
            if (!ChickenRules.Reachable(context, creature, definition, position, scratch)) continue;
            long dx = Math.Abs((long)CreatureState.CentreOf(position.X) - creature.PosX);
            long dy = Math.Abs((long)CreatureState.CentreOf(position.Y) - creature.PosY);
            long distance = ((dx * dx) >> 12) + ((dy * dy) >> 12);
            if (distance >= bestDistance) continue;
            best = room;
            bestDistance = distance;
        }

        if (best is not null && PileTile(context, best) is int tile)
        {
            int point = context.Rng.NextInt(PileOffsets.Length / 2);
            int x = ((tile % world.Width) << 12) | PileOffsets[point * 2];
            int y = ((tile / world.Width) << 12) | PileOffsets[(point * 2) + 1];
            if (context.Navigation.TryRouteTo(creature, x, y, Navigation.MoveOptions.ExactSpot))
            {
                SetOutInState(context, creature);
                context.Emit(SystemName, "wages_sought",
                    $"creature={creature.Id} type={definition.Name} " +
                    $"player={creature.OwnerPlayerId} owed={creature.WageOwed} " +
                    $"to=({tile % world.Width},{tile / world.Width}) gold={world.TileAtIndex(tile).GoldHeld} " +
                    $"steps={creature.Body.PathTotalCount}");
                return;
            }
        }

        // Nothing to walk to. The original distinguishes the two cases with two
        // different messages, and so does this - a keeper with no treasury at
        // all has a different problem from one whose treasury is empty.
        creature.PayAttemptsFailed++;
        context.Emit(SystemName, "wages_unreachable",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} owed={creature.WageOwed} " +
            $"reason={(rooms == 0 ? "no_treasury"
                : withGold == 0 ? "treasury_empty" : "unreachable")} " +
            $"rooms={rooms} with_gold={withGold} " +
            $"attempts={creature.PayAttemptsFailed}");
    }

    /// <summary><c>0x00698C20</c>: the eight points of a tile 187 picks from, x then y, a ring round
    /// the centre in thirds.</summary>
    private static readonly int[] PileOffsets =
    [
        0x2AA, 0x2AA, 0x7FF, 0x2AA, 0xD54, 0x2AA, 0x2AA, 0x7FF,
        0xD54, 0x7FF, 0x2AA, 0xD54, 0x7FF, 0xD54, 0xD54, 0xD54,
    ];

    /// <summary>187's tile: a random chain tile (<c>0x004E4D90(room, 1, 0)</c>), then on round the chain,
    /// wrapping, to the first that holds a store pile or no object; null once back at the start. A
    /// store pile is ours <see cref="TileState.GoldHeld"/>, as in <see cref="GoldStore.DepositTileAlongChain"/>.</summary>
    private static int? PileTile(SimulationContext context, RoomInstance room)
    {
        WorldState world = context.World;
        int[] chain = SlabChain.Order(world, room);
        int start = context.Rng.NextInt(room.TileCount);
        HashSet<int> occupied = GoldStore.ObjectTiles(world, room, chain);
        for (int step = 0; step < chain.Length; step++)
        {
            int tile = chain[(start + step) % chain.Length];
            if (world.TileAtIndex(tile).GoldHeld > 0 || !occupied.Contains(tile)) return tile;
        }
        return null;
    }

    /// <summary>Walking to the treasury, and taking the money on arrival.</summary>
    private static void Walking(SimulationContext context, CreatureState creature)
    {
        // CreatureMovementSystem walks the path; this only notices the end of
        // it, exactly as the hunger walk does.
        if (creature.HasRoute) return;
        Arrived(context, creature);
    }

    /// <summary>The decision sets 187, and 187 calls the position helper with 188 as the
    /// resume (<c>payday.md</c> §4.1-4.2, ADR-0180). Even a creature standing on the tile
    /// takes the helper's walk, because 187 has no shortcut of its own.</summary>
    private static void SetOutInState(SimulationContext context, CreatureState creature)
    {
        // Taken off a job, 187 is set from the job's own state; otherwise from idle.
        if (NeedRules.IsAtAJob(creature)) NeedRules.EndJob(creature);
        StateShadow.Move(context, SystemName, creature, PaydayRules.FindTheMoneyState);
        CreatureStateMachine.BeginTravel(context.Hooks,
            creature, StateShadow.TravelToPoint, PaydayRules.ArrivingState);
    }

    /// <summary>The walk ends in 188, which moves to 189 to take the money. 188 first
    /// re-checks the keeper still owns a treasury or heart, and waits while it turns to face the
    /// tile centre (<c>0x004B2AE0</c>, the turn, tunnelling.md §3); ours does neither.</summary>
    private static void Arrived(SimulationContext context, CreatureState creature)
    {
        StateShadow.Arrive(context, SystemName, creature);
        StateShadow.Move(context, SystemName, creature, PaydayRules.TakeItState);
        Take(context, creature);
    }

    /// <summary>
    /// State 189 (<c>0x00472740</c>): takes the wage from the room underfoot, and decides what
    /// happens next.
    ///
    /// <para><b>Not on a paying room, nothing.</b> The tile must be a Treasury or Heart of the
    /// creature's own keeper (<c>0x004539B0</c>, types 1 then 5). Otherwise 189 returns 0 and
    /// counts no attempt.</para>
    /// <para><b>The whole room, from the tile underfoot</b> (<c>0x004F4510</c> on the tile's room,
    /// <c>0x004727B4</c>, Q435). The withdrawal starts at the creature's own tile and goes round
    /// the room, so only a room emptied during the walk comes up with nothing.</para>
    /// </summary>
    private static void Take(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        int here = (creature.Tile.Y * world.Width) + creature.Tile.X;

        RoomInstance? room = RoomIndex.RoomAt(world, context.Database, here);
        if (room is null || !PaydayRules.IsPayingRoomOf(room, creature.OwnerPlayerId))
        {
            creature.StopMoving();
            StateShadow.ToIdle(context, SystemName, creature);
            context.Emit(SystemName, "wages_not_here",
                $"creature={creature.Id} player={creature.OwnerPlayerId} " +
                $"owed={creature.WageOwed} at=({creature.Tile.X},{creature.Tile.Y})");
            return;
        }

        int taken = PaydayRules.TakeFromRoom(world, room, here, creature.WageOwed);

        if (taken <= 0)
        {
            creature.PayAttemptsFailed++;
            creature.StopMoving();
            StateShadow.ToIdle(context, SystemName, creature);

            context.Emit(SystemName, "wages_missed",
                $"creature={creature.Id} player={creature.OwnerPlayerId} " +
                $"owed={creature.WageOwed} at=({creature.Tile.X},{creature.Tile.Y}) " +
                $"attempts={creature.PayAttemptsFailed}");
            return;
        }

        // The coin sound first (0x00472803): the gold object's pick-up group with the creature's own handle.
        ThingSoundRules.RequestInObjectCategory(context, SystemName, creature, FloorGold.FloorGoldObjectId,
            ThingSoundEvent.WageTaken, voiced: true);
        // Then the whole wage taken floats up from the creature, for its keeper (0x0047281E).
        CostNumberRules.Show(context, SystemName, new PlayerId(creature.OwnerPlayerId), CostNumberRules.PointOf(creature), taken);

        // Half of what came off the floor goes into the creature's own purse, and the rest
        // is gone (0x0048AE70(taken / 2), payday.md 4.4).
        CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
        int pocketed = PurseRules.Receive(
            context.Database, definition, creature, PurseRules.PocketedFromWage(taken));
        creature.WageOwed -= taken;

        // Paid in full, state 189 wipes the pay grievance outright (payday.md 4.4).
        if (creature.WageOwed == 0)
            CreatureAngerRules.Forget(world, definition, creature, AngerBucket.Pay);
        ThingSoundRules.Request(context, SystemName, creature, ThingSoundEvent.GoldReceived);   // 0x0047287A

        context.Emit(SystemName, "paid",
            $"creature={creature.Id} player={creature.OwnerPlayerId} " +
            $"took={taken} pocketed={pocketed} purse={creature.GoldHeld} " +
            $"still_owed={creature.WageOwed} " +
            $"at=({creature.Tile.X},{creature.Tile.Y})");

        // State 189 returns 0, which is idle, and payday's own decision runs again next tick
        // for anything still owed. Unless the creature's happy table holds Drink: then 189
        // hands it to the Drink handler, owed or not, which walks from 189 itself (Q421).
        creature.StopMoving();
        if (CreatureJobSystem.TryGoGamblingWhenPaid(context, creature, definition)) return;
        StateShadow.ToIdle(context, SystemName, creature);
    }
}
