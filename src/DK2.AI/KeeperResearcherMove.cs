// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.AI;

/// <summary>Making room for a researcher (<c>0x004FA8D0</c>): with fewer than two creatures that can
/// research, the non-researcher with the smallest descriptor byte <c>+0x787</c> is dropped on a Portal,
/// where it leaves, so a researcher can come; only at the portal creature cap, and spending no
/// allowance. Called by step 12, the Research job's placement test and the attack planner.</summary>
/// <remarks>Deviates as the training moves do: the keeper's own stream (ADR-0244) and next-tick
/// lift and drop commands (ADR-0284). The byte's meaning is UNKNOWN (ADR-0735).</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/computer-keeper-management.md")]
public sealed class KeeperResearcherMove
{
    /// <summary>The leaving states the move passes over: 197 after a Portal drop, and 196.</summary>
    private const int PortalLeavingState = 197, LeavingState = 196;

    private readonly KeeperView _view;
    private readonly SimRandom _rng;
    private readonly List<PlayerCommand> _commands;
    private readonly HashSet<int> _prepared;
    private readonly long _turn;

    public KeeperResearcherMove(KeeperView view, SimRandom rng, List<PlayerCommand> commands, HashSet<int> prepared)
    {
        _view = view;
        _rng = rng;
        _commands = commands;
        _prepared = prepared;
        // DK2's first game turn is 1; the observation's tick is the turn about to run.
        _turn = view.Tick.Value + 1;
    }

    public void Run()
    {
        int researchers = 0, least = int.MaxValue;
        KeeperCreatureView? candidate = null;
        foreach (KeeperCreatureView creature in KeeperResearchStep.Owned(_view))
        {
            CreatureDef definition = _view.Database.CreatureById(creature.CreatureTypeId);
            if ((definition.Flags & CreatureFlags.IsWorker) == CreatureFlags.IsWorker) continue;
            if (definition.HappyJobs.Any(j => j.Job == JobType.Research)) researchers++;
            // Equal values replace the candidate: the list's last minimum wins.
            else if (definition.Unknown_0F4C <= least)
            {
                least = definition.Unknown_0F4C;
                candidate = creature;
            }
        }
        if (researchers >= 2 || candidate is not KeeperCreatureView leaver) return;

        // A Portal, else a Hero Portal; then the cap, which counts both kinds.
        KeeperRoomView? portal = _view.RandomRoomOfType(_view.PlayerId, RoomTypeId.Portal, _rng)
            ?? _view.RandomRoomOfType(_view.PlayerId, RoomTypeId.HeroPortal, _rng);
        if (portal is null) return;
        int portals = _view.OwnRooms.Count(r => PortalRules.IsEntrance(r.Definition));
        int came = KeeperResearchStep.Owned(_view).Count(c => c.CameThroughPortal);
        if (PortalRules.CreatureCapacity(_view.Database, portals) > came) return;

        if (leaver.LastDroppedCounter <= KeeperThreatResponder.DroppedGraceTurns
            && _turn > KeeperThreatResponder.DroppedGraceTurns) return;
        if (leaver.InMyHand) return;
        int state = KeeperResearchStep.StateOf(leaver, _prepared);
        if (state is DropLandingRules.LandingState or PortalLeavingState or LeavingState
            || KeeperThreatResponder.IsStunned(state)) return;
        if (!leaver.PickupPermitted) return;
        _commands.Add(new TakeCreatureCommand(_view.PlayerId, leaver.Id));
        _prepared.Add(leaver.Id);
        if (leaver.CanPickUp) _commands.Add(new DropCommand(_view.PlayerId, _view.PositionOf(portal)));
    }
}
