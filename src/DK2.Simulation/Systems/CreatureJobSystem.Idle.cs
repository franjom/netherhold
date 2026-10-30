// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;

namespace DK2.Simulation.Systems;

/// <summary>State 89's tail and state 3 (<c>idle-fallback.md</c>, ADR-0228). When the mood chooser
/// takes nothing, gated or refused, 89 tries <c>0x00478680</c>: Train, job 26 and Drink, each only
/// when the keeper's level makes that room available. Then it sets <c>+0x7E</c> to now + 12 and
/// state 3, where the creature wanders in 87 or plays an idle action until the deadline, and idles.</summary>
public sealed partial class CreatureJobSystem
{
    /// <summary>State 3, "doing nothing" (<c>0x00477680</c>).</summary>
    public const int DoingNothingState = 3;

    /// <summary>89's deadline for 3, <c>+0x7E</c> = now + 12.</summary>
    public const int NothingToDoTicks = 12;

    /// <summary>The creature type whose idle action rolls for a shot (<c>+0x36F</c> 3, the Bile Demon).</summary>
    internal const byte BileDemonTypeId = 3;

    /// <summary>The shot a Bile Demon's idle action makes under 10 of 100 (<c>0x00477801</c>, the
    /// Heal Instant Shot), at its position with no data words, caster or target.</summary>
    private const byte BileDemonIdleShotId = 0x35;
    private const int BileDemonIdleShotChance = 10;

    /// <summary>What 3 writes to <c>+0xA7</c> on entering 87 (<c>0x84</c>); read only for non-zero.</summary>
    private const int WanderStampTicks = 0x84;

    /// <summary>Availability kind 1 is a room; 3 is "available" (<c>0x004BAB20</c>).</summary>
    private const int Available = 3;

    /// <summary>89 after its mood chooser: the fallback, then 3.</summary>
    private static void NothingChosen(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        if (creature.Dk2StateId is not (CreatureStateMachine.Idle or IdleChooserState)) return;
        if (Fallback(context, creature, definition)) return;
        creature.StateDeadlineTick = context.Tick.Value + NothingToDoTicks;
        StateShadow.Move(context, SystemName, creature, DoingNothingState);
    }

    /// <summary><c>0x00478680</c>. Train, unless the definition is <c>NotTrainWhenIdle</c>
    /// (<c>+0x686</c> bit 21), when the Training Room is available; then job 26 when the Combat Pit
    /// is; then Drink when the Casino is. Otherwise a walk resuming 1 to a point two tiles from the
    /// Heart's centre, which 89's <c>SetState(3)</c> replaces at once, so it is not built.</summary>
    private static bool Fallback(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        WorldState world = context.World;
        PlayerId owner = creature.OwnerPlayerId;
        if (!definition.Flags.HasFlag(CreatureFlags.NotTrainWhenIdle) && RoomAvailable(world, owner, RoomTypeId.TrainingRoom)
            && TryTake(context, creature, definition, JobType.Train, roll: -1, "idle", report: false))
            return true;
        if (RoomAvailable(world, owner, RoomTypeId.CombatPit)
            && TryTake(context, creature, definition, JobType.CombatPitSpectate, roll: -1, "idle", report: false))
            return true;
        return RoomAvailable(world, owner, RoomTypeId.Casino)
            && TryTake(context, creature, definition, JobType.Drink, roll: -1, "idle", report: false);
    }

    private static bool RoomAvailable(WorldState world, PlayerId player, RoomTypeId type) =>
        world.Availability(player, AvailabilityKind.Room, (int)type) == Available;

    /// <summary>States 3/4 (<c>0x00477680</c> and <c>0x004776C0</c>): wait out a playing action; at the
    /// deadline, idle. Otherwise, after a wander (<c>+0xA7</c> set), roll: under 25 wander again,
    /// 25–74 action 43, 75–99 action 44. With no wander yet, or on the low roll, 87 with
    /// <c>+0xA7</c> = now + 132. Ours: every creature has both actions, whose length is a guess
    /// (<see cref="ImpTaskSystem.IdleActionGuessTicks"/>).</summary>
    internal static void DoingNothing(SimulationContext context, CreatureState creature)
    {
        long now = context.Tick.Value;
        if (now < creature.ActionUntilTick) return;
        if (creature.StateDeadlineTick <= now)
        {
            StateShadow.ToIdle(context, SystemName, creature);
            return;
        }
        IdleBody(context, creature);
    }

    /// <summary>State 5, a neutral creature's idle (<c>0x004776B0</c>): a bare jump to 3's inner body
    /// <c>0x004776C0</c>, without 3's wait for a running action or its deadline (refused-states.md §1).</summary>
    private static void NeutralIdle(SimulationContext context, CreatureState creature) => IdleBody(context, creature);

    /// <summary><c>0x004776C0</c>, the body 3, 4 and 5 share. After an idle action, a Bile Demon
    /// (<c>+0x36F</c> 3) rolls <c>random(100)</c> again (simulation stream, <c>0x004777D7</c>) and under
    /// 10 makes shot <c>0x35</c> at its position through <c>0x004B7CD0</c> (<c>0x00477806</c>): the Heal
    /// Instant Shot, whose data words are 0 and which no caller aims, so it dies on its first turn
    /// and its death shot heals by 0 (refused-states.md §1). It is built for the entities it makes.</summary>
    private static void IdleBody(SimulationContext context, CreatureState creature)
    {
        long now = context.Tick.Value;
        if (creature.WanderStamp != 0)
        {
            int roll = context.Rng.NextInt(100);
            if (roll >= 25)
            {
                creature.WanderStamp = 0;
                creature.ActionUntilTick = now + ImpTaskSystem.IdleActionGuessTicks;
                context.Emit(SystemName, "idle_action",
                    $"creature={creature.Id} action={(roll < 75 ? 43 : 44)} at=({creature.Tile.X},{creature.Tile.Y})");
                if (creature.CreatureTypeId == BileDemonTypeId && context.Rng.NextInt(100) < BileDemonIdleShotChance
                    && context.Database.Shots.TryGetValue(BileDemonIdleShotId, out ShotDef? shot))
                {
                    // Made in list 0 of 0x004B7830, before list 1 is walked, so the shot pass gives it
                    // its first update this turn (ADR-0551, ADR-0554).
                    ShotState made = ShotRules.Spawn(context.World, shot, creature.OwnerPlayerId, creature.PosX, creature.PosY, 0, 0, 0,
                        FightTargetKind.None, CombatRules.NoTarget, 0, -1, creature.PosX, creature.PosY);
                    ShotRules.RequestTrail(context, made, new Dk2Angle((ushort)creature.Heading), new Fp32(Fp32.One + creature.PosZ));
                }
                return;
            }
        }
        if (CreatureStateMachine.TrySetState(context.Hooks, creature, ImpStates.Wandering)) creature.WanderStamp = now + WanderStampTicks;
        else StateShadow.ToIdle(context, SystemName, creature);
    }
}
