// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.AI;

/// <summary>Step 12 of the keeper's turn (<c>0x00500C20</c> at <c>0x00500FAE</c>): the research roll,
/// drawn every turn; with allowance and the roll at least <c>ProbabilityOfMovingCreatureForResearch</c>,
/// the researcher move when a spell awaits research and a Library exists, then job placement
/// (<c>0x00503690</c>) unless an attack response is queued.</summary>
/// <remarks>Job placement drops the hand's last item, while it is a creature, at a room one of its
/// happy jobs names (JobRooms, <c>0x006ED490</c>), one allowance a try, the same item retried on a
/// miss; then on <c>random(100) &lt; 10</c> lifts idle own creatures whose job room exists and whose
/// job admits them, one allowance each, for a later turn to drop. Deviates as the training moves
/// do: the keeper's own stream (ADR-0244) and next-tick commands (ADR-0284). ADR-0735.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/computer-keeper-management.md")]
public sealed class KeeperResearchStep
{
    /// <summary>The pick-up scan runs when <c>random(100)</c> is below this.</summary>
    public const int PickUpPercent = 10;

    /// <summary><c>CreatureActivity</c> code 5, the scan's idle creature (<see cref="CreatureActivityRules"/>).</summary>
    private const int IdleActivityCode = 5;

    /// <summary>The scan passes over state 209, a stun the needs group does not cover.</summary>
    private const int ExcludedState = 209;

    private readonly KeeperView _view;
    private readonly KeeperAIState _state;
    private readonly KeeperAIParameters _p;
    private readonly SimRandom _rng;
    private readonly List<PlayerCommand> _commands;
    private readonly KeeperReadiness _readiness;
    private readonly HashSet<int> _prepared;
    private readonly KeeperResearcherMove _move;
    private readonly long _turn;

    public KeeperResearchStep(KeeperView view, KeeperAIState state, KeeperAIParameters parameters, SimRandom rng,
        List<PlayerCommand> commands, KeeperReadiness readiness, HashSet<int> prepared)
    {
        _view = view;
        _state = state;
        _p = parameters;
        _rng = rng;
        _commands = commands;
        _readiness = readiness;
        _prepared = prepared;
        _move = new KeeperResearcherMove(view, rng, commands, prepared);
        // DK2's first game turn is 1; the observation's tick is the turn about to run.
        _turn = view.Tick.Value + 1;
    }

    /// <summary>The roll is drawn whatever the allowance; the rest runs only with allowance left.</summary>
    public int Run(int allowance)
    {
        int roll = _rng.NextInt(3);
        if ((_p.ProbabilityOfMovingCreatureForResearch & 3) > roll || allowance <= 0) return allowance;
        // 0x004C6460(6) only for the first spell at availability 2; the move spends nothing.
        if (ResearchableSpellRemains(_view)
            && _view.RandomRoomOfType(_view.PlayerId, RoomTypeId.Library, _rng) is not null) _move.Run();
        // Every queued event task is a response to an attack (task 1), which holds placement back.
        return _state.Responses.Count == 0 ? PlaceJobs(allowance) : allowance;
    }

    /// <summary><c>0x00503690</c>: the hand's drops, then the pick-up scan.</summary>
    private int PlaceJobs(int allowance)
    {
        int held = _view.HandItems.Count;
        for (; held != 0 && allowance != 0; allowance--)
        {
            if (Creature(_view.HandItems[held - 1]) is not KeeperCreatureView creature) continue;
            foreach (JobType job in Jobs(creature))
            {
                if (KeeperMoodRemedies.JobRoom(job) is not RoomTypeId type
                    || _view.RandomRoomOfType(_view.PlayerId, type, _rng) is not KeeperRoomView room) continue;
                _commands.Add(new DropCommand(_view.PlayerId, _view.PositionOf(room)));
                held--;
                break;
            }
        }

        int capacity = HandRules.Capacity(_view.Database);
        if (_rng.NextInt(100) >= PickUpPercent || allowance == 0 || held >= capacity) return allowance;
        foreach (KeeperCreatureView creature in Owned(_view))
        {
            if (!Scanned(creature)) continue;
            foreach (JobType job in Jobs(creature))
            {
                if (KeeperMoodRemedies.JobRoom(job) is not RoomTypeId type
                    || _view.RandomRoomOfType(_view.PlayerId, type, _rng) is null) continue;
                if (!MayPlace(job) || !creature.PickupPermitted) continue;
                // DK2 rereads the hand's count; the take passes here, permitted with room in the hand.
                _commands.Add(new TakeCreatureCommand(_view.PlayerId, creature.Id));
                _prepared.Add(creature.Id);
                held++;
                allowance--;
                break;
            }
            if (allowance == 0) return 0;
            if (held >= capacity) return allowance;
        }
        return allowance;
    }

