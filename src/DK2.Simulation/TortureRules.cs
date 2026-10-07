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

    /// <summary>135 (<c>0x00484CC0</c>): zeroes three animation words and returns 0, idle. The kick a
    /// willing torturee takes before a Call to Arms walk (<c>0x004BDF0E</c>) or the Heart's responder
    /// walk (<c>0x004D99E4</c>, not built), so its leave from category 15 frees the device; the owner
    /// change <c>0x0048D050</c> passes through it on a conversion (refused-states.md §7).</summary>
    public const int VoluntaryLeaveState = 135;

    /// <summary>What each act of the hook adds to <see cref="CreatureState.TorturedDuration"/>:
    /// variable 19.</summary>
    public const int DurationPerAct = CreatureStateMachine.HookCountdownSeed;

    /// <summary>The torturer's states, group 8 and category 0 (ADR-0196): 127 chooses in the
    /// chamber, 128 faces a victim, 129 whips it. 128 and 129 reach each other through idle,
    /// since a category-0 target in the same group is refused (ADR-0195).</summary>
    public const int TorturerChoosingState = 127;
    public const int TorturerFacingState = 128;
    public const int TorturerWhippingState = 129;

    /// <summary>136: taken off a device that went away (<c>0x00484CE0</c>). It gets onto another
    /// free one within reach, through 130, or goes idle.</summary>
    public const int ReleasedState = 136;

    /// <summary>77's destination for a Horned Reaper (<c>IsHorny</c>, definition flag bit 14),
    /// whatever room it lands in: it is never strapped on.</summary>
    public const int HornyDropState = 238;

    /// <summary>Turns a whip lasts: action 63's handler <c>0x00499C20</c> ends once more than
    /// five turns have passed since it began, spawning effect <c>0xED</c> at the victim.</summary>
    public const int WhipTurns = 6;

    public static bool IsDevice(byte objectTypeId) =>
        objectTypeId is WheelObjectId or ElectricChairObjectId;

    /// <summary>Whether this creature is on a device.</summary>
    public static bool IsBeingTortured(CreatureState creature) =>
        creature.Activity == CreatureActivity.BeingTortured;

    /// <summary>The keeper whose chamber a creature on a device is in: its jailer if it is a
    /// captive, else its own, a Mistress who got on by herself.</summary>
    public static byte KeeperOf(CreatureState creature) =>
        PrisonRules.IsImprisoned(creature) ? (byte)creature.PrisonOwnerPlayerId : creature.OwnerPlayerId;

    /// <summary>Whether a device will hold this creature. 131 (<c>0x00484590</c>) turns away,
    /// from an Electric Chair, a creature without <see cref="CreatureFlags.CanBeElectrocuted"/>
    /// (definition <c>+0x686</c> bit 19); the wheel takes anyone.</summary>
    public static bool Holds(ObjectState device, CreatureDef creature) =>
        device.ObjectTypeId != ElectricChairObjectId || creature.Flags.HasFlag(CreatureFlags.CanBeElectrocuted);

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

    /// <summary>Straps a captive dropped on a free device: 1, 130, 131 (76's enemy-room arm,
    /// ADR-0226). It stays its keeper's captive. Returns whether it took: 130's enter hook claims
    /// a place (ADR-0309), and a refusal leaves it in 1.</summary>
    public static bool TryStrap(
        in StateHookContext hooks, CreatureState creature, byte keeper, int tileIndex)
    {
        WorldState world = hooks.World;
        GameDatabase database = hooks.Database;
        ObjectState? device = FreeDeviceAt(world, database, keeper, tileIndex, creature.Id);
        if (device is null || !Holds(device, database.CreatureById(creature.CreatureTypeId))) return false;

        creature.PrisonOwnerPlayerId = keeper;
        StrapOn(world, creature, tileIndex);
        return EnterTheDevice(hooks, creature);
    }

    /// <summary>1, 130, 131; false when 130's claim is refused.</summary>
    private static bool EnterTheDevice(in StateHookContext hooks, CreatureState creature)
    {
        CreatureStateMachine.TrySetState(hooks, creature, CreatureStateMachine.Idle);
        return CreatureStateMachine.TrySetState(hooks, creature, StrappingState)
            && CreatureStateMachine.TrySetState(hooks, creature, TorturedState);
    }

    /// <summary>A keeper's own creature dropped on a free device of its own chamber: 1, 130 and
    /// 131, the room's drop state (76, <c>0x004E8DA0(0)</c>, ADR-0226), as for a captive, but it
    /// stays its own and is not a prisoner. A Horned Reaper is not strapped. Returns whether it
    /// took, as <see cref="TryStrap"/>.</summary>
    public static bool TryStrapOwn(
        in StateHookContext hooks, CreatureState creature, int tileIndex)
    {
        WorldState world = hooks.World;
        GameDatabase database = hooks.Database;
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        if ((definition.Flags & CreatureFlags.IsHorny) != 0) return false;
        ObjectState? device = FreeDeviceAt(world, database, creature.OwnerPlayerId, tileIndex, creature.Id);
        if (device is null || !Holds(device, definition)) return false;

        StrapOn(world, creature, tileIndex);
        creature.CurrentJob = JobType.None;
        creature.JobTileIndex = -1;
        return EnterTheDevice(hooks, creature);
    }

    /// <summary>136's search: a free device that holds this creature, on its own tile or one
    /// orthogonally beside it. DK2 takes the nearest in the room and needs it within √1.5 of
    /// a tile (<c>0x1800</c> against the squared distance), which from a tile's centre is the
    /// tile itself or a side neighbour. Ours, stated: a tie goes to the lower tile index.</summary>
    public static int DeviceWithinReach(
        WorldState world, GameDatabase database, CreatureState creature, byte keeper)
    {
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        int x = creature.Tile.X, y = creature.Tile.Y;
        ReadOnlySpan<(int X, int Y)> order = [(x, y), (x, y - 1), (x - 1, y), (x + 1, y), (x, y + 1)];
        foreach ((int tx, int ty) in order)
        {
            if (tx < 0 || ty < 0 || tx >= world.Width || ty >= world.Height) continue;
            int index = (ty * world.Width) + tx;
            ObjectState? device = FreeDeviceAt(world, database, keeper, index, creature.Id);
            if (device is not null && Holds(device, definition)) return index;
        }

        return -1;
    }

    /// <summary>131 found its device gone: 136, and from there 130 and 131 on the device at
    /// <paramref name="tileIndex"/>. Keeps the creature a captive if it was one.</summary>
    public static void Restrap(in StateHookContext hooks, CreatureState creature, int tileIndex)
    {
        WorldState world = hooks.World;
        CreatureStateMachine.TrySetState(hooks, creature, ReleasedState);
        StrapOn(world, creature, tileIndex);
        CreatureStateMachine.TrySetState(hooks, creature, StrappingState);
        CreatureStateMachine.TrySetState(hooks, creature, TorturedState);
    }

    /// <summary>130 for a creature that walked onto a free device of its own keeper's chamber,
    /// a Mistress with no victim to whip: it straps itself on and moves to 131. It stays its
    /// keeper's own. Returns whether it took; 130 fails to idle otherwise.</summary>
    public static bool TryStrapSelf(in StateHookContext hooks, CreatureState creature)
    {
        WorldState world = hooks.World;
        GameDatabase database = hooks.Database;
        int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
        ObjectState? device = FreeDeviceAt(world, database, creature.OwnerPlayerId, here, creature.Id);
        if (device is null || !Holds(device, database.CreatureById(creature.CreatureTypeId))) return false;
        if (!CreatureStateMachine.TrySetState(hooks, creature, TorturedState)) return false;

        StrapOn(world, creature, here);
        return true;
    }

    private static void StrapOn(WorldState world, CreatureState creature, int tileIndex)
    {
        creature.PosX = CreatureState.CentreOf(tileIndex % world.Width);
        creature.PosY = CreatureState.CentreOf(tileIndex / world.Width);
        creature.Body.Vel = Steering.Fixed16Vector.Zero;
        creature.StopMoving();
    }

    /// <summary>Takes a creature off its device, giving its state back. A captive is still its
    /// keeper's until something decides otherwise: the hand's drop, or
    /// <see cref="PrisonRules.Release"/>. A creature that got on by itself is idle.</summary>
    public static void Unstrap(in StateHookContext hooks, CreatureState creature)
    {
        if (!IsBeingTortured(creature)) return;
        CreatureStateMachine.TrySetState(hooks, creature, CreatureStateMachine.Idle);
        if (PrisonRules.IsImprisoned(creature))
        {
            return;
        }

        creature.CurrentJob = JobType.None;
        creature.JobTileIndex = -1;
        creature.ClearJob();
    }

    /// <summary>The leave hook <c>0x0047B4A0</c>, its simulation part: a creature that was not on
    /// its device by choice forgets the job it was dropped to do (<c>DroppedToDoJobId</c>, state
    /// <c>+0x6E</c>), then <see cref="CreatureState.TortureVoluntary"/> is cleared, then the release.
    /// Not modelled: its first two steps, which re-show the device and switch off an Electric
    /// Chair's sparks (draw and effect bits only, <c>leave-hook-bodies.md</c>).</summary>
    public static void Leave(in StateHookContext hooks, CreatureState creature)
    {
        if (!creature.TortureVoluntary) creature.DroppedToDoJob = JobType.None;
        creature.TortureVoluntary = false;
        RoomCapacityRules.Release(creature);
    }
}
