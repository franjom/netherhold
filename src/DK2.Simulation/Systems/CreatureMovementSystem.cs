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
public sealed class CreatureMovementSystem : ICreatureUpdateStep
{
    private const string SystemName = "CreatureMovement";

    /// <summary>The speed setter clamps what it stores at <c>0xFFF</c> entity units a tick.</summary>
    public const int MaxSpeedPerTick = 0xFFF;

    /// <summary>The Imp's creature type id, the one the update's mover gate names.</summary>
    private const int ImpTypeId = 1;

    public string Name => SystemName;

    // The door contact's SelectAVictim needs the fight's caches; ours, as CombatSystem keeps its own.
    private readonly List<Fixed16Vector> _scratch = [];
    private readonly HeartSites _hearts = new();
    private readonly RoomCache _rooms = new();
    private WorldSight? _sight;
    private WorldState? _sightWorld;

    public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);

    public void BeginTurn(SimulationContext context)
    {
        WorldSteering steering = context.Navigation;
        steering.Refresh();
        if (!ReferenceEquals(_sightWorld, context.World))
        {
            _sight = new WorldSight(steering, context.World, context.Database);
            _sightWorld = context.World;
        }
        if (context.World.Doors.Count > 0) _hearts.Refresh(context.World, context.Database);

        // Every body on its creature first, so a neighbour read by Separation or the crowd probe is
        // where the creature is and as big as it is, whether or not it moves this tick or has ever
        // moved. Its pace is set before its own step, after its handler has run.
        foreach (CreatureState creature in context.World.Creatures)
        {
            WorldSteering.SyncBody(creature);
            creature.Body.Radius = SteeringMover.CrowdWordOf(
                context.Database.CreatureById(creature.CreatureTypeId).FootprintRadius.Raw);
        }
    }

    /// <summary>The end of the creature update: the physical commit and the steering step
    /// (<c>0x0048A204</c>, <c>0x0048A266</c>), after the fight and the state handler have set this
    /// tick's route and pace. Creatures updated earlier this tick have moved already, so an earlier
    /// mover's tile crossing is in a later mover's crowd query (imp-first-integration.md).</summary>
    public void Update(SimulationContext context, CreatureState creature)
    {
        // The previous frame's position (DK2's vtable+0x2C): where the creature stood before
        // this tick's move, which a shot's aim leads by (shots.md §7).
        creature.LastPosX = creature.PosX;
        creature.LastPosY = creature.PosY;
        Sync(context, creature);

        if (CreatureEligibility.IsExcluded(creature, CreatureGuards.Sidelined)) return;
        if (TremorRules.IsRecovering(creature) || context.TremorRecoveryCompletedThisTick.Contains(creature)) return;
        if (creature.TremorRoutePaused)
        {
            if (!CreatureStateMachine.HasState(creature)
                || (CreatureStateTable.Get(creature.Dk2StateId).Flags & CreatureStateFlags.Unread0) == 0) return;
            creature.TremorRoutePaused = false;
        }

        // Native fight states 199/200 have the movement bit even without a route: contact
        // steering and braking still run (combat-spacing.md).
        // Other states retain the route gate until their movement dispatch is mirrored. The
        // approach, state 8, keeps 199's exemption it had as a flag: whether its mover
        // (0x00470C80) steers with no route is unread (ADR-0483).
        if (creature.Body.PathTotalCount == 0
            && creature.Dk2StateId is not (CombatStates.Engaging or 200 or CombatStates.Approaching)) return;

        if (creature.CreatureTypeId == ImpTypeId)
        {
            // DK2 moves an imp only in its walking states, 6, 7 and 265 (the update's gate skips type
            // id 1 at 0x0048A202; state 6 integrates it itself), so a kicked or working imp stands
            // (imp-task-objects-and-walks.md §3). Ours routes imps in states of its own too, so the
            // gate names the ones DK2 is read to hold still: selection 10–12 and the work 13–26.
            if (creature.Dk2StateId is >= 10 and <= 26) return;
            // State 6's handler is the imp's step, and the dispatcher ran the state the update
            // began in: an imp sent walking during this update takes its first step next turn
            // (walk-start-timing-2026-10-27: a native imp stepped on the tick it entered 6 in 22 of 3,891 starts).
            if (creature.Dk2StateId == StateShadow.TravelToPoint
                && context.UpdateStartStateId != StateShadow.TravelToPoint) return;
        }

        Advance(context, context.Navigation, creature, context.Database.CreatureById(creature.CreatureTypeId));
    }

    /// <summary>The body on its creature, with the definition's limits and this tick's pace.</summary>
    private static void Sync(SimulationContext context, CreatureState creature)
    {
        WorldSteering.SyncBody(creature);
        CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
        creature.Body.Radius = SteeringMover.CrowdWordOf(definition.FootprintRadius.Raw);
        creature.Body.MaxForce = SeparationRules.ShippedMaxForce;
        // A party's shared pace holds until the creature runs (ADR-0216).
        if (creature.Running) creature.PartySpeedRaw = 0;
        int cap = creature.PartySpeedRaw > 0
            ? Math.Min(SimulationTiming.LoaderRatePerTick(creature.PartySpeedRaw), MaxSpeedPerTick) << 4
            : MaxSpeedOf(definition, creature.Running);
        creature.Body.MaxSpeed = StatusEffectRules.SpeedCap(context.Database, definition, creature, cap);
        TerrainDef terrain = context.Database.TerrainById(context.World.Tile(creature.Tile).TerrainId);
        creature.Body.MaxSpeed = WaterSpeedRules.SpeedCap(terrain, definition, creature, creature.Body.MaxSpeed);
        PossessionRules.ApplySpeedModifier(context.Database, creature);
    }

    /// <summary>The body's speed cap in 16.16 tiles a tick: the definition's speed divided by the
    /// tick rate as the loader does it, clamped, shifted up four. <c>0x004887D0</c> takes it from
    /// the shared definition (<c>+0x5B4</c> walking, <c>+0x5B8</c> running), so level never
    /// changes it (ADR-0518).</summary>
    public static int MaxSpeedOf(CreatureDef definition, bool running = false)
    {
        int raw = running ? definition.RunSpeed.Raw : definition.Speed.Raw;
        int perTick = SimulationTiming.LoaderRatePerTick(raw);
        return Math.Min(perTick, MaxSpeedPerTick) << 4;
    }

    private void Advance(SimulationContext context, WorldSteering steering, CreatureState creature, CreatureDef definition)
    {
        GameDatabase database = context.Database;
        SteeringBody body = creature.Body;

        // The sync keeps only the entity's 1/4096 precision of both position and velocity
        // (0x004D59A0 after 0x004D5AF0).
        body.Vel = new Fixed16Vector((body.Vel.X >> 4) << 4, (body.Vel.Y >> 4) << 4);

        var mover = new SteeringMover
        {
            IsWorker = definition.Flags.HasFlag(CreatureFlags.IsWorker),
            Heading = creature.Heading,
            TurnRate = SimulationTiming.TurnRatePerTick(definition),
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
        // The door contact (0x0048A2EB) runs on the new position's tile and, when it forces a
        // door fight, cancels the commit; a door is never a solid tile here (door-blocking.md).
        if (creature.CreatureTypeId != ImpTypeId && DoorContact(context, creature, definition, x / Fp32.One, y / Fp32.One))
        {
            WorldSteering.SyncBody(creature);
            return;
        }
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

    /// <summary>The walking arm's door contact (<c>0x0048A2EB</c>–<c>0x0048A44A</c>): a completed
    /// door on the tile, hostile to the mover (no closed test, Q454), or shut and the mover a rebel,
    /// with the mover not neutral and allowed a forced fight (<c>0x0045A6D0(1)</c>). Any fight is let
    /// go and any old forced target dropped; the door becomes the forced target, the route is
    /// stopped and <c>SelectAVictim</c> takes it. DK2 also clears <c>IgnoringDoors</c> and
    /// <c>IgnoringLockedRegion</c> (bits 17, 18), which nothing here sets.</summary>
    private bool DoorContact(SimulationContext context, CreatureState creature, CreatureDef definition, int tileX, int tileY)
    {
        WorldState world = context.World;
        if (world.Doors.Count == 0 || world.DoorAt(tileX, tileY) is not { UnderConstruction: false } door) return false;
        bool hostile = !world.AreAllied(creature.OwnerPlayerId, door.PlayerId);
        if (!hostile && !(creature.RebelActive && !door.Open)) return false;
        if (creature.OwnerPlayerId == PlayerIds.Neutral) return false;
        if (!CombatRules.MayLookForAFight(world, context.Database, creature, force: true)) return false;

        if (CombatRules.InCombat(creature)) CombatRules.Disengage(context.Hooks, creature);
        if (creature.AttackingId != CombatRules.NoTarget) CombatPitRules.ClearForcedTarget(world, creature);
        creature.AttackAbnormalTargets = true;
        creature.AttackingId = door.Id;
        creature.AttackingKind = FightTargetKind.Door;
        creature.StopMoving();
        context.Emit(SystemName, "meets_door", $"creature={creature.Id} door={door.Id} player={creature.OwnerPlayerId} door_player={door.PlayerId}");
        CombatTargetRules.SelectAVictim(context, _sight!, _hearts, _rooms, creature, definition, _scratch);
        return true;
    }
}
