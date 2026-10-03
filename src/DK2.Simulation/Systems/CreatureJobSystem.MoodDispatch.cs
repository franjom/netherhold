// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation.Systems;

public sealed partial class CreatureJobSystem
{
    public const int IdleChooserState = 89;

    private void DispatchFleeingIdle(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        if (TryDispatchMoodPrelude(context, creature, definition)) return;
        creature.Fleeing = false;
        if (Combat.FightReturnRules.TryReturn(context, creature)) return;
        // One handler per turn: state 1 requests 89, whose job work runs on a later turn.
        CreatureStateMachine.TrySetState(context.Hooks, creature, IdleChooserState, context.Tick.Value);
    }

    private bool TryDispatchMoodPrelude(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        context.TremorMoodDispatchedThisTick.Add(creature);
        CreatureMood mood = CreatureAngerRules.MoodOf(definition, creature);
        bool eligible = creature.OwnerPlayerId != PlayerIds.Neutral
            && !definition.Flags.HasFlag(CreatureFlags.IsHorny)
            && !creature.RebelActive
            && !CallToArmsRules.IsUnderCallToArms(context.World, creature);
        // Raw idle and the caller's role/need/combat guards cover the modeled dispatcher gates.
        // Unmodeled body flags and possessed-following are scoped in the Tremor research.
        if (eligible && mood != CreatureMood.Content)
        {
            if (mood == CreatureMood.Angry && TryChooseDispatchedMood(context, creature, definition, mood)) return true;
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
