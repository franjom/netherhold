// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Systems;

/// <summary>The Call to Arms flag's process (the object states of <c>0x0049E686</c>'s table),
/// the slap that removes it (action 63) and the three heeding states 190 to 192.</summary>
/// <remarks>Deviates: a handler that changes the flag's state runs the next state in the same
/// tick, so activation reaches the recall and a fade reaches the removal without a tick between;
/// DK2's object update order between those states was not read. The flag's effects and sound
/// are display and absent. A flag in the hand is not processed (ADR-0279).</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/call-to-arms.md")]
public sealed class CallToArmsSystem : ISimulationSystem
{
    public const string SystemName = "call_to_arms";
    public string Name => SystemName;

    /// <summary>The 192 wander's four directions in table order (<c>0x006BC350</c>): up, right,
    /// down, left; the walk starts at a random one and tries the next three.</summary>
    private static readonly (int Dx, int Dy)[] Directions = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    /// <summary>The nine sub-tile positions of <c>0x00698EC8</c>, a three-by-three grid.</summary>
    private static readonly (int X, int Y)[] SubTile =
    [
        (682, 682), (2047, 682), (3412, 682), (682, 2047), (2047, 2047), (3412, 2047),
        (682, 3412), (2047, 3412), (3412, 3412),
    ];

    private readonly List<ObjectState> _flags = new();
    private readonly List<CreatureState> _creatures = new();

    public void Execute(SimulationContext context)
    {
        foreach (RemoveCallToArmsCommand remove in context.Commands<RemoveCallToArmsCommand>())
        {
            if (!CallToArmsRules.Remove(context, SystemName, remove.PlayerId))
                context.Emit(SystemName, "refused", $"player={remove.PlayerId} command=remove_call_to_arms");
        }

        // The creatures' states first, then the flags: a creature the recruit sends to 190
        // this tick runs 190 next tick. DK2's order between its creature and object lists is
        // not read (ADR-0279).
        WorldState world = context.World;
        _creatures.Clear();
        _creatures.AddRange(world.Creatures);
        foreach (CreatureState creature in _creatures)
        {
            if (CreatureEligibility.IsExcluded(creature, CreatureGuards.Sidelined)
                || !CreatureStateMachine.HasState(creature)) continue;
            if (StateShadow.IsTravelling(creature))
            {
                if (creature.UtilityNewState is not (CallToArmsRules.ArrivedState or CallToArmsRules.MillingState)) continue;
                if (creature.HasRoute) continue;
                creature.StopMoving();
                StateShadow.Arrive(context, SystemName, creature);
                continue;
            }
            switch (creature.Dk2StateId)
            {
                case CallToArmsRules.GoingState: Going(context, creature); break;
                case CallToArmsRules.ArrivedState: Arrived(context, creature); break;
                case CallToArmsRules.MillingState: Milling(context, creature); break;
            }
        }

        _flags.Clear();
        foreach (ObjectState thing in world.Objects)
            if (thing.ObjectTypeId == CallToArmsRules.ObjectTypeId && !thing.InHand) _flags.Add(thing);
        foreach (ObjectState flag in _flags) RunFlag(context, flag);
    }

    /// <summary>One flag's process states, chained until a handler leaves the state alone.</summary>
    private static void RunFlag(SimulationContext context, ObjectState flag)
    {
        WorldState world = context.World;
        long now = context.Tick.Value;
        if (flag.AnimationStartedTick < 0) flag.AnimationStartedTick = now;
        for (int guard = 0; guard < 8; guard++)
        {
            byte before = flag.ProcessState;
            switch (flag.ProcessState)
            {
                case CallToArmsRules.Activating:
                    CallToArmsRules.Activate(context, SystemName, flag);
                    if (world.ObjectById(flag.Id) is null) return;
                    if (!AnimationEnded(context, flag, 0)) return;
                    flag.ProcessState = CallToArmsRules.Activated;
                    flag.AnimationStartedTick = now;
                    break;
                case CallToArmsRules.Activated:
                    if (flag.ProcessEnding)
                    {
                        flag.ProcessState = CallToArmsRules.Releasing;
                        flag.AnimationStartedTick = now;
                        break;
                    }
                    flag.ProcessCount = 0;
                    flag.ProcessState = CallToArmsRules.Recalling;
                    flag.ProcessDeadline = now + SimulationTiming.OriginalTicksPerGameSecond;
                    break;
                case CallToArmsRules.Recalling:
                    if (flag.ProcessEnding)
                    {
                        flag.ProcessState = CallToArmsRules.Releasing;
                        flag.AnimationStartedTick = now;
                        break;
                    }
                    if (flag.ProcessDeadline < now)
                    {
                        CallToArmsRules.Activate(context, SystemName, flag);
                        if (world.ObjectById(flag.Id) is null) return;
                        flag.ProcessDeadline = now + SimulationTiming.OriginalTicksPerGameSecond;
                        // Every third recall plays the flag's effect, which is display.
                        flag.ProcessCount = (byte)((flag.ProcessCount + 1) % 3);
                    }
                    break;
                case CallToArmsRules.Releasing:
                    flag.ProcessEnding = true;
                    if (!AnimationEnded(context, flag, 7)) return;
                    CallToArmsRules.Deactivate(context, SystemName, flag.OwnerPlayerId);
                    world.RemoveObject(flag.Id);
                    return;
                case CallToArmsRules.Fading:
                    flag.ProcessEnding = true;
                    flag.ProcessState = CallToArmsRules.Faded;
                    flag.AnimationStartedTick = now;
                    break;
                case CallToArmsRules.Faded:
                    flag.ProcessEnding = true;
                    if (!AnimationEnded(context, flag, 6)) return;
                    world.RemoveObject(flag.Id);
                    context.Emit(SystemName, "call_to_arms_faded", $"player={flag.OwnerPlayerId} flag={flag.Id}");
                    return;
                default:
                    return;
            }
            if (flag.ProcessState == before) return;
        }
    }

