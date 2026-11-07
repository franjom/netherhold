// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

public enum PossessedExcavationKind : byte { None = 0, Dig = 1, Mine = 2 }

/// <summary>The possessed excavation's action index, point and independent animation completion.</summary>
public struct PossessedExcavationState
{
    public PossessedExcavationKind Kind;
    public TilePos Target;
    public bool AnimationComplete;
    /// <summary>Adapter ownership of the shared slot-5 clock through end-of-turn cleanup.</summary>
    public bool OwnsClip;
}

/// <summary>Possessed actions 1/2 use the ordinary slot-5 mode-3 action lifecycle.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/possessed-excavation-clock.md")]
internal static class PossessedExcavationRules
{
    private const byte ArtSlot = 5;
    private static readonly TileWorkSystem Work = new();

    internal static bool TryBegin(SimulationContext context, CreatureState creature, CreatureDef definition,
        PossessedExcavationKind kind, TilePos target)
    {
        // Other diggers and definitions without timing retain their prior queued-work adapter.
        if (creature.CreatureTypeId != CombatRules.ImpCreatureTypeId
            || !ActionAnimationClock.CanPlay(definition, ArtSlot)) return false;
        bool fresh = creature.ActionArtSlot != ArtSlot
            || ((creature.ActionAnimationFlags & (ActionAnimationFlags.Complete | ActionAnimationFlags.Wrapped)) != 0
                && (creature.ActionAnimationFlags & ActionAnimationFlags.Loop) == 0);
        CreatureStateMachine.EndAction(creature);
        creature.ClaimActionTile = -1;
        ActionAnimationClock.Begin(creature, definition, ArtSlot, context.Rng);
        creature.PossessedExcavation = new PossessedExcavationState { Kind = kind, Target = target, OwnsClip = true };
        creature.ActionHandlerDone = false;
        if (fresh) CreatureActionSounds.Begin(context, "possession", creature, (int)kind);
        context.Emit("possession", kind == PossessedExcavationKind.Mine ? "possessed_mine" : "possessed_dig",
            $"creature={creature.Id} tile={target.Y * context.World.Width + target.X}");
        return true;
    }

    internal static void Poll(SimulationContext context, CreatureState creature)
    {
        if (creature.PossessedExcavation.Kind == PossessedExcavationKind.None
            || !context.PossessedExcavationsPolledThisTick.Add(creature)) return;
        if (!creature.ActionHandlerDone)
        {
            PossessedExcavationState action = creature.PossessedExcavation;
            if (Workable(context, action))
            {
                CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
                TileWorkKind work = action.Kind == PossessedExcavationKind.Mine ? TileWorkKind.MineGold : TileWorkKind.Dig;
                TileWorkStrike strike = work == TileWorkKind.Dig
                    ? TileWorkStrike.DiggableWallGuard
                    : TileWorkStrike.None;
                int multiplier = PossessionRules.PossessedMultiplier(context.Database, creature, definition);
                context.World.QueueWork(new TileWorkOrder(action.Target.Y * context.World.Width + action.Target.X,
                    work, creature.OwnerPlayerId, creature.Id, multiplier, strike));
                Work.Execute(context);
            }
            if (Workable(context, action)) creature.ActionHandlerDone = true;
            else
            {
                creature.ActionAnimationFlags |= ActionAnimationFlags.Stop;
                creature.ActionHandlerDone = (creature.ActionAnimationFlags & ActionAnimationFlags.Complete) != 0;
            }
        }
        creature.PossessedExcavation.AnimationComplete |= ActionAnimationClock.AnimationDone(creature);
        if (creature.ActionHandlerDone && creature.PossessedExcavation.AnimationComplete)
        {
            Cancel(creature);
        }
    }

    private static bool Workable(SimulationContext context, PossessedExcavationState action) =>
        context.World.Contains(action.Target) && (action.Kind == PossessedExcavationKind.Mine
            ? TileWorkRules.IsMinableGold(context.Database, context.World.Tile(action.Target))
            : TileWorkRules.IsDiggableWall(context.Database.TerrainById(context.World.Tile(action.Target).TerrainId)));

    internal static void Advance(SimulationContext context, CreatureState creature)
    {
        if (!creature.PossessedExcavation.OwnsClip || !context.PossessedExcavationsAdvancedThisTick.Add(creature)) return;
        if (creature.PossessedExcavation.Kind == PossessedExcavationKind.None)
        {
            if (creature.ActionArtSlot == ArtSlot) ActionAnimationClock.Clear(creature);
            creature.PossessedExcavation = default;
        }
        else if (!StatusEffectRules.IsFrozen(creature))
            ActionAnimationClock.Advance(creature, context.Database.CreatureById(creature.CreatureTypeId));
    }

    internal static void Cancel(CreatureState creature)
    {
        if (!creature.PossessedExcavation.OwnsClip) return;
        creature.PossessedExcavation = new PossessedExcavationState { OwnsClip = true };
        creature.ActionHandlerDone = false;
        creature.ActionCount = 0;
    }
}
