// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;

namespace DK2.Simulation.Systems;

/// <summary>
/// A hungry prisoner looks round its cell for something to eat, walks to the
/// nearest thing it finds, and eats it — the second half of Q172, and the half
/// the owner described as a race:
///
/// <para><i>"they rush toward that chicken, the first one to reach it eats
/// it"</i> (2026-09-05).</para>
///
/// <para><b>Read from the original's own prison states</b>, under the
/// state-table frame corrected by ADR-0072 — without which these handlers were
/// all filed one state early and the search looked like an eat-in-place
/// (<c>docs/research/prison.md</c> §7):</para>
///
/// <list type="table">
/// <item><term><b>115</b> (<c>0x73</c>)</term><description>The decision. When
/// <c>HungerNeed &gt;= HungerFill</c> <i>and</i>
/// <c>TimeBetweenReattemptingHungerSeconds</c> has passed since the last
/// attempt, it stamps the clock and goes to 117. Otherwise it wanders
/// (ADR-0073).</description></item>
/// <item><term><b>117</b> (<c>0x75</c>)</term><description>The search, and it is
/// a <b>walk</b> rather than a meal: it scans every tile of the prison for
/// food-flagged objects, takes the nearest by straight-line squared distance,
/// and issues a move to it resuming in 118. Finding none, it applies
/// <c>AngerNoFood</c>, tells the interface, and drops back to 116 to keep
/// wandering.</description></item>
/// <item><term><b>118</b> (<c>0x76</c>)</term><description>The meal. If a
/// food-flagged object is on the tile underfoot it destroys it and pays off
/// hunger — <b>the whole of <c>HungerFill</c></b>, not one helping. Still owing?
/// back to 117 for another. Nothing there? <b>also back to 117</b>, and that is
/// the race: the bird was never reserved and somebody else ate
/// it.</description></item>
/// <item><term><b>119</b> (<c>0x77</c>)</term><description>Arriving, which here
/// is the same tick as the meal.</description></item>
/// </list>
///
/// <para><b>A prisoner never leaves the prison states to eat.</b> 117, 118 and
/// 119 carry the same label, group tag and category as 114, 115 and 116 — the
/// creature is <i>being held captive</i> throughout — so this system leaves
/// <see cref="CreatureActivity.Imprisoned"/> alone and marks the walk with
/// <see cref="CreatureState.TargetObjectId"/> instead. That is not tidiness: it
/// is why <see cref="PrisonSystem"/> keeps draining a prisoner that is walking
/// to its dinner, and why the keeper can still lift one out mid-stride.</para>
///
/// <para><b>Two rules that were nearly missed, and both are in the original's
/// units rather than its prose.</b></para>
///
/// <list type="number">
/// <item><b>One bird fills a prisoner completely.</b> DK2's state 118 passes the
/// level-scaled <c>HungerFill</c> to the same routine its hatchery state passes
/// a literal 1 to, so a caged Bile Demon is filled by one chicken where a free
/// one eats five (<see cref="EatingRules.EatInPrison"/>, written down two
/// sittings before anything could call it).</item>
/// <item><b>The anger is a quarter of the tabled figure.</b> The creature loader
/// divides every <c>angerNo*</c> field by DK2's four ticks a second as it loads
/// it, and state 117 adds one of those per attempt rather than per tick
/// (<see cref="PrisonRules.AngerPerFailedFoodSearch"/>).</item>
/// </list>
///
/// <para><b>What is ours, and stated as such.</b> The route to the bird is
/// restricted to the cell's own tiles. DK2 issues an ordinary move and relies on
/// the bird being in the room the prisoner is standing in; ours would happily
/// route an L-shaped prison's corner through the corridor outside, which would
/// look exactly like an escape. The containment that ADR-0073 found in the
/// wander — <i>a tile outside the room is never a candidate</i> — is applied
/// here too, and it is a narrowing rather than a rule.</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/prison.md")]
public sealed class PrisonerEatSystem : ISimulationSystem
{
    private const string SystemName = "PrisonerEat";

    private readonly IPathfinder _pathfinder;
    private readonly List<TilePos> _route = [];

    public PrisonerEatSystem() : this(new GridPathfinder()) { }

    public PrisonerEatSystem(IPathfinder pathfinder) =>
        _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState creature = creatures[i];
            if (!PrisonRules.IsImprisoned(creature)) continue;

            // In the keeper's hand it is out of the dungeon (ADR-0062); down,
            // it is not walking anywhere and PrisonSystem owns its second.
            if (HandRules.IsHeld(creature)) continue;
            if (CreatureDeathRules.IsDown(creature)) continue;

            CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
            if (!CreatureHealthRules.CanGetHungry(definition))
            {
                creature.TargetObjectId = EatingRules.NoChicken;
                continue;
            }

            // Already walking to one: states 118 and 119.
            if (creature.TargetObjectId >= 0)
            {
                Fetching(context, creature, definition);
                continue;
            }

            // State 115's own hunger test. It is HungerNeed >= HungerFill, the
            // same entry test the general needs routine uses, and it is asked
            // before the wander - which is why this system is registered above
            // PrisonerWanderSystem.
            if (!EatingRules.IsHungry(database, definition, creature)) continue;

