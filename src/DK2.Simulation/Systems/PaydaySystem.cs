// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;

namespace DK2.Simulation.Systems;

/// <summary>Payday: the wage bill falls due, every creature is credited, and each
/// walks to a treasury for its own wages (docs/research/payday.md, ADR-0078).</summary>
/// <remarks>Two phases in one system, the clock then the collecting, which is the
/// original's order — its clock calls the collect decision in the same loop that
/// credits the wage — so a creature credited on this tick sets off on this tick.
/// Being owed is what charges <c>AngerNoPay</c> +60, and that charge lives in
/// <see cref="CreatureAngerRules"/> on the anger system's second (ADR-0079,
/// <see cref="PaydayRules.IsOwedWages"/>). Ours, none of it changing who gets paid:
/// one activity over states 187-189, held as state ids (ADR-0180); the arrival tile is
/// the room's gold-bearing tile nearest the creature, not the original's search; and the
/// Dungeon Heart is admitted unconditionally where the original tests one undecoded
/// flag bit beside the type.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/payday.md")]
public sealed class PaydaySystem : ISimulationSystem
{
    private const string SystemName = "Payday";

    private readonly IPathfinder _pathfinder;
    private readonly List<TilePos> _route = [];

    public PaydaySystem() : this(new GridPathfinder()) { }

