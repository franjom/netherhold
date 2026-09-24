// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The Torture Chamber: a captive dropped on a free device is strapped to it, and the
/// category 15 update hook <c>0x0047B590</c> hurts it and, once it has served its
/// <c>TortureTimeToConvert</c>, turns it to the keeper's side (ADR-0193,
/// <c>state-category-hooks.md</c> §3.3). The manual: enemies placed there "are likely to convert
/// or, failing that, release a valuable piece of information and die".</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/state-category-hooks.md")]
public static class TortureRules
{
    /// <summary>The two devices a creature is strapped to, the objects <c>0x004F2180</c> accepts by
    /// type: the Torture Wheel (8) and the Electric Chair (61). Coals and Water are decoration.</summary>
    public const byte WheelObjectId = 8;
    public const byte ElectricChairObjectId = 61;

    /// <summary>77, a creature dropped from the hand, goes to 130 in a Torture Chamber; 130 straps
    /// it to the device underfoot and moves to 131, being tortured.</summary>
    public const int StrappingState = 130;
    public const int TorturedState = 131;

    /// <summary>What each act of the hook adds to <see cref="CreatureState.TorturedDuration"/>:
    /// variable 19.</summary>
    public const int DurationPerAct = CreatureStateMachine.HookCountdownSeed;

    public static bool IsDevice(byte objectTypeId) =>
        objectTypeId is WheelObjectId or ElectricChairObjectId;

    /// <summary>Whether this creature is on a device.</summary>
    public static bool IsBeingTortured(CreatureState creature) =>
        creature.Activity == CreatureActivity.BeingTortured;

    /// <summary>The time served that converts: the definition's <c>TortureTimeToConvert</c> in
    /// turns (the loader stores seconds × variable 19, <c>0x0049391A</c>), scaled by the level row
    /// 24. A Goblin's is 80 seconds, 320 turns of duration, reached after 80 acts.</summary>
    public static int ConversionThreshold(GameDatabase database, CreatureDef creature, int level) =>
        CreatureLevelRules.Scale(
            (int)(((long)creature.TortureTimeToConvert.Raw * CreatureStateMachine.HookCountdownSeed) >> 12),
            CreatureLevelRules.Percent(database, CreatureStatId.TortureTimeToConvert, level));

    /// <summary>The free device on this tile of one of this keeper's Torture Chambers, or null. A
    /// device is taken while another creature is on it (<c>0x004F2180</c>'s 131/136 test).</summary>
    public static ObjectState? FreeDeviceAt(
        WorldState world, GameDatabase database, byte keeper, int tileIndex, int exceptCreatureId)
    {
        RoomInstance? room = RoomIndex.RoomAt(world, database, tileIndex);
        if (room is null || room.Definition.TypeId != RoomTypeId.TortureChamber) return null;
        if (room.OwnerPlayerId != keeper) return null;

        ObjectState? device = null;
        foreach (ObjectState thing in world.Objects)
        {
            if (thing.TileIndex == tileIndex && !thing.InHand && IsDevice(thing.ObjectTypeId))
            {
                device = thing;
                break;
            }
        }

        if (device is null) return null;
        foreach (CreatureState other in world.Creatures)
        {
            if (other.Id == exceptCreatureId || !IsBeingTortured(other) || HandRules.IsHeld(other)) continue;
            if ((other.Tile.Y * world.Width) + other.Tile.X == tileIndex) return null;
        }

        return device;
    }

    /// <summary>Straps a captive dropped on a free device: 77, 130, 131. It stays its keeper's
    /// captive. Returns whether it took.</summary>
    public static bool TryStrap(
        WorldState world, GameDatabase database, CreatureState creature, byte keeper, int tileIndex)
    {
        if (FreeDeviceAt(world, database, keeper, tileIndex, creature.Id) is null) return false;

        creature.PrisonOwnerPlayerId = keeper;
        creature.PosX = CreatureState.CentreOf(tileIndex % world.Width);
        creature.PosY = CreatureState.CentreOf(tileIndex / world.Width);
        creature.Body.Vel = Steering.Fixed16Vector.Zero;
        creature.StopMoving();
        creature.Activity = CreatureActivity.BeingTortured;

        if (CreatureStateMachine.HasState(creature)) CreatureStateMachine.Release(creature);
        CreatureStateMachine.Begin(creature, PrisonRules.HandDropState);
        CreatureStateMachine.TrySetState(creature, StrappingState);
        CreatureStateMachine.TrySetState(creature, TorturedState);
        return true;
    }

    /// <summary>Takes a creature off its device, giving its state back. It is still its keeper's
    /// captive until something decides otherwise: the hand's drop, or <see cref="PrisonRules.Release"/>.</summary>
    public static void Unstrap(CreatureState creature)
    {
        if (!IsBeingTortured(creature)) return;
        if (CreatureStateMachine.HasState(creature)) CreatureStateMachine.Release(creature);
        creature.Activity = CreatureActivity.Imprisoned;
    }
}