            // At most one attempt a game second. The general path has the 4 as a
            // literal; the prison reads TimeBetweenReattemptingHungerSeconds,
            // which is 4, and four DK2 ticks is one game second (Q1/Q132). So
            // the number to use is one game second and not the variable's own
            // value read as seconds, which would be four times too slow.
            creature.FoodAttemptTicks--;
            if (creature.FoodAttemptTicks > 0) continue;
            creature.FoodAttemptTicks = EatingRules.RetryTicks;

            Search(context, creature, definition);
        }
    }

    /// <summary>
    /// State 117: look round the cell, and either set off for the nearest thing
    /// worth eating or grow angry that there is none.
    /// </summary>
    private void Search(
        SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        RoomInstance? cell = RoomIndex.RoomAt(
            world, database, (creature.Tile.Y * world.Width) + creature.Tile.X);

        // Standing on something that has stopped being a room. PrisonSystem
        // frees it on the same second; there is nothing to search.
        if (cell is null) return;

        ObjectState? food = EatingRules.NearestFoodIn(world, database, cell, creature.Tile);
        if (food is null)
        {
            int anger = PrisonRules.AngerPerFailedFoodSearch(definition);
            if (anger != 0)
            {
                CreatureAngerRules.Apply(creature, anger);
                context.Emit(SystemName, "prisoner_no_food",
                    $"creature={creature.Id} type={definition.Name} " +
                    $"player={creature.OwnerPlayerId} " +
                    $"at=({creature.Tile.X},{creature.Tile.Y}) " +
                    $"need={creature.HungerNeed} anger=+{anger} " +
                    $"total={creature.Anger}");
            }

            return;
        }

        creature.TargetObjectId = food.Id;

        int x = food.TileIndex % world.Width;
        int y = food.TileIndex / world.Width;

        // Already on it or beside it: eat this tick, with no walk at all.
        if (EatingRules.DistanceSquared(world, creature.Tile, food.TileIndex)
            <= EatingRules.ReachTilesSquared)
        {
            Swallow(context, creature, definition, food);
            return;
        }

        creature.Path.Clear();
        creature.PathIndex = 0;

        var grid = new CellGrid(
            new WorldPathGrid(database, world, definition), cell, world.Width);
        if (_pathfinder.TryFindPath(grid, creature.Tile, new TilePos(x, y), _route))
        {
            creature.Path.AddRange(_route);
        }

        context.Emit(SystemName, "prisoner_food_chosen",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} object={food.Id} " +
            $"at=({x},{y}) steps={creature.Path.Count}");
    }

    /// <summary>
    /// States 118 and 119: walking to the chosen thing, and eating it on
    /// arrival — or finding it gone, which is what losing the race looks like
    /// from the inside.
    /// </summary>
    private static void Fetching(
        SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        ObjectState? food = world.ObjectById(creature.TargetObjectId);
        if (food is null
            || food.CarriedByCreatureId >= 0
            || !EatingRules.IsFood(database, food))
        {
            context.Emit(SystemName, "prisoner_food_lost",
                $"creature={creature.Id} type={definition.Name} " +
                $"player={creature.OwnerPlayerId} object={creature.TargetObjectId}");
            BackToTheSearch(creature);
            return;
        }

        if (EatingRules.DistanceSquared(world, creature.Tile, food.TileIndex)
            > EatingRules.ReachTilesSquared)
        {
            // Still walking. A route that ran out short of the bird is a failed
            // approach and the search starts again from where it stands.
            if (creature.PathIndex < creature.Path.Count) return;

            BackToTheSearch(creature);
            return;
        }

        Swallow(context, creature, definition, food);
    }

    private static void Swallow(
        SimulationContext context, CreatureState creature,
        CreatureDef definition, ObjectState food)
    {
        int before = creature.Health;
        int gained = EatingRules.EatInPrison(context.Database, definition, creature);
        context.World.RemoveObject(food.Id);

        context.Emit(SystemName, "prisoner_ate",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} object={food.Id} " +
            $"at=({creature.Tile.X},{creature.Tile.Y}) " +
            $"health={before}->{creature.Health} gained={gained} " +
            $"need={creature.HungerNeed}");

        // Still owing helpings sends it round again at once. EatInPrison pays
        // off a whole HungerFill, so this only fires for a prisoner that had
        // accumulated more than one - which is any prisoner left long enough.
        if (EatingRules.StillOwesFood(creature))
        {
            BackToTheSearch(creature);
            return;
        }

        creature.TargetObjectId = EatingRules.NoChicken;
        creature.Path.Clear();
        creature.PathIndex = 0;
    }

    /// <summary>
    /// Back into state 117, and <b>without</b> the retry wait: the original's
    /// clock is stamped on the way out of the decision state, not on the way
    /// into the search, so a prisoner that loses its bird looks again straight
    /// away rather than a second later.
    /// </summary>
    private static void BackToTheSearch(CreatureState creature)
    {
        creature.TargetObjectId = EatingRules.NoChicken;
        creature.FoodAttemptTicks = 1;
        creature.Path.Clear();
        creature.PathIndex = 0;
    }

    /// <summary>
    /// The cell, as a thing to walk about in: every tile the mover could
    /// ordinarily use, minus every tile that is not this room's.
    ///
    /// <para>See the type summary — this is the narrowing, not the
    /// original.</para>
    /// </summary>
    private sealed class CellGrid(IPathGrid inner, RoomInstance room, int width) : IPathGrid
    {
        public int Width => inner.Width;

        public int Height => inner.Height;

        public bool IsWalkable(int x, int y) =>
            inner.IsWalkable(x, y) && EatingRules.Contains(room, (y * width) + x);
    }
}
