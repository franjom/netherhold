// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation.Systems;

public sealed partial class CreatureJobSystem
{
    public const int IdleChooserState = 89;
    public const int NeutralIdleState = 5;
    public const int HornyIdleState = 238;

    /// <summary>State 1 under the dispatcher (<c>0x00476FC0</c>, <c>0x004773D0</c>): the mood prelude,
    /// the Fleeing clear, the fight return, then the role's next state, whose handler runs on a later
    /// turn (ADR-0574).</summary>
    private void DispatchIdle(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        if (TryDispatchMoodPrelude(context, creature, definition)) return;
        creature.Fleeing = false;
        if (Combat.FightReturnRules.TryReturn(context, creature)) return;
        int destination;
        if (creature.OwnerPlayerId == PlayerIds.Neutral)
        {
            creature.StateDeadlineTick = context.Tick.Value + NothingToDoTicks;
            destination = NeutralIdleState;
        }
        else destination = definition.Flags.HasFlag(CreatureFlags.IsHorny) ? HornyIdleState : IdleChooserState;
        // One handler per turn. The special-role controllers remain explicitly unbuilt.
        CreatureStateMachine.TrySetState(context.Hooks, creature, destination, context.Tick.Value);
    }

    private bool TryDispatchMoodPrelude(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        context.TremorMoodDispatchedThisTick.Add(creature);
        CreatureMood mood = CreatureAngerRules.MoodOf(definition, creature);
        // The dispatcher's owner test: the hero and neutral players' creatures run no choosers (ADR-0485).
        bool eligible = NeedRules.RunsNeeds(creature)
            && !definition.Flags.HasFlag(CreatureFlags.IsHorny)
            && !creature.RebelActive
            && !CallToArmsRules.IsUnderCallToArms(context.World, creature);
        // Raw idle and the caller's role/need/combat guards cover the modeled dispatcher gates.
        // Unmodeled body flags and possessed-following are scoped in the Tremor research.
        if (eligible && mood != CreatureMood.Content)
        {
            // The dispatcher runs the angry chooser once a turn, and its failure falls into the
            // unhappy one (0x00477244, 0x00477256). CreatureAngerSystem ran it earlier in this
            // update: a choice made there skips the handler, and none goes on to unhappy.
            if (mood == CreatureMood.Angry)
            {
                if (context.AngryChooserRanThisTick.Contains(creature))
                {
                    if (creature.LastAngryChoiceTick == context.Tick.Value) return true;
                }
                else if (TryChooseDispatchedMood(context, creature, definition, mood)) return true;
            }
            if (TryChooseDispatchedMood(context, creature, definition, CreatureMood.Unhappy)) return true;
        }
        return false;
    }

    private bool TryChooseDispatchedMood(
        SimulationContext context, CreatureState creature, CreatureDef definition, CreatureMood mood)
    {
        long now = context.Tick.Value;
        long last = mood == CreatureMood.Angry ? creature.LastAngryChoiceTick : creature.LastUnhappyChoiceTick;
        if (last != 0 && now - last < JobRules.ReselectionGateTicks) return false;
        if (_seeker.TryGoForALair(context, SystemName, creature, definition))
        {
            // The unhappy lair arm also writes the angry timestamp in the original.
            creature.LastAngryChoiceTick = now;
            return true;
        }

        CreatureAngerRules.CumulativeRanges(JobRules.TableFor(definition, mood), _ranges);
        int roll = CreatureAngerRules.RollPercent(context.Rng);
        for (int i = 0; i < _ranges.Count; i++)
        {
            var (job, low, high) = _ranges[i];
            if (job != JobType.None && low <= roll && roll <= high && TryStart(job)) return Accepted();
        }
        for (int i = 0; i < _ranges.Count; i++)
            if (_ranges[i].Job != JobType.None && TryStart(_ranges[i].Job)) return Accepted();
        return false;

        bool TryStart(JobType job) => job is JobType.Leave or JobType.Rebel
            ? _moodJobs.TryStartMoodJob(context, definition, creature, job)
            : TryTake(context, creature, definition, job, roll, "mood_dispatch", report: false);

        bool Accepted()
        {
            if (mood == CreatureMood.Angry) creature.LastAngryChoiceTick = now;
            else creature.LastUnhappyChoiceTick = now;
            return true;
        }
    }
}