    public PaydaySystem(IPathfinder pathfinder) =>
        _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        Clock(context);
        Collect(context);
    }

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
    private static void Clock(SimulationContext context)
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

            byte playerId = player.PlayerId;
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
                if (wage <= 0) continue;

                creature.WageOwed += wage;
                billed += wage;
            }

            player.NextPaydayTick = new GameTick(now + PaydayRules.IntervalTicks(database));

            context.Emit(SystemName, "payday",
                $"player={playerId} creatures={creatures} billed={billed} " +
                $"skipped={skipped} treasury={GoldStore.Total(world, playerId)} " +
                $"next={player.NextPaydayTick.Value}");
        }
    }

    // --------------------------------------------------------- the collecting

    private void Collect(SimulationContext context)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState creature = creatures[i];

            if (HandRules.IsHeld(creature)) continue;
            if (CreatureDeathRules.IsDown(creature)) continue;
            if (PrisonRules.IsImprisoned(creature)) continue;

            if (creature.Activity == CreatureActivity.SeekingWages)
            {
                Walking(context, creature);
                continue;
            }

            // A creature walking out for good does not stop to be paid - it is
            // leaving BECAUSE of things like this. Same narrowing the eat and
            // sleep systems make, and ours rather than the original's.
            if (creature.Activity == CreatureActivity.Leaving) continue;
            if (Combat.CombatRules.Preoccupied(creature)) continue;

            // An idle creature sets off, and so does one at a job whose state admits
            // Payday: the needs routine's mask test (NeedRules, ADR-0198).
            if (!NeedRules.MayAsk(creature, CreatureStateCategory.Payday)) continue;

            // Tried every fourth turn: the stamp moves whether or not anything is owed, and
            // before the busy test (0x00477F69, ADR-0202).
            long now = context.Tick.Value;
            if (now < creature.NextPayTryTick) continue;
            creature.NextPayTryTick = now + (NeedRules.RetryTurns * SimulationTiming.OriginalTickPeriodTicks);
            if (NeedRules.IsBusy(context.World, creature, now)) continue;

            // SkipNextPayment is spent by this decision whatever it decides: a set flag, nothing
            // owed or two failures all clear it (0x0047237F, hand-drop-earmark.md §9).
            bool skip = creature.SkipNextPayment;
            creature.SkipNextPayment = false;
            if (skip) continue;
            if (creature.WageOwed <= 0) continue;
            if (creature.PayAttemptsFailed >= PaydayRules.MaxFailedAttempts) continue;

            SetOutForTheTreasury(context, creature, database.CreatureById(creature.CreatureTypeId));
        }
    }

    /// <summary>Chooses a treasury and starts the walk, or records why not.</summary>
    /// <remarks>Rooms are ranked nearest-first by squared straight-line distance — the
    /// original's ranking, and the same one the prison's food search and the hunger
    /// walk use — and the first this creature can actually reach over its own
    /// <see cref="WorldPathGrid"/> wins. Ties fall back to the lowest tile index,
    /// <see cref="RoomIndex.Build"/>'s order (ADR-0001). A treasury with no gold in it
    /// is not a candidate, which is the original's own gate and is visible in play: a
    /// keeper with three empty treasuries and one full one sends everybody to the full
    /// one rather than to the nearest.</remarks>
    private void SetOutForTheTreasury(
        SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        var candidates = new List<(long Distance, int FirstTile, RoomInstance Room)>();
        int rooms = 0;

        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (!PaydayRules.IsPayingRoomOf(room, creature.OwnerPlayerId)) continue;
            if (room.TileIndices.Count == 0) continue;
            rooms++;

            if (PaydayRules.GoldIn(world, room) <= 0) continue;

            candidates.Add((
                EatingRules.DistanceSquared(world, creature.Tile, room.TileIndices[0]),
                room.TileIndices[0], room));
        }

        candidates.Sort((a, b) =>
            a.Distance != b.Distance
                ? a.Distance.CompareTo(b.Distance)
                : a.FirstTile.CompareTo(b.FirstTile));

        var grid = new WorldPathGrid(database, world, definition);

        foreach ((_, _, RoomInstance room) in candidates)
        {
            int target = NearestPayingTile(world, room, creature.Tile);
            if (target < 0) continue;

            var at = new TilePos(target % world.Width, target / world.Width);

            // Standing on it already: no walk to make, take the money now.
            if (at == creature.Tile)
            {
                creature.StopMoving();
                SetOutInState(context, creature);
                Arrived(context, creature);
                return;
            }

            if (!context.Navigation.TryRoute(creature, at, Navigation.MoveOptions.ExactSpot)) continue;

            SetOutInState(context, creature);

            context.Emit(SystemName, "wages_sought",
                $"creature={creature.Id} type={definition.Name} " +
                $"player={creature.OwnerPlayerId} owed={creature.WageOwed} " +
                $"to=({at.X},{at.Y}) gold={world.TileAtIndex(target).GoldHeld} " +
                $"steps={creature.Body.PathTotalCount}");
            return;
        }

        // Nothing to walk to. The original distinguishes the two cases with two
        // different messages, and so does this - a keeper with no treasury at
        // all has a different problem from one whose treasury is empty.
        creature.PayAttemptsFailed++;
        context.Emit(SystemName, "wages_unreachable",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} owed={creature.WageOwed} " +
            $"reason={(rooms == 0 ? "no_treasury"
                : candidates.Count == 0 ? "treasury_empty" : "unreachable")} " +
            $"rooms={rooms} with_gold={candidates.Count} " +
            $"attempts={creature.PayAttemptsFailed}");
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
        CreatureStateMachine.BeginTravel(
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

        // Half of what came off the floor goes into the creature's own purse, and the rest
        // is gone (0x0048AE70(taken / 2), payday.md 4.4).
        CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
        int pocketed = PurseRules.Receive(
            context.Database, definition, creature, PurseRules.PocketedFromWage(taken));
        creature.WageOwed -= taken;

        // Paid in full, state 189 wipes the pay grievance outright (payday.md 4.4).
        if (creature.WageOwed == 0)
            CreatureAngerRules.Forget(world, definition, creature, AngerBucket.Pay);

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

    /// <summary>
    /// The tile of this room with gold on it that is nearest the creature, or
    /// -1 when the room has none.
    ///
    /// <para>Ours. The original walks the room to a tile and then searches that
    /// tile's object list for a gold pile, which needs an object model for gold
    /// that this project does not have — gold lives in a field on the tile
    /// (ADR-0015). Picking the nearest gold-bearing tile reaches the same place
    /// by the shortest honest route.</para>
    /// </summary>
    private static int NearestPayingTile(WorldState world, RoomInstance room, TilePos from)
    {
        int best = -1;
        long bestDistance = long.MaxValue;

        foreach (int index in room.TileIndices)
        {
            if (world.TileAtIndex(index).GoldHeld <= 0) continue;

            long distance = EatingRules.DistanceSquared(world, from, index);
            if (best >= 0 && distance >= bestDistance) continue;

            best = index;
            bestDistance = distance;
        }

        return best;
    }
}
