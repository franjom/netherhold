// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Navigation;

namespace DK2.Simulation.Systems;

/// <summary>The shot thing's tick (docs/research/shots.md §4, <c>0x004AB180</c>), for every shot in
/// flight: a hit on a creature or the heart it was sent at, else a move in two half-steps
/// stopped by solid ground — a bounce for a shot that bounces, otherwise a stop that kills a shot
/// flagged to die on solid; the area damage some shots deal each tick; the lifetime counting down;
/// death chaining a death shot; the process handler on the first tick and on the thing struck,
/// which becomes the shot's target. Shots are list 1 of the frame's entity pass (<c>0x004B7830</c>),
/// walked newest first; a shot made while the list is walked (a chained shot) or after it (a
/// trap's) waits for the next turn, and one a creature makes in its own update moves this turn
/// (ADR-0367, ADR-0551).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/shots.md")]
public sealed class ShotSystem : ISimulationSystem
{
    private const string SystemName = "shots";
    private readonly List<ShotState> _work = [];

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        if (world.Shots.Count == 0) return;
        _work.Clear();
        _work.AddRange(world.Shots);

        for (int i = _work.Count - 1; i >= 0; i--) Update(context, _work[i]);
    }

    /// <summary>One shot's update (<c>0x004AB180</c>), which <see cref="Execute"/> runs for every shot.
    /// A shot a creature makes in its update is here before this pass, as DK2 makes it in list 0 of
    /// <c>0x004B7830</c> and reads list 1's head afterwards (ADR-0551, ADR-0554).</summary>
    internal static void Update(SimulationContext context, ShotState shot)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        if (!database.Shots.TryGetValue(shot.ShotTypeId, out ShotDef? definition))
        {
            world.RemoveShot(shot.Id);
            return;
        }

        FpPosition3 start = shot.ViewPosition;
        shot.PreviousViewPosition = start;
        shot.Age++;
        if (shot.Age == 1)
        {
            ShotRules.RequestShotEffect(context, shot, new EffectId(definition.CreationEffectId), start,
                carryVelocity: (definition.Flags & 0x80) != 0);
            ShotRules.Process(context, shot, definition, ShotRules.FirstTickEvent, null);
        }
        if (shot.Health <= 0)
        {
            ShotRules.RequestTimedEffect(context, shot, definition, shot.ViewPosition);
            Die(context, shot, definition, deathShot: false, showDeathEffect: false);
            return;
        }

        // Step 4: the pre-move event (an instant shot moves onto its target here).
        ShotRules.Process(context, shot, definition, ShotRules.PreMoveEvent, null);
        if (shot.Health <= 0)
        {
            ShotRules.RequestEffectsAfterProcessKill(context, shot, definition, start);
            Die(context, shot, definition, deathShot: false, showDeathEffect: false);
            return;
        }

        bool stopped = false;
        bool forced = shot.ForcedCollision;
        CreatureState? hit = ShotRules.FindHit(world, database, shot, definition, out long hitOrder);
        // A forced collision searches nothing; otherwise the first of the creature and the trap in the walk.
        long trapOrder = long.MaxValue;
        TrapState? trapHit = forced ? null : ShotRules.FindTrapHit(world, shot, definition, out trapOrder);
        if (trapHit != null && hit != null && hitOrder < trapOrder) trapHit = null;
        if (trapHit != null)
        {
            // The target word now names a trap, which no creature-side process handler acts on.
            shot.TargetKind = FightTargetKind.None;
            shot.TargetId = CombatRules.NoTarget;
            ShotRules.RequestShotEffect(context, shot, new EffectId(definition.HitThingEffectId), shot.ViewPosition);
            ShotRules.Chain(context, shot, definition.HitThingShotId, onThing: true);
            ShotRules.Process(context, shot, definition, ShotRules.HitThingEvent, null);
            ShotRules.ContactDamageTrap(context, shot, definition, trapHit);
            if ((definition.Flags & ShotRules.DiesOnThing) != 0) shot.Health = 0;
        }
        else if (hit != null)
        {
            // 0x004ABE7D: the thing struck is written into the target word before anything else.
            shot.TargetKind = FightTargetKind.Creature;
            shot.TargetId = hit.Id;
            ShotRules.RequestShotEffect(context, shot, new EffectId(definition.HitThingEffectId), shot.ViewPosition,
                attachedCreatureId: new CreatureId(hit.Id));
            ShotRules.Chain(context, shot, definition.HitThingShotId, onThing: true);
            ShotRules.Process(context, shot, definition, ShotRules.HitThingEvent, hit);
            ShotRules.ContactDamage(context, shot, definition, hit);
            CollapseSystem.Now(context, hit);   // inside the damage call (0x004B2FB5)
            // 0x004AC05B..0x004AC099, after the damage and before the die-on-thing test: the creature's heading
            // becomes the angle distance of the shot's heading from 0x400 (so a collapse's own pick is
            // overwritten), and the hit sounds in its own category at its own position with the shot's handle.
            if ((definition.Flags & ShotRules.TurnsCreature) != 0)
                hit.Heading = Steering.Bearing.Distance(shot.Heading, 0x400);
            if ((definition.Flags & ShotRules.TurnsCreature) != 0 && database.CreatureById(hit.CreatureTypeId).SoundCategory is { Length: > 0 } struck
                && !ReferenceEquals(context.Events, NullEventSink.Instance))
                ThingSoundRules.Request(context, SystemName, new SoundSourceId(SoundSourceKind.Shot, shot.Id), struck, ThingSoundEvent.Struck,
                    new FpPosition3(new Fp32(hit.PosX), new Fp32(hit.PosY), new Fp32(hit.PosZ)));
            if ((definition.Flags & ShotRules.DiesOnThing) != 0) shot.Health = 0;
        }
        else if (ShotRules.HitsHeart(world, database, shot, definition))
        {
            ShotRules.RequestShotEffect(context, shot, new EffectId(definition.HitThingEffectId), shot.ViewPosition);
            ShotRules.Chain(context, shot, definition.HitThingShotId, onThing: true);
            ShotRules.ContactDamageHeart(context, shot, definition);
            if ((definition.Flags & ShotRules.DiesOnThing) != 0) shot.Health = 0;
        }
        else if (ShotRules.HitsDoor(world, shot, definition, out DoorState? door))
        {
            ShotRules.RequestShotEffect(context, shot, new EffectId(definition.HitThingEffectId), shot.ViewPosition);
            ShotRules.Chain(context, shot, definition.HitThingShotId, onThing: true);
            ShotRules.ContactDamageDoor(context, shot, definition, door!);
            if ((definition.Flags & ShotRules.DiesOnThing) != 0) shot.Health = 0;
        }
        else
        {
            ProbeAhead(context, shot, definition);
            stopped = !Move(context, shot, definition, start);
        }

        ShotRules.AreaDamage(context, shot, definition);
        ShotRules.SquareDamage(context, shot, definition);

        // The lifetime counts down every tick the shot is not destroyed by the world; a shot
        // standing still (an explosion, a cloud, an Inferno) counts down too (Q398), and so does one
        // the world stopped without killing (0x004AB4B6, keeper-spell-inferno-chicken.md §4).
        if (shot.Health > 0 && !stopped && (definition.Flags & ShotRules.CountsDown) != 0) shot.Health--;
        if (shot.Health <= 0) Die(context, shot, definition, deathShot: !stopped, showDeathEffect: !stopped);
        if (!stopped) ShotRules.RequestTimedEffect(context, shot, definition, shot.ViewPosition);
    }

    /// <summary><c>0x004ABD60</c> with nothing struck: the collision test is repeated where the shot is and
    /// half a move on, the shot put back afterwards. A thing found leaves the velocity at the distance
    /// covered so far, for good, so the shot stops short of it and strikes at the start of its next
    /// update. The move's end is not tested. Level 5, turn 1719: two Fireballs half their velocity
    /// beside the Wizard and the newer strikes on 1720.</summary>
    private static void ProbeAhead(SimulationContext context, ShotState shot, ShotDef definition)
    {
        WorldState world = context.World;
        // A collide type with no search has no collision step at all (table 0x006E4FF0, types 0 and 5 up).
        if (definition.CollideType == ShotRules.CollideNone || definition.CollideType > ShotRules.CollideBodies) return;
        if (shot.VelX == 0 && shot.VelY == 0 && shot.VelZ == 0) return;
        int startX = shot.PosX, startY = shot.PosY;
        int halfX = shot.VelX / 2, halfY = shot.VelY / 2, halfZ = shot.VelZ / 2;
        int doneX = 0, doneY = 0, doneZ = 0;
        for (int step = 0; step < 2; step++)
        {
            if (ShotRules.FindHit(world, context.Database, shot, definition) != null
                || ShotRules.FindTrapHit(world, shot, definition, out _) != null
                || ShotRules.HitsHeart(world, context.Database, shot, definition)
                || ShotRules.HitsDoor(world, shot, definition, out _))
            {
                shot.VelX = doneX;
                shot.VelY = doneY;
                shot.VelZ = doneZ;
                break;
            }
            doneX += halfX;
            doneY += halfY;
            doneZ += halfZ;
            // The probe point is taken wherever it lies on the map, solid or not (0x00509050).
            if (!world.Contains(new TilePos((startX + doneX) >> 12, (startY + doneY) >> 12))) continue;
            shot.PosX = startX + doneX;
            shot.PosY = startY + doneY;
        }
        shot.PosX = startX;
        shot.PosY = startY;
    }

    /// <summary>Two half-steps along the velocity, each refused by a solid tile (§4a, §6). False when
    /// the world stopped the shot this tick and its <c>Flags</c> bit 0 destroyed it; a shot stopped
    /// without that bit goes on to the countdown and the flying death, as DK2's falls through.</summary>
    private static bool Move(SimulationContext context, ShotState shot, ShotDef definition, FpPosition3 start)
    {
        WorldState world = context.World;
        WorldSteering navigation = context.Navigation;
        if (shot.VelX == 0 && shot.VelY == 0)
        {
            ShotRules.RequestShotEffect(context, shot, new EffectId(definition.HitSolidEffectId), start);
            ShotRules.RequestShotSound(context, shot, definition, ThingSoundEvent.ShotHitSolid);   // 0x004AB49B, standing as moving shots' hit-solid effect is
            return true;
        }

        int halfX = shot.VelX / 2, halfY = shot.VelY / 2;
        for (int step = 0; step < 2; step++)
        {
            int dx = step == 0 ? halfX : shot.VelX - halfX;
            int dy = step == 0 ? halfY : shot.VelY - halfY;
            int nx = shot.PosX + dx, ny = shot.PosY + dy;
            if (ShotRules.Passable(world, navigation, nx, ny))
            {
                shot.PosX = nx;
                shot.PosY = ny;
                continue;
            }

            ShotRules.RequestShotEffect(context, shot, new EffectId(definition.HitSolidEffectId), start);
            ShotRules.Chain(context, shot, definition.HitSolidShotId);
            ShotRules.RequestShotSound(context, shot, definition, ThingSoundEvent.ShotHitSolid);   // 0x004AB49B, after the hit-solid shot
            if ((definition.Flags & ShotRules.Bounces) != 0)
            {
                bool xBlocked = !ShotRules.Passable(world, navigation, nx, shot.PosY);
                bool yBlocked = !ShotRules.Passable(world, navigation, shot.PosX, ny);
                if (xBlocked || !yBlocked) shot.VelX = -shot.VelX / 2;
                if (yBlocked || !xBlocked) shot.VelY = -shot.VelY / 2;
                context.Emit(SystemName, "shot_bounced", $"shot={shot.Id} at={shot.PosX},{shot.PosY}");
                return true;
            }

            shot.VelX = 0;
            shot.VelY = 0;
            context.Emit(SystemName, "shot_stopped", $"shot={shot.Id} type={definition.ShotId} at={shot.PosX},{shot.PosY}");
            if ((definition.Flags & ShotRules.DiesOnSolid) == 0) return true;
            shot.Health = 0;
            return false;
        }

        // The stepper adds a sixteenth of the velocity a tick, truncated toward zero (shots.md section 6).
        shot.PosZ += shot.VelZ / 16;
        if (definition.AirFriction != 0)
        {
            int keep = Math.Max(Fp32.One - definition.AirFriction, Fp32.One / 2);
            shot.VelX = (int)(((long)shot.VelX * keep) >> 12);
            shot.VelY = (int)(((long)shot.VelY * keep) >> 12);
            shot.VelZ = (int)((long)shot.VelZ * keep / Fp32.One);   // the stepper truncates toward zero (0x004B26B2)
        }
        return true;
    }

    /// <summary>Death (§4 step 7): the death shot for a shot spent on a thing or its lifetime, none
    /// for one the world stopped; then the thing is gone.</summary>
    private static void Die(SimulationContext context, ShotState shot, ShotDef definition, bool deathShot,
        bool showDeathEffect = true)
    {
        // The flying death (§4 step 7): the death shot, then process event 0x80; a shot the
        // world stopped is removed without either.
        if (showDeathEffect)
        {
            ShotRules.RequestShotSound(context, shot, definition, ThingSoundEvent.ShotEnded);   // 0x004AB509, before the death effect
            ShotRules.RequestShotEffect(context, shot, new EffectId(definition.DeathEffectId), shot.ViewPosition);
        }
        if (deathShot)
        {
            ShotRules.Chain(context, shot, definition.DeathShotId);
            ShotRules.Process(context, shot, definition, ShotRules.DeathEvent, null);
        }
        // Shot_Teardown (0x004AAF9F), on removal, whichever way it died: the same group with the second handle.
        ShotRules.RequestShotSound(context, shot, definition, ThingSoundEvent.ShotEnded, SoundSourceKind.ShotSecond);
        context.World.RemoveShot(shot.Id);
        context.Emit(SystemName, "shot_died", $"shot={shot.Id} type={definition.ShotId} age={shot.Age}");
    }
}
