// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>
/// The ladder: <b>content → unhappy → angry → gone</b>. Once a game second.
///
/// <para><b>Confirmed three times from three different causes before it was
/// ever built</b>, which is unusual and is why the shape was never in doubt —
/// only the unit was:</para>
///
/// <list type="bullet">
/// <item><b>Q9</b> (pay): <i>"Unpaid → unhappy → angry → leaves; a converted
/// creature rebels instead."</i></item>
/// <item><b>Q105</b> (lairs): <i>"If a creature can't get a lair it becomes
/// unhappy, it can't go to sleep, then angry, then it leaves the dungeon."</i></item>
/// <item><b>The bed-loss answer</b>: a creature whose lair tile is sold looks
/// for another, <i>"otherwise anger mechanic is triggered, it gets upset, then
/// angry then it leaves the dungeon."</i></item>
/// </list>
///
/// <para>And then <b>Q106</b> gave the unit with a stopwatch — 32767 — which is
/// the whole reason this file exists. See <see cref="CreatureAngerRules"/> for
/// the arithmetic.</para>
///
/// <para><b>What an angry creature does comes from its own record</b>, not from
/// a rule here: a Goblin's <c>AngryJobs</c> table is <c>Leave(50%)</c> and
/// <c>Rebel(50%)</c>. Leaving is implemented. Rebelling is <b>not</b> — it needs
/// a creature to change sides and become a hero, and the three variables that
/// govern the outcome (<c>RebelLeavingPercentage</c> 70,
/// <c>RebelBecomeGoodPercentage</c> 15, <c>RebelDefectPercentage</c> 15) have
/// nothing to act on. So a creature that draws Rebel stays angry and says so,
/// once, rather than being quietly made to leave instead. Turning an unbuilt
/// outcome into a built one would misreport half of every angry creature.</para>
///
/// <para><b>Cadence and order.</b> The last tick of each game second, the phase
/// <see cref="ManaSystem"/> and <see cref="CreatureHealthSystem"/> use, because
/// every rate it reads is written per second. Registered <b>after</b>
/// <see cref="CreatureSleepSystem"/> so that a creature which claimed a bed this
/// tick is not charged <c>AngerNoLair</c> for the second it arrived, and
/// <b>after</b> <see cref="CreatureHealthSystem"/> so that a creature which
/// collapsed this second is already <see cref="CreatureActivity.Dying"/> and is
/// skipped.</para>
///
/// <para><b>A creature that is down is skipped entirely.</b> Something lying on
/// the floor waiting to be carried to bed is not accruing a grievance about not
/// having got there — and more practically, a dying creature that deserted would
/// vanish out of the rescue window by a second route (ADR-0051).</para>
/// </summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/creature-anger.md")]
public sealed class CreatureAngerSystem : ISimulationSystem
{
    private const string SystemName = "CreatureAnger";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        if ((context.Tick.Value + 1) % SimulationTiming.TicksPerSecond != 0) return;

        WorldState world = context.World;
        GameDatabase database = context.Database;

        // Descending, so a creature that walks out cannot skip its neighbour.
        for (int i = world.Creatures.Count - 1; i >= 0; i--)
        {
            CreatureState state = world.Creatures[i];
            CreatureDef creature = database.CreatureById(state.CreatureTypeId);

            if (CreatureDeathRules.IsDown(state)) continue;
            if (!CreatureAngerRules.CanBeAngered(creature)) continue;

            CreatureMood before = CreatureAngerRules.MoodOf(creature, state);

            int delta = CreatureAngerRules.PerSecond(world, database, creature, state);
            if (delta != 0) CreatureAngerRules.Apply(state, delta);

            CreatureMood after = CreatureAngerRules.MoodOf(creature, state);

            if (after != before)
            {
                context.Emit(SystemName, "creature_mood",
                    $"creature={state.Id} type={creature.Name} " +
                    $"player={state.OwnerPlayerId} {before}->{after} " +
                    $"anger={state.Anger}/{CreatureAngerRules.Ceiling} " +
                    $"unhappy_at={CreatureAngerRules.UnhappyAt(creature)}");
            }

            if (after == CreatureMood.Angry) Decide(context, creature, state);
        }
    }

    /// <summary>
    /// An angry creature draws from its own angry job table and acts on it —
    /// or reports that it cannot.
    ///
    /// <para>Drawn once on the second it <i>becomes</i> angry and then every
    /// second it stays angry, which matters for the unimplemented half: a
    /// creature that drew Rebel would otherwise re-roll for ever and eventually
    /// leave by luck. So the draw is recorded on the creature and kept.</para>
    /// </summary>
    private static void Decide(
        SimulationContext context, CreatureDef creature, CreatureState state)
    {
        if (state.AngryChoice == JobType.None)
        {
            state.AngryChoice = CreatureAngerRules.AngryChoice(creature, context.Rng);

            context.Emit(SystemName, "creature_angry",
                $"creature={state.Id} type={creature.Name} " +
                $"player={state.OwnerPlayerId} chose={state.AngryChoice} " +
                $"from=[{string.Join(",", creature.AngryJobs
                    .Select(j => $"{j.Job}:{j.ChancePercent}%"))}]");
        }

        switch (state.AngryChoice)
        {
            case JobType.Leave:
                Desert(context, creature, state);
                break;

            default:
                // Rebel, Sulk, DestroyWalls, StealGold and the rest are real
                // DK2 outcomes with nothing here to act on. Reported once by
                // the event above and then left alone: the creature stays in
                // the dungeon, angry, doing nothing. Honest, and visible.
                break;
        }
    }

    /// <summary>
    /// The creature walks out. <b>It simply leaves</b> — no route to a portal,
    /// no walk to the edge of the map.
    ///
    /// <para>DK2 sends a deserter to a portal and it leaves through it, which
    /// means a keeper with no portal, or one whose portal is unreachable, keeps
    /// a creature that wanted to go. The wiki's DK2 section says as much: an
    /// angry creature <i>"head[s] for the exit, or rebel[s] by becoming a hero
    /// if they can't do that"</i>. That reachability test is not implemented, so
    /// desertion here is unconditional and the event says so. Q116.</para>
    ///
    /// <para>Its bed is released, for the same reason death releases one: a lair
    /// that leaks a tile every time somebody storms off would quietly shrink.</para>
    /// </summary>
    private static void Desert(
        SimulationContext context, CreatureDef creature, CreatureState state)
    {
        LairRules.ReleaseBed(state);

        context.Emit(SystemName, "creature_left",
            $"creature={state.Id} type={creature.Name} " +
            $"player={state.OwnerPlayerId} at=({state.Tile.X},{state.Tile.Y}) " +
            $"anger={state.Anger} gold_lost={state.GoldHeld} " +
            $"via=nowhere_in_particular");

        context.World.RemoveCreature(state.Id);
    }
}
