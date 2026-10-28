// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation.Navigation;

namespace DK2.Simulation;

/// <summary>A door's life past its opening (door-blocking.md): the blow taken flat, the recovery
/// at the head of its update (<c>0x004A55F0</c>), the two kills that update makes, an owner change
/// under it and a missing support wall, and the removal the world's thing pass makes of a door
/// whose death query (<c>0x004B5C30</c>, health 0 or below) answers yes (<c>0x004B7830</c>).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/door-blocking.md")]
public static class DoorRules
{
    private const string SystemName = "doors";

    /// <summary>The health routine's door arm (<c>0x004B2CE0</c>): the signed delta applied flat, no
    /// multiplier and no refusal. <paramref name="source"/> is the striker's owner, which DK2 stamps
    /// at <c>+0x36</c> and nothing here reads.</summary>
    public static void Damage(SimulationContext context, DoorState door, int delta, byte source) =>
        door.Health += delta;

    /// <summary>Whether DK2's death query would say the door is dead.</summary>
    public static bool IsDead(DoorState door) => door.Health <= 0;

    /// <summary>The recovery (<c>0x004A55F0</c>'s head): a live door past its deadline gains the
    /// definition's signed <c>HealthGain</c> (<c>def+0x93</c>), capped by what it is missing, and
    /// the deadline moves on one game second whenever that gain is defined — gained or not. The
    /// deadline starts at 0, so a door made late recovers on consecutive turns until it catches up.</summary>
    public static void Recover(SimulationContext context, DoorState door, DoorDef definition)
    {
        if (IsDead(door) || context.Tick.Value <= door.RecoveryDeadline) return;
        int rate = definition.HealthGain;
        if (rate == 0) return;
        int gain = Math.Min(rate, definition.Health - door.Health);
        if ((short)gain != 0) Damage(context, door, (short)gain, door.PlayerId);
        door.RecoveryDeadline += SimulationTiming.OriginalTicksPerGameSecond;
    }

    /// <summary>The update's owner test: a door whose tile no longer belongs to its owner has its
    /// health set to 0, and an unbuilt one leaves its keeper's blueprint list (<c>0x005056E0</c>).</summary>
    public static bool OwnerLost(WorldState world, DoorState door)
    {
        if (!world.Contains(door.X, door.Y) || world.Tile(door.X, door.Y).OwnerPlayerId == door.PlayerId) return false;
        if (door.UnderConstruction)
            CrateRules.Unregister(world, door.PlayerId, new BlueprintRef(CrateRules.DoorKind, door.Id));
        door.Health = 0;
        return true;
    }

    /// <summary>The support test after the barricade return (<c>0x004A5A41</c>–<c>0x004A5B0F</c>):
    /// both walls the stored orientation names must be solid in the navigation grid (world slot
    /// <c>+0x208</c>, <c>0x00509200</c>), north and south for 0, west and east otherwise. Either
    /// one missing calls the door's <c>+0x20</c>, <c>0x004B2AD0</c>, which sets health to 0.</summary>
    public static bool SupportLost(WorldSteering navigation, DoorState door)
    {
        bool northSouth = door.Orientation == 0;
        int ax = northSouth ? door.X : door.X - 1, ay = northSouth ? door.Y - 1 : door.Y;
        int bx = northSouth ? door.X : door.X + 1, by = northSouth ? door.Y + 1 : door.Y;
        if (NavBytes.IsSolid(navigation.NavByte(ax, ay)) && NavBytes.IsSolid(navigation.NavByte(bx, by))) return false;
        door.Health = 0;
        return true;
    }

    /// <summary>The removal (<c>0x004A66A0</c> → <c>0x004B7BD0</c> → cleanup <c>0x004A5290</c>): the
    /// door leaves the world, and with it its blueprint; the navigation byte goes to floor on the
    /// steering's next refresh, whatever the terrain: <c>0x004A66F0(0)</c> writes 0 over any byte
    /// carrying a door owner, so a door that dies on water or lava leaves floor there until the
    /// terrain changes (<see cref="WorldState.NavStamps"/>, Q600). DK2 skips that write for a door
    /// that never wrote its byte, one placed in play and still under construction, and for an owner
    /// whose number (<c>player+0x10FC</c>) is 0; the first stands on its keeper's claimed floor
    /// and the one shipped door off floor is the Good player's, so ours stamps every door off floor. The
    /// cleanup's sound, linked objects and the destruction effect
    /// (<c>def+0xAE</c>) are presentation or unbuilt.</summary>
    public static void Remove(SimulationContext context, DoorState door)
    {
        WorldState world = context.World;
        CrateRules.Unregister(world, door.PlayerId, new BlueprintRef(CrateRules.DoorKind, door.Id));
        world.RemoveDoor(door);
        if (world.Contains(door.X, door.Y))
        {
            if (context.Database.Doors.GetValueOrDefault(door.DoorId)?.Flags.HasFlag(DoorFlags.Secret) == true)
                world.Tile(door.X, door.Y).SecretDoor = false;
            TerrainDef terrain = context.Database.TerrainById(world.Tile(door.X, door.Y).TerrainId);
            if (!terrain.IsSolid && (terrain.IsWater || terrain.IsLava))
                world.Stamp((door.Y * world.Width) + door.X, NavBytes.Floor);
        }
        context.Emit(SystemName, "door_destroyed", $"door={door.Id} type={door.DoorId} player={door.PlayerId} at={door.X},{door.Y}");
    }
}
