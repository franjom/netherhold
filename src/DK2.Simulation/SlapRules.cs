// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

/// <summary>The slap's state moves and its reaction (<c>slap-states.md</c>, ADR-0401). The slap
/// <c>0x0048B8A0</c> moves a creature holding a place in its slapper's Workshop to 67 and in its
/// slapper's Prison to 120, each ending a running action first, and anyone else to 77; refused, the
/// slap does nothing more. 67, 77 and 120 share the reaction <c>0x0046F1F0</c>: action 19, then
/// back to its room's state (63, 115, 130), a worker to 10, anyone else idle.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/slap-states.md")]
public static class SlapRules
{
    [Dk2Behavior(Confidence.Confirmed, "docs/research/casino-winner-slap.md")]
    public static int SlapCasinoWinner(SimulationContext context, CreatureState winner)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        int quarter = (ushort)winner.JackpotWinnings / 4;
        PurseRules.Spend(winner, Math.Min(quarter, winner.GoldHeld));
        int anger = CreatureAngerRules.Apply(world, database.CreatureById(winner.CreatureTypeId), winner,
            AngerBucket.Treatment, winner.Anger[AngerBucket.Treatment] * 25 / 100, AngerComplaint.Slapped);
        if (winner.CasinoBigWinnerSlappedCount != 0)
        {
            world.Messages.Post(world, winner.OwnerPlayerId, PlayerMessageKind.JackpotWinnerSlapped, 0, 0);
            foreach (CreatureState other in world.OwnedCreatures(winner.OwnerPlayerId))
                if (other != winner)
                    CreatureAngerRules.Apply(world, database.CreatureById(other.CreatureTypeId), other,
                        AngerBucket.Treatment, 5000, AngerComplaint.Slapped);
        }
        // The second cap is against the already debited purse; see the binary's ordered effects.
        int amount = Math.Min(quarter, winner.GoldHeld);
        int count = FloorGold.PileCount(amount);
        if (count > 0)
        {
            var tile = new TilePos(winner.PosX >> 12, winner.PosY >> 12);
            for (int i = 0; i < count; i++)
            {
                int x = (winner.PosX & ~4095) | context.Rng.NextInt(4096);
                int y = (winner.PosY & ~4095) | context.Rng.NextInt(4096);
                if (FloorGold.Drop(world, database, world.TileIndex(tile), amount / count) is ObjectState pile)
                {
                    pile.PosX = x;
                    pile.PosY = y;
                    pile.PosZ = 0x3000;
                    pile.Cell = (byte)(Math.Min((y & 4095) / 0x555, 2) * 3 + Math.Min((x & 4095) / 0x555, 2));
                }
            }
        }
        winner.CasinoBigWinnerSlappedCount = unchecked((byte)(winner.CasinoBigWinnerSlappedCount + 1));
        return anger;
    }

    /// <summary>67: slapped in its keeper's Workshop (<c>0x0048B9DB</c>).</summary>
    public const int WorkshopState = 67;

    /// <summary>77: slapped anywhere else (<c>0x0048B9FE</c>).</summary>
    public const int ReactionState = 77;

    /// <summary>120: slapped in its keeper's Prison.</summary>
    public const int PrisonState = 120;

    /// <summary>The reaction's action, <c>0x13</c>, and the art slot its record plays (byte
    /// <c>+0x14</c> at <c>0x006E43D3</c>, written by the filler at <c>0x004956A0</c>).</summary>
    public const int ReactionAction = 19, ReactionArtSlot = 4;

    public static bool IsReaction(int state) => state is WorkshopState or ReactionState or PrisonState;

    /// <summary>The slap's move: 67 for a place in a Workshop, 120 in a Prison (each ending a running
    /// action, <c>0x00496D20</c>), else 77. <paramref name="room"/> is the room it holds a place in,
    /// already known to be the slapper's. Returns whether the move was taken.</summary>
    internal static bool Move(SimulationContext context, CreatureState creature, RoomInstance? room)
    {
        int target = room?.Definition.TypeId switch
        {
            RoomTypeId.Workshop => WorkshopState,
            RoomTypeId.Prison => PrisonState,
            _ => ReactionState,
        };
        if (target != ReactionState) creature.ActionUntilTick = 0;
        if (!CreatureStateMachine.TrySetState(context.Hooks, creature, target, context.Tick.Value)) return false;
        // DK2 moves only a walking state and the reaction zeroes the velocity, so the walk ends here;
        // ours' mover would carry on along a loaded route (CasinoJackpotRules.Kick does the same).
        creature.StopMoving();
        return true;
    }

    /// <summary><c>0x0046F1F0</c>, a turn of 67, 77 or 120. At <c>Age</c> 1 a running action ends and
    /// action 19 starts. While it runs, it holds. Then: a Horny to 238 (not reached: ours refuses a
    /// slap on a Horny, <see cref="HandRules.CanSlap"/>); a place in a Workshop to 63, a Prison to 115, a Torture Chamber to 130;
    /// otherwise a worker to 10; otherwise, or refused, idle. The knock-back's velocity, which the
    /// reaction zeroes, is not modelled.</summary>
    public static void React(SimulationContext context, string system, CreatureState creature, CreatureDef definition)
    {
        long now = context.Tick.Value;
        long age = now - creature.StateEnteredTick;
        if (age == 0) return;
        if (age == 1)
        {
            creature.ActionUntilTick = now + AnimationTimingRules.CountedHoldTicks(definition, ReactionArtSlot, 0);
            context.Emit(system, "slap_reaction", $"creature={creature.Id} state={creature.Dk2StateId} until={creature.ActionUntilTick}");
            return;
        }
        if (now < creature.ActionUntilTick) return;

        WorldState world = context.World;
        RoomInstance? room = creature.HeldPlace.IsHeld ? RoomIndex.RoomAt(world, context.Database, creature.HeldPlace.TileIndex) : null;
        int? back = room?.Definition.TypeId switch
        {
            RoomTypeId.Workshop => WorkshopStep.ChoosingState,
            RoomTypeId.Prison => PrisonRules.DecidingState,
            RoomTypeId.TortureChamber => TortureRules.StrappingState,
            _ => null,
        };
        if (back is int state)
        {
            if (!StateShadow.TryMove(context, system, creature, state)) StateShadow.ToIdle(context, system, creature);
            return;
        }
        if (!definition.Flags.HasFlag(CreatureFlags.IsWorker)
            || !CreatureStateMachine.TrySetState(context.Hooks, creature, ImpStates.TaskSelection, now))
            StateShadow.ToIdle(context, system, creature);
    }
}
