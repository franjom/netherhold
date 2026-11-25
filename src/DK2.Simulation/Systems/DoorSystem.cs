// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Systems;

/// <summary>DK2's door update (<c>0x004A55F0</c>, doors-and-traps.md), in its own order: the
/// owner's lock command, then the lock-pick countdown, then the open state. A completed door is
/// open while a picked lock is still running, shut while its lock mode is 3, and otherwise open
/// while an eligible creature stands on one of three tiles — its own and the two beside it along
/// the passage. Eligible: allied with the door's owner, not the neutral player, not dying, and
/// permitted by the lock mode. An open door's tile is plain floor in the navigation byte; a
/// closed one carries its owner and lock (<see cref="Navigation.WorldSteering"/>).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/doors-and-traps.md")]
public sealed class DoorSystem : ISimulationSystem
{
    private const string SystemName = "doors";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        Lock(context);

        foreach (DoorState door in context.World.Doors)
        {
            // The countdown runs before anything reads it and whatever the door is doing:
            // the original decrements at 0x004A56A1, above both the construction bypass and
            // the open decision.
            if (door.LockPickTicks > 0) door.LockPickTicks--;
            if (door.UnderConstruction) continue;
            if (context.Database.Doors.TryGetValue(door.DoorId, out DoorDef? definition)
                && ((definition.Flags & DoorFlags.Barricade) == DoorFlags.Barricade)) continue;

            bool was = door.Open;
            door.Open = door.LockPickTicks > 0
                || (door.LockMode != 3 && AnyEligibleAtHand(context, door));
            // 0x004A60B9: the open state changed. A door found dead skips the open decision in DK2, but its owner and support
            // kills run inside the same update, before that test, and ours are DoorHealthSystem's, after this one: a door
            // killed this tick may still sound this tick (a deviation with no practical effect, it goes the same turn).
            if (door.Open != was && !DoorRules.IsDead(door))
                ThingSoundRules.Request(context, Name, door, door.Open ? ThingSoundEvent.DoorOpened : ThingSoundEvent.DoorClosed);

            if (door.Open && definition is not null && (definition.Flags & DoorFlags.Secret) == DoorFlags.Secret)
                SeenOpen(context, door);
        }
    }

    /// <summary>The update's last arm (<c>0x004A64A3</c>–<c>0x004A6661</c>): an open, completed
    /// Secret Door is seen by every creature of a player who is an enemy of its owner
    /// (<c>0x004BF390</c>) that stands strictly within four tiles of it on the plane, is outside the
    /// combat category, may look for a fight unforced (<c>0x0045A6D0(0)</c>, which refuses the
    /// possessed creature) and has a clear line to it (<c>0x004512F0</c>). Each such creature gets
    /// the door as its forced target (<c>0x0048F390</c>) and the tile's secret-door marker is
    /// cleared (<c>0x00450FF0</c>), every tick and whether or not the marker was still set. DK2 also
    /// posts command <c>0x11</c>, which is presentation.</summary>
    private void SeenOpen(SimulationContext context, DoorState door)
    {
        WorldState world = context.World;
        int doorX = CreatureState.CentreOf(door.X), doorY = CreatureState.CentreOf(door.Y);
        foreach (CreatureState creature in world.Creatures)
        {
            if (world.AreAllied(creature.OwnerPlayerId, door.PlayerId)) continue;
            if (!IsWithinSeeingRange(creature, doorX, doorY)) continue;
            if (NeedRules.ResolvedCategory(creature) == CreatureStateCategory.Combat) continue;
            if (!CombatRules.MayLookForAFight(world, context.Database, creature, force: false)) continue;
            if (!LineOfSight.Clear(Sight(context), creature.PosX, creature.PosY, doorX, doorY)) continue;

            ForceOnto(world, creature, door);
            Unhide(context, door, seenBy: creature);
        }
    }

    /// <summary>Strictly nearer than four tiles on the plane; height is not measured
    /// (<c>0x004A6521</c>–<c>0x004A656C</c>).</summary>
    private static bool IsWithinSeeingRange(CreatureState creature, int doorX, int doorY)
    {
        long offsetX = creature.PosX - doorX, offsetY = creature.PosY - doorY;
        long distanceSquared = ((offsetX * offsetX) / Fp32.One) + ((offsetY * offsetY) / Fp32.One);
        return distanceSquared < SeeingRangeSquared;
    }

    private const long SeeingRangeSquared = 4 * 4 * Fp32.One;

    /// <summary><c>0x0048F390</c>: the door becomes the creature's forced target. A forced target it
    /// already had is unlinked first; a fight in progress is left alone.</summary>
    private static void ForceOnto(WorldState world, CreatureState creature, DoorState door)
    {
        if (creature.AttackingId != CombatRules.NoTarget) CombatPitRules.ClearForcedTarget(world, creature);
        creature.AttackAbnormalTargets = true;
        creature.AttackingId = door.Id;
        creature.AttackingKind = FightTargetKind.Door;
    }

    /// <summary><c>0x00450FF0(x, y, 0)</c>: the tile stops being marked as a hidden door, for every
    /// player at once.</summary>
    private static void Unhide(SimulationContext context, DoorState door, CreatureState seenBy)
    {
        ref TileState tile = ref context.World.Tile(door.X, door.Y);
        if (!tile.SecretDoor) return;
        tile.SecretDoor = false;
        context.Emit(SystemName, "secret_door_seen",
            $"door={door.Id} player={door.PlayerId} creature={seenBy.Id} by={seenBy.OwnerPlayerId} at=({door.X},{door.Y})");
    }

    private WorldSight? _sight;
    private WorldState? _sightWorld;

    private WorldSight Sight(SimulationContext context)
    {
        if (!ReferenceEquals(_sightWorld, context.World))
        {
            context.Navigation.Refresh();
            _sight = new WorldSight(context.Navigation, context.World, context.Database);
            _sightWorld = context.World;
        }
        return _sight!;
    }

    /// <summary>The owner's lock command (<c>0x00513800</c>): a door of this player's, toggled
    /// between locked and unlocked. A command naming a tile with no door of theirs does nothing,
    /// which is the original's three refusals — no live thing, the wrong type, another
    /// owner.</summary>
    private static void Lock(SimulationContext context)
    {
        foreach (LockDoorCommand request in context.Commands<LockDoorCommand>())
        {
            foreach (DoorState door in context.World.Doors)
            {
                if (door.X != request.Tile.X || door.Y != request.Tile.Y) continue;
                if (door.PlayerId != request.PlayerId) continue;
                door.LockMode = door.LockMode == 0 ? (byte)3 : (byte)0;
                // 0x004A6844, only for a completed door: the lock, or the unlock.
                if (!door.UnderConstruction)
                    ThingSoundRules.Request(context, SystemName, door, door.LockMode == 0 ? ThingSoundEvent.DoorUnlocked : ThingSoundEvent.DoorLocked);
            }
        }
    }

    private static bool AnyEligibleAtHand(SimulationContext context, DoorState door)
    {
        WorldState world = context.World;
        foreach (CreatureState creature in world.Creatures)
        {
            TilePos at = creature.Tile;
            int dx = at.X - door.X, dy = at.Y - door.Y;
            bool onDoor = dx == 0 && dy == 0;
            bool beside = door.Orientation == 0
                ? dy == 0 && (dx == 1 || dx == -1)
                : dx == 0 && (dy == 1 || dy == -1);
            if (!onDoor && !beside) continue;
            if (Eligible(context, door, creature)) return true;
        }

        return false;
    }

    /// <summary>The creature arms of the update (<c>0x004A5E9E</c>–<c>0x004A5F1B</c> and its two
    /// copies). The read lock writers use 0 and 3: the lock command toggles them, the loader
    /// sets 3, and an effect handler sets 0. Modes 1 and 2 retain their read eligibility arms.</summary>
    /// <remarks>DK2's third test is the creature flag <c>+0x3E6</c> bit 21, <c>CreatureDying</c>.
    /// Ours is <see cref="CreatureDeathRules.IsDown"/>, which is that state plus the tick window
    /// in which a creature is out of health and its booked collapse has not run yet — the guard
    /// every other system here uses, and a wider door than the original's until that pass.</remarks>
    public static bool Eligible(SimulationContext context, DoorState door, CreatureState creature)
    {
        if (!context.World.AreAllied(door.PlayerId, creature.OwnerPlayerId)) return false;
        if (creature.OwnerPlayerId == PlayerIds.Neutral) return false;
        if (CreatureDeathRules.IsDown(creature)) return false;

        bool worker = ((context.Database.CreatureById(creature.CreatureTypeId).Flags & CreatureFlags.IsWorker) == CreatureFlags.IsWorker);
        return door.LockMode switch
        {
            0 => true,
            1 => !worker,
            2 => worker,
            _ => false,
        };
    }

}