    /// <summary>The scan's filter: idle (code 5), not prepared (75) or landing (76), past the
    /// dropped-counter guard, no dropped-to-do job, not in the needs group, not state 209.</summary>
    private bool Scanned(KeeperCreatureView creature)
    {
        int state = StateOf(creature, _prepared);
        if (creature.ActivityCode != IdleActivityCode || state is KeeperTrainingStep.PreparedState or DropLandingRules.LandingState)
            return false;
        if (creature.LastDroppedCounter <= KeeperThreatResponder.DroppedGraceTurns
            && _turn > KeeperThreatResponder.DroppedGraceTurns) return false;
        if (creature.DroppedToDoJob != JobType.None) return false;
        return CreatureStateTable.Get(state).Group != NeedRules.NeedGroup && state != ExcludedState;
    }

    /// <summary>JobRooms' tests (<c>0x00502E90</c> and its siblings): Research while a spell awaits
    /// research, making room for a researcher first when a Library exists; Train while the army is
    /// not ready, readiness granting levels as it asks; Manufacture always; Torture never.</summary>
    private bool MayPlace(JobType job)
    {
        switch (job)
        {
            case JobType.Research:
                if (!ResearchableSpellRemains(_view)) return false;
                if (_view.RandomRoomOfType(_view.PlayerId, RoomTypeId.Library, _rng) is not null) _move.Run();
                return true;
            case JobType.Train: return !_readiness.ArmyReady();
            case JobType.Manufacture: return true;
            default: return false;
        }
    }

    /// <summary>The happy table in order, the descriptor's <c>+0x78B</c> entries.</summary>
    private IEnumerable<JobType> Jobs(KeeperCreatureView creature) =>
        _view.Database.CreatureById(creature.CreatureTypeId).HappyJobs.Select(j => j.Job);

    /// <summary>A held creature, the thing class job placement drops; a body or an object is none.</summary>
    private KeeperCreatureView? Creature(HandItem item)
    {
        if (item.Kind != HandItemKind.Creature) return null;
        foreach (KeeperCreatureView c in _view.Creatures)
            if (c.Id == item.Id) return c.Activity == CreatureActivity.Dead ? null : c;
        return null;
    }

    /// <summary>Spell ids 1 to the descriptor count at availability 2, the state the research
    /// chooser looks for.</summary>
    public static bool ResearchableSpellRemains(KeeperView view)
    {
        int count = view.Database.KeeperSpells.Count;
        for (int id = 1; id <= count; id++)
            if (view.Availability(AvailabilityKind.Spell, id) == 2) return true;
        return false;
    }

    /// <summary>The owner's list in DK2's order, held ones included; a corpse is a different thing.</summary>
    internal static IEnumerable<KeeperCreatureView> Owned(KeeperView view) =>
        view.Creatures.Where(c => c.OwnerPlayerId == view.PlayerId && c.OwnershipRank >= 0
                && c.Activity != CreatureActivity.Dead)
            .OrderBy(c => c.OwnershipRank);

    /// <summary>A creature an earlier step prepared is, in DK2, landing (76) when the hand took and
    /// dropped it, or still in 75 when the hand refused it.</summary>
    internal static int StateOf(KeeperCreatureView creature, HashSet<int> prepared) =>
        !prepared.Contains(creature.Id) ? creature.StateId
            : creature.CanPickUp ? DropLandingRules.LandingState : KeeperTrainingStep.PreparedState;
}