    private static bool AnimationEnded(SimulationContext context, ObjectState flag, int slot)
    {
        var timing = context.Database.ObjectById(flag.ObjectTypeId)?.ArtSlot(slot) ?? default;
        return timing.ResourceType != 6 || context.Tick.Value - flag.AnimationStartedTick
            >= AnimationTimingRules.FirstCycleHoldTicks(timing);
    }

    /// <summary>190 (<c>0x00455DA0</c>): the call gone ends it; otherwise walk to the flag's tile
    /// centre resuming 191, and a failed route sends message 84 and ends it.</summary>
    private static void Going(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        if (!CallToArmsRules.IsOn(world, creature.OwnerPlayerId)) { Release(context, creature); return; }
        TilePos tile = world.Player(creature.OwnerPlayerId).CallToArms.Tile;
        if (context.Navigation.TryRoute(creature, tile, 0))
        {
            CreatureStateMachine.BeginTravel(context.Hooks, creature, StateShadow.TravelToPoint, CallToArmsRules.ArrivedState);
            return;
        }
        world.Messages.Post(world, creature.OwnerPlayerId, PlayerMessageKind.CallToArmsUnreachable, 0, 0);
        context.Emit(SystemName, "call_to_arms_unreachable", $"player={creature.OwnerPlayerId} creature={creature.Id}");
        Release(context, creature);
    }

    /// <summary>191 (<c>0x00455EA0</c>): the call gone ends it; a creature whose tile differs from
    /// the flag's on both axes is sent to 190, and 192 is set over that either way.</summary>
    private static void Arrived(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        if (!CallToArmsRules.IsOn(world, creature.OwnerPlayerId)) { Release(context, creature); return; }
        TilePos tile = world.Player(creature.OwnerPlayerId).CallToArms.Tile;
        if (creature.Tile.X != tile.X && creature.Tile.Y != tile.Y)
            CreatureStateMachine.TrySetState(context.Hooks, creature, CallToArmsRules.GoingState, context.Tick.Value);
        StateShadow.Move(context, SystemName, creature, CallToArmsRules.MillingState);
    }

    /// <summary>192 (<c>0x00455F30</c>): from a random direction, the first neighbouring tile
    /// within two of the flag's tile that is open takes a walk to one of nine sub-tile spots,
    /// resuming 192; none ends the heeding. The call itself is not tested here.</summary>
    private static void Milling(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        TilePos call = world.HasPlayer(creature.OwnerPlayerId) ? world.Player(creature.OwnerPlayerId).CallToArms.Tile : creature.Tile;
        int start = context.Rng.NextInt(4);
        context.Navigation.Refresh();
        for (int i = 0; i < 4; i++)
        {
            (int dx, int dy) = Directions[(start + i) & 3];
            int x = creature.Tile.X + dx, y = creature.Tile.Y + dy;
            if (Math.Max(Math.Abs(x - call.X), Math.Abs(call.Y - y)) >= 3) continue;
            if (!world.Contains(x, y) || (context.Navigation.NavByte(x, y) & 8) != 0) continue;
            (int subX, int subY) = SubTile[context.Rng.NextInt(9)];
            if (!context.Navigation.TryRouteTo(creature, (x << 12) | subX, (y << 12) | subY, 0)) continue;
            CreatureStateMachine.BeginTravel(context.Hooks, creature, StateShadow.TravelToPoint, CallToArmsRules.MillingState);
            return;
        }
        creature.HeedingCallToArms = false;
        StateShadow.ToIdle(context, SystemName, creature);
    }

    private static void Release(SimulationContext context, CreatureState creature)
    {
        creature.HeedingCallToArms = false;
        StateShadow.ToIdle(context, SystemName, creature);
    }
}
