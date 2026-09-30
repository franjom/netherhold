// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation.Combat;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;

namespace DK2.Simulation.Systems;

/// <summary>DK2's per-tick mover update (<c>0x004D5A40</c> and its four callers;
/// docs/research/steering-behaviours.md §7.5): every creature that has a route or is still moving
/// re-derives its body from its position, runs the four weighted behaviours through the Euler
/// step, and takes the new position if the tile under it is not solid; otherwise the body is put
/// back on the entity with no velocity. The body's speed cap is the definition's speed at the
/// creature's level, per tick, capped at just under a tile (<c>0x0048D010</c>).</summary>
/// <remarks>Behaviours run in their registration order (navigation-and-steering.md §3.3). The
/// solid-tile test is the main update's (<c>0x004B35C0</c> at <c>0x0048A2B3</c>); three other
/// callers commit unconditionally, and which creature states use which is UNKNOWN.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/steering-behaviours.md")]
public sealed class CreatureMovementSystem : ISimulationSystem
{
    private const string SystemName = "CreatureMovement";

    /// <summary>The speed setter clamps what it stores at <c>0xFFF</c> entity units a tick.</summary>
    public const int MaxSpeedPerTick = 0xFFF;

    /// <summary>The Imp's creature type id, the one the update's mover gate names.</summary>
    private const int ImpTypeId = 1;

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        WorldSteering steering = context.Navigation;
        steering.Refresh();

        // Every body on its creature first, with its definition's limits, so a neighbour read by
        // Separation or the crowd probe is where the creature is and as big as it is, whether or
        // not it moves this tick or has ever moved.
        foreach (CreatureState creature in context.World.Creatures)
        {
            // The previous frame's position (DK2's vtable+0x2C): where the creature stood before
            // this tick's move, which a shot's aim leads by (shots.md §7).
            creature.LastPosX = creature.PosX;
            creature.LastPosY = creature.PosY;
            WorldSteering.SyncBody(creature);
            CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
            creature.Body.Radius = SteeringMover.CrowdWordOf(definition.FootprintRadius.Raw);
            creature.Body.MaxForce = SeparationRules.ShippedMaxForce;
            // A party's shared pace holds until the creature runs (ADR-0216).
            if (creature.Running) creature.PartySpeedRaw = 0;
            int cap = creature.PartySpeedRaw > 0
                ? Math.Min(SimulationTiming.LoaderRatePerTick(creature.PartySpeedRaw), MaxSpeedPerTick) << 4
                : MaxSpeedOf(context.Database, definition, creature.Level, creature.Running);
            creature.Body.MaxSpeed = StatusEffectRules.SpeedCap(context.Database, definition, creature, cap);
        }

        foreach (CreatureState creature in context.World.Creatures)
        {
            if (HandRules.IsHeld(creature) || CreatureDeathRules.IsDown(creature)) continue;

            // Only a creature in a walking state is moved (the state table's bit at 0x0048A23B).
            // Ours: one with a route loaded, arrived or not, so the arrival tick still takes its
            // braking step and the state that follows stands where it stopped.
            if (creature.Body.PathTotalCount == 0) continue;

            // DK2 moves an imp only in its walking states, 6, 7 and 265 (the update's gate skips type
            // id 1 at 0x0048A202; state 6 integrates it itself), so a kicked or working imp stands
            // (imp-task-objects-and-walks.md §3). Ours routes imps in states of its own too, so the
            // gate names the ones DK2 is read to hold still: selection 10–12 and the work 13–26.
            if (creature.CreatureTypeId == ImpTypeId && creature.Dk2StateId is >= 10 and <= 26)
                continue;

            Advance(context.Database, steering, creature, context.Database.CreatureById(creature.CreatureTypeId));
        }
    }

    /// <summary>The body's speed cap in 16.16 tiles a tick: the level-scaled definition speed
    /// divided by the tick rate as the loader does it, clamped, shifted up four.</summary>
    public static int MaxSpeedOf(GameDatabase database, CreatureDef definition, int level)
    {
        return MaxSpeedOf(database, definition, level, running: false);
    }

    /// <summary>The same cap for a creature set running (<c>0x004887D0(1)</c>, the fight and the
    /// withdrawal): the record's <c>RunSpeed</c>, scaled by the Speed row like the walk
    /// (STRONGLY INFERRED: the stat setter scales all three speeds together).</summary>
    public static int MaxSpeedOf(GameDatabase database, CreatureDef definition, int level, bool running)
    {
        int raw = running
            ? CreatureLevelRules.Scale(definition.RunSpeed.Raw, CreatureLevelRules.Percent(database, CreatureStatId.Speed, level))
            : CreatureLevelRules.SpeedRaw(database, definition, level);
        int perTick = SimulationTiming.LoaderRatePerTick(raw);
        return Math.Min(perTick, MaxSpeedPerTick) << 4;
    }

    private static void Advance(GameDatabase database, WorldSteering steering, CreatureState creature, CreatureDef definition)
    {
        SteeringBody body = creature.Body;

        // The sync keeps only the entity's 1/4096 precision of both position and velocity
        // (0x004D59A0 after 0x004D5AF0).
        body.Vel = new Fixed16Vector((body.Vel.X >> 4) << 4, (body.Vel.Y >> 4) << 4);

        var mover = new SteeringMover
        {
            IsWorker = definition.Flags.HasFlag(CreatureFlags.IsWorker),
            Heading = creature.Heading,
            TurnRate = SimulationTiming.LoaderRatePerTick(definition.TurnRate),
            SizeClass = definition.SizeClass,
            CrowdWord = body.Radius,
            Profile = WorldSteering.ProfileOf(definition, creature.OwnerPlayerId, creature.RouteOptions),
        };

        Fixed16Vector avoidWalls = AvoidWallsRules.Compute(body, mover.Profile, steering);
        Fixed16Vector repel = RepelObstacleRules.Compute(body, steering);
        Fixed16Vector followPath = FollowPathRules.Compute(body, mover, steering);
        Fixed16Vector separation = SeparationBehaviour.Compute(body, mover, steering);
        SteeringIntegrator.Step(body, SteeringIntegrator.Combine(body, avoidWalls, repel, followPath, separation), mover.Profile, steering);
        creature.Heading = mover.Heading;

        int x = body.Pos.X >> 4, y = body.Pos.Y >> 4;
        // UNKNOWN integration: 0x0048A2EB can cancel this move and force a door fight.
        // Q453 tracks its thing-target spell survey, reach and destruction dependencies;
        // do not substitute a solid tile for that combat transition (door-blocking.md).
        if (!NavBytes.IsSolid(steering.NavByte(x / Fp32.One, y / Fp32.One)))
        {
            creature.PosX = x;
            creature.PosY = y;
        }
        else
        {
            body.Vel = Fixed16Vector.Zero;
        }

        // The entity's position is the one that persists; the body's is derived from it again.
        WorldSteering.SyncBody(creature);
    }
}
