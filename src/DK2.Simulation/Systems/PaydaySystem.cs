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
/// three original states folded into one activity; the arrival tile is the room's
/// gold-bearing tile nearest the creature, not the original's object search; and the
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
    /// (<see cref="PlayerState.NextPaydayTick"/>), seeded from
    /// <c>TimeBeforeFirstPayDaySeconds</c> and rewritten as
    /// <c>now + PayDayFrequencySeconds</c> each time it fires. A creature is credited
    /// <c>Pay</c> scaled by its level, added to what it is already owed, and its
    /// failed-attempt budget refills; one holding
    /// <see cref="CreatureState.SkipNextPayment"/> is not credited and spends the
    /// ticket.</remarks>
    private static void Clock(SimulationContext context)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        long now = context.Tick.Value;

        for (int p = 0; p < world.PlayerCount; p++)
        {
            ref PlayerState player = ref world.PlayerAtIndex(p);

            // Zero means "not scheduled yet", and the first sight of it sets the
            // first payday TimeBeforeFirstPayDaySeconds out, so a level does not
            // open with the whole dungeon owed money. A stated divergence: the
            // original seeds this in an initialiser and reads zero as "this
            // player never has a payday". Suppressing a player here would want a
            // flag rather than the sentinel back.
            if (player.NextPaydayTick.Value == 0)
            {
                player.NextPaydayTick =
                    new GameTick(now + PaydayRules.FirstPaydayTicks(database));
                continue;
            }

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

                if (creature.SkipNextPayment)
                {
                    // Spent whether or not it was needed, and no wage is
                    // credited at all - the payday does not happen for this
                    // creature, once.
                    creature.SkipNextPayment = false;
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

            // Only an idle creature sets off. The original gates on the current
            // state's category byte instead, which is the same idea expressed
            // against a table this project does not have.
            if (creature.Activity != CreatureActivity.Idle) continue;

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
                creature.Activity = CreatureActivity.SeekingWages;
                creature.StopMoving();
                Take(context, creature);
                return;
            }

            if (!context.Navigation.TryRoute(creature, at)) continue;

            creature.Activity = CreatureActivity.SeekingWages;

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
        Take(context, creature);
    }

    /// <summary>
    /// Takes what the tile underfoot will give, and decides what happens next.
    ///
    /// <para><b>One tile, and it can come up empty.</b> The original passes the
    /// creature's own coordinates to its withdrawal, so a creature standing on a
    /// bare tile of an otherwise full treasury gets nothing and spends an
    /// attempt. Ours picks a gold-bearing tile to walk to, so that is rarer here
    /// than in the original — but it still happens, because somebody else may
    /// have emptied the tile during the walk, and that case is the one worth
    /// having.</para>
    /// </summary>
    private static void Take(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        int here = (creature.Tile.Y * world.Width) + creature.Tile.X;

        int taken = PaydayRules.TakeFromTile(world, here, creature.WageOwed);

        if (taken <= 0)
        {
            creature.PayAttemptsFailed++;
            creature.Activity = CreatureActivity.Idle;
            creature.StopMoving();

            context.Emit(SystemName, "wages_missed",
                $"creature={creature.Id} player={creature.OwnerPlayerId} " +
                $"owed={creature.WageOwed} at=({creature.Tile.X},{creature.Tile.Y}) " +
                $"attempts={creature.PayAttemptsFailed}");
            return;
        }

        creature.WageOwed -= taken;

        context.Emit(SystemName, "paid",
            $"creature={creature.Id} player={creature.OwnerPlayerId} " +
            $"took={taken} still_owed={creature.WageOwed} " +
            $"at=({creature.Tile.X},{creature.Tile.Y})");

        // Still owed something? The original loops back to its search state and
        // walks to another tile; so does this, on the next tick, because the
        // creature goes idle and the decision runs again.
        creature.Activity = CreatureActivity.Idle;
        creature.StopMoving();
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
