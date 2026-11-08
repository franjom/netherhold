// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

public enum ClaimActionKind : byte { None = 0, Claim = 4, AttackFloor = 5, ConvertRoom = 10 }

/// <summary>The independent action point and completion state of ordinary actions 4, 5 and 10.</summary>
public struct ClaimActionState
{
    public ClaimActionKind Kind;
    public TilePos Target;
    public bool AnimationComplete;
    public bool OwnsClip;
}

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/imp-claim-action-clock.md")]
internal static class ClaimActionRules
{
    private const byte ArtSlot = 13;
    private static readonly TileWorkSystem Work = new();

    internal static bool UsesClock(CreatureState creature, CreatureDef definition) =>
        creature.TargetTileIndex >= 0 && IsFamily(creature.TargetWork)
        && creature.Dk2StateId == ImpStates.For(creature.TargetWork).Work
        && ActionAnimationClock.CanPlay(definition, ArtSlot);

    internal static bool IsFamily(TileWorkKind kind) =>
        kind is TileWorkKind.Claim or TileWorkKind.AttackTile or TileWorkKind.ConvertRoom;

    internal static void Begin(SimulationContext context, CreatureState creature, CreatureDef definition, TilePos target)
    {
        CreatureStateMachine.EndAction(creature);
        ActionAnimationClock.Begin(creature, definition, ArtSlot, context.Rng);
        ClaimActionKind kind = creature.TargetWork switch
        {
            TileWorkKind.Claim => ClaimActionKind.Claim,
            TileWorkKind.AttackTile => ClaimActionKind.AttackFloor,
            TileWorkKind.ConvertRoom => ClaimActionKind.ConvertRoom,
            _ => throw new InvalidOperationException("Not a claim-family action."),
        };
        creature.ClaimAction = new ClaimActionState { Kind = kind, Target = target, OwnsClip = true };
        creature.ActionHandlerDone = false;
        if (creature.ActionAnimationRate == 0 || creature.ActionAnimationPhase < creature.ActionAnimationRate)
            CreatureActionSounds.Begin(context, "ImpTask", creature, (int)kind);
    }

    internal static void Poll(SimulationContext context, CreatureState creature)
    {
        if (creature.ClaimAction.Kind == ClaimActionKind.None || !context.ClaimActionsPolledThisTick.Add(creature)) return;
        ClaimActionState action = creature.ClaimAction;
        if (!creature.ActionHandlerDone)
        {
            if (Workable(context, creature, action))
            {
                int multiplier = action.Kind == ClaimActionKind.ConvertRoom ? 1
                    : PossessionRules.PossessedMultiplier(context.Database, creature,
                        context.Database.CreatureById(creature.CreatureTypeId));
                context.World.QueueWork(new TileWorkOrder(action.Target.Y * context.World.Width + action.Target.X,
                    WorkKind(action.Kind), creature.OwnerPlayerId, creature.Id, multiplier));
                Work.Execute(context);
            }
            if (Workable(context, creature, action)) creature.ActionHandlerDone = action.Kind != ClaimActionKind.Claim;
            else
            {
                creature.ActionAnimationFlags |= ActionAnimationFlags.Stop;
                ActionAnimationFlags terminal = ActionAnimationFlags.Complete;
                if (action.Kind == ClaimActionKind.ConvertRoom) terminal |= ActionAnimationFlags.Wrapped;
                creature.ActionHandlerDone = (creature.ActionAnimationFlags & terminal) != 0;
            }
        }
        PollAnimation(creature);
        if (creature.ActionHandlerDone && creature.ClaimAction.AnimationComplete) Cancel(creature);
    }

    private static void PollAnimation(CreatureState creature)
    {
        if (creature.ClaimAction.AnimationComplete) return;
        if (creature.ActionAnimationRate == 0) creature.ClaimAction.AnimationComplete = true;
        const ActionAnimationFlags cycle = ActionAnimationFlags.Complete | ActionAnimationFlags.Wrapped;
        if ((creature.ActionAnimationFlags & cycle) == 0) return;
        if (creature.ActionCount == 0) creature.ClaimAction.AnimationComplete = true;
        else if (creature.ActionCount == 1)
        {
            if ((creature.ActionAnimationFlags & ActionAnimationFlags.Complete) != 0) creature.ActionCount = byte.MaxValue;
        }
        else
        {
            creature.ActionCount--;
            if (creature.ActionCount == 1) creature.ActionAnimationFlags &= ~ActionAnimationFlags.Loop;
            else creature.ActionAnimationFlags &= ~cycle;
        }
    }

    private static TileWorkKind WorkKind(ClaimActionKind kind) => kind switch
    {
        ClaimActionKind.Claim => TileWorkKind.Claim,
        ClaimActionKind.AttackFloor => TileWorkKind.AttackTile,
        ClaimActionKind.ConvertRoom => TileWorkKind.ConvertRoom,
        _ => throw new InvalidOperationException("No claim action is running."),
    };

    private static bool Workable(SimulationContext context, CreatureState creature, ClaimActionState action) =>
        context.World.Contains(action.Target)
        && (action.Kind == ClaimActionKind.Claim
            ? TileWorkRules.IsClaimablePath(context.Database, context.World.Tile(action.Target))
            : TileWorkRules.Applies(context.World, context.Database, context.World.Tile(action.Target),
                WorkKind(action.Kind), creature.OwnerPlayerId));

    internal static void Advance(SimulationContext context, CreatureState creature)
    {
        if (!creature.ClaimAction.OwnsClip || !context.ClaimActionsAdvancedThisTick.Add(creature)) return;
        bool sameJob = creature.TargetTileIndex >= 0 && IsFamily(creature.TargetWork)
            && (creature.Dk2StateId == ImpStates.For(creature.TargetWork).Entry
                || creature.Dk2StateId == ImpStates.For(creature.TargetWork).Work);
        if (creature.ClaimAction.Kind == ClaimActionKind.None && !sameJob)
        {
            if (creature.ActionArtSlot == ArtSlot) ActionAnimationClock.Clear(creature);
            creature.ClaimAction = default;
        }
        else if (!StatusEffectRules.IsFrozen(creature))
            ActionAnimationClock.Advance(creature, context.Database.CreatureById(creature.CreatureTypeId));
    }

    internal static void Cancel(CreatureState creature)
    {
        if (!creature.ClaimAction.OwnsClip) return;
        creature.ClaimAction = new ClaimActionState { OwnsClip = true };
        creature.ActionHandlerDone = false;
        creature.ActionCount = 0;
    }
}
