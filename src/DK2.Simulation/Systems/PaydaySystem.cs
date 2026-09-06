// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;

namespace DK2.Simulation.Systems;

/// <summary>
/// Payday: the keeper's wage bill falls due, every creature it owns is credited,
/// and each one walks to a treasury and lifts its own wages off the floor.
///
/// <para>The last of M4's original seven items, and the one that turns gold from
/// something a keeper accumulates into something a keeper can <b>run out
/// of</b>. Read from the original — <c>docs/research/payday.md</c> has the
/// addresses; ADR-0078 has the decisions.</para>
///
/// <para><b>Two phases, in one system, and the original keeps them apart.</b>
/// Its clock is a per-player routine and its collecting is three creature
/// states. This runs the clock first and then every creature's collecting,
/// which matters for exactly one thing: a creature credited on this tick sets
/// off on this tick rather than the next. That is the original's order too —
/// its clock calls the collect decision directly, in the same loop that credits
/// the wage.</para>
///
/// <list type="number">
/// <item><b>The clock.</b> Each keeper carries a deadline
/// (<see cref="PlayerState.NextPaydayTick"/>), seeded from
/// <c>TimeBeforeFirstPayDaySeconds</c> and rewritten as <c>now +
/// PayDayFrequencySeconds</c> each time it fires. Zero means never.</item>
/// <item><b>The credit.</b> Every creature of that keeper gets <c>Pay</c> scaled
/// by its level <i>added</i> to what it is already owed, and its failed-attempt
/// counter reset. A creature holding <see cref="CreatureState.SkipNextPayment"/>
/// is not credited at all and the ticket is spent.</item>
/// <item><b>The walk.</b> A creature owed money and not already busy goes to the
/// nearest reachable Treasury or Dungeon Heart of its own keeper <b>that has
/// gold in it</b>.</item>
/// <item><b>The taking.</b> On arrival it takes up to what it is owed off <b>the
/// one tile it is standing on</b>. An empty tile costs it one of its two
/// attempts.</item>
/// </list>
///
/// <para><b>What is ours and named as such.</b> Three states folded into one
/// activity; the arrival tile chosen as the room's gold-bearing tile nearest the
/// creature rather than by the original's object search, which walks a tile's
/// object list this project does not model for gold; and the Dungeon Heart
/// admitted unconditionally where the original tests one undecoded flag bit
/// beside the type. None of the three changes who gets paid.</para>
///
/// <para><b>What is missing, and it is the important one.</b> Nothing here
/// charges <c>AngerNoPay</c>. The owner has described the consequence (Q9:
/// unhappy, then angry, then leaves — or rebels, if converted) and the ladder is
/// built (ADR-0053), but the routine that connects them was not found in the
/// payday path and is not invented here. An unpaid creature stays owed and stays
/// calm, which is wrong and is wrong <i>visibly</i> — the events say so.</para>
/// </summary>
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

    /// <summary>
    /// Fires any keeper's payday that has come due, credits its creatures, and
    /// reschedules.
    /// </summary>
    private static void Clock(SimulationContext context)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        long now = context.Tick.Value;

        for (int p = 0; p < world.PlayerCount; p++)
        {
            ref PlayerState player = ref world.PlayerAtIndex(p);

            // Zero means "not scheduled yet", and the first sight of it sets
            // the first payday TimeBeforeFirstPayDaySeconds out - so a level
            // does not open with the whole dungeon owed money.
            //
            // A DIVERGENCE, stated: the original seeds this in an initialiser
            // and reads zero as "this player never has a payday". Ours has
            // nowhere to seed it (WorldState.FromMap has no GameDatabase) and
            // no player that needs "never" - a slot with no creatures bills
            // nobody. If a hero or front-end player ever needs suppressing, it
            // wants a flag rather than this sentinel back.
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

    /// <summary>
    /// Chooses a treasury and starts the walk, or records why not.
    ///
    /// <para>Rooms are ranked nearest-first by squared straight-line distance —
    /// the original's ranking, and the same one the prison's food search and the
    /// hunger walk use — and the first that this creature can actually reach
    /// over its own <see cref="WorldPathGrid"/> wins. Ties fall back to the
    /// lowest tile index, which is <see cref="RoomIndex.Build"/>'s order and is
    /// reproducible (ADR-0001).</para>
    ///
    /// <para><b>A treasury with no gold in it is not a candidate</b>, which is
    /// the original's own gate and has a visible consequence: a keeper with
    /// three empty treasuries and one full one sends everybody to the full one
    /// rather than to the nearest.</para>
    /// </summary>
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
                creature.Path.Clear();
                creature.PathIndex = 0;
                Take(context, creature);
                return;
            }

            if (!_pathfinder.TryFindPath(grid, creature.Tile, at, _route)) continue;

            creature.Path.Clear();
            creature.Path.AddRange(_route);
            creature.PathIndex = 0;
            creature.Activity = CreatureActivity.SeekingWages;

            context.Emit(SystemName, "wages_sought",
                $"creature={creature.Id} type={definition.Name} " +
                $"player={creature.OwnerPlayerId} owed={creature.WageOwed} " +
                $"to=({at.X},{at.Y}) gold={world.TileAtIndex(target).GoldHeld} " +
                $"steps={creature.Path.Count}");
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
        if (creature.PathIndex < creature.Path.Count) return;
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
            creature.Path.Clear();
            creature.PathIndex = 0;

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
        creature.Path.Clear();
        creature.PathIndex = 0;
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
