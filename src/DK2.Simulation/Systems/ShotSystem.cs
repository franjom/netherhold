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
/// walked newest first; a shot made while the list is walked waits for the next turn (Q398).</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/shots.md")]
public sealed class ShotSystem : ISimulationSystem
{
    private const string SystemName = "shots";
    private readonly List<ShotState> _work = [];

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        if (world.Shots.Count == 0) return;
        GameDatabase database = context.Database;
        _work.Clear();
        _work.AddRange(world.Shots);

        for (int i = _work.Count - 1; i >= 0; i--)
        {
            ShotState shot = _work[i];
            if (!database.Shots.TryGetValue(shot.ShotTypeId, out ShotDef? definition))
            {
                world.RemoveShot(shot.Id);
                continue;
            }

            shot.Age++;
            if (shot.Age == 1) ShotRules.Process(context, shot, definition, ShotRules.FirstTickEvent, null);
            if (shot.Health <= 0)
            {
                Die(context, shot, definition, deathShot: false);
                continue;
            }

            // Step 4: the pre-move event (an instant shot moves onto its target here).
            ShotRules.Process(context, shot, definition, ShotRules.PreMoveEvent, null);
            if (shot.Health <= 0)
            {
                Die(context, shot, definition, deathShot: false);
                continue;
            }

            bool stopped = false;
            bool forced = shot.ForcedCollision;
            CreatureState? hit = ShotRules.FindHit(world, database, shot, definition);
            // A forced collision searches nothing; otherwise the nearer of the creature and the trap.
            int trapDistance = int.MaxValue;
            TrapState? trapHit = forced ? null : ShotRules.FindTrapHit(world, shot, definition, out trapDistance);
            if (trapHit != null && hit != null
                && CombatRules.DistanceSquared(shot.PosX, shot.PosY, hit.PosX, hit.PosY) <= trapDistance) trapHit = null;
            if (trapHit != null)
            {
                // The target word now names a trap, which no creature-side process handler acts on.
                shot.TargetKind = FightTargetKind.None;
                shot.TargetId = CombatRules.NoTarget;
                ShotRules.Chain(context, shot, definition.HitThingShotId);
                ShotRules.Process(context, shot, definition, ShotRules.HitThingEvent, null);
                ShotRules.ContactDamageTrap(context, shot, definition, trapHit);
                if ((definition.Flags & ShotRules.DiesOnThing) != 0) shot.Health = 0;
            }
            else if (hit != null)
            {
                // 0x004ABE7D: the thing struck is written into the target word before anything else.
                shot.TargetKind = FightTargetKind.Creature;
                shot.TargetId = hit.Id;
                ShotRules.Chain(context, shot, definition.HitThingShotId);
                ShotRules.Process(context, shot, definition, ShotRules.HitThingEvent, hit);
                ShotRules.ContactDamage(context, shot, definition, hit);
                if ((definition.Flags & ShotRules.DiesOnThing) != 0) shot.Health = 0;
            }
            else if (ShotRules.HitsHeart(world, shot, definition))
            {
                ShotRules.Chain(context, shot, definition.HitThingShotId);
                ShotRules.ContactDamageHeart(context, shot, definition);
                if ((definition.Flags & ShotRules.DiesOnThing) != 0) shot.Health = 0;
            }
            else if (ShotRules.HitsDoor(world, shot, definition, out DoorState? door))
            {
                ShotRules.Chain(context, shot, definition.HitThingShotId);
                ShotRules.ContactDamageDoor(context, shot, definition, door!);
                if ((definition.Flags & ShotRules.DiesOnThing) != 0) shot.Health = 0;
            }
            else
            {
                stopped = !Move(context, shot, definition);
            }

            ShotRules.AreaDamage(context, shot, definition);
            ShotRules.SquareDamage(context, shot, definition);

            // The lifetime counts down every tick the shot is not destroyed by the world; a shot
            // standing still (an explosion, a cloud, an Inferno) counts down too (Q398), and so does one
            // the world stopped without killing (0x004AB4B6, keeper-spell-inferno-chicken.md §4).
            if (shot.Health > 0 && !stopped && (definition.Flags & ShotRules.CountsDown) != 0) shot.Health--;
            if (shot.Health <= 0) Die(context, shot, definition, deathShot: !stopped);
        }
    }

    /// <summary>Two half-steps along the velocity, each refused by a solid tile (§4a, §6). False when
    /// the world stopped the shot this tick and its <c>Flags</c> bit 0 destroyed it; a shot stopped
    /// without that bit goes on to the countdown and the flying death, as DK2's falls through.</summary>
    private static bool Move(SimulationContext context, ShotState shot, ShotDef definition)
    {
        WorldState world = context.World;
        WorldSteering navigation = context.Navigation;
        if (shot.VelX == 0 && shot.VelY == 0) return true;

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

            ShotRules.Chain(context, shot, definition.HitSolidShotId);
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

        if (definition.AirFriction != 0)
        {
            int keep = Math.Max(Fp32.One - definition.AirFriction, Fp32.One / 2);
            shot.VelX = (int)(((long)shot.VelX * keep) >> 12);
            shot.VelY = (int)(((long)shot.VelY * keep) >> 12);
        }
        return true;
    }

    /// <summary>Death (§4 step 7): the death shot for a shot spent on a thing or its lifetime, none
    /// for one the world stopped; then the thing is gone.</summary>
    private static void Die(SimulationContext context, ShotState shot, ShotDef definition, bool deathShot)
    {
        // The flying death (§4 step 7): the death shot, then process event 0x80; a shot the
        // world stopped is removed without either.
        if (deathShot)
        {
            ShotRules.Chain(context, shot, definition.DeathShotId);
            ShotRules.Process(context, shot, definition, ShotRules.DeathEvent, null);
        }
        context.World.RemoveShot(shot.Id);
        context.Emit(SystemName, "shot_died", $"shot={shot.Id} type={definition.ShotId} age={shot.Age}");
    }
}
