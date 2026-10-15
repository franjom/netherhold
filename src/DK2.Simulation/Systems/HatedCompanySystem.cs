// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>The hated-company step of four room types' tick handlers (<c>0x004E8C60</c>, called by
/// the Library, Training Room, Guard Room and Workshop handlers): every 32 turns, a room whose
/// place-holders include an <c>IsEvil</c> creature and one without it angers each place-holder
/// that lacks <c>DoesntGetAngryWithEnemies</c> by eight seconds of
/// <c>ModifyAngerInCompanyOfHatedCreaturesPerSecond</c>, into the Treatment bucket with complaint 7.</summary>
/// <remarks>Deviation: DK2's phase is the room's id, <c>(turn + room+4) &amp; 31</c>; ours is the
/// room's lowest tile index, because our rooms carry no id (ADR-0479).</remarks>
[Dk2Behavior(Confidence.Confirmed, "docs/research/binary-census.md")]
public sealed class HatedCompanySystem : ISimulationSystem
{
    /// <summary>The step's period in turns, the handler's <c>&amp; 0x1F</c>.</summary>
    public const int PeriodTurns = 32;

    /// <summary>The handler shifts the per-second variable left by 3: the 32 turns' eight seconds.</summary>
    public const int SecondsPerApplication = 8;

    private readonly List<CreatureState> _holders = [];
    private readonly List<RoomInstance> _rooms = [];

    public string Name => "hated company";

    /// <summary>The room types whose tick handler calls <c>0x004E8C60</c>.</summary>
    public static bool Applies(RoomTypeId? type) =>
        type is RoomTypeId.Library or RoomTypeId.TrainingRoom or RoomTypeId.GuardRoom or RoomTypeId.Workshop;

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        // One pass over the held places; rooms are built only when two alignments meet in them.
        _holders.Clear();
        bool anyEvil = false, anyOther = false;
        foreach (CreatureState c in world.Creatures)
        {
            if (!c.HeldPlace.IsHeld) continue;
            RoomDef? kind = RoomRules.RoomOn(database, world.TileAtIndex(c.HeldPlace.TileIndex));
            if (kind is null || !Applies(kind.TypeId)) continue;
            _holders.Add(c);
            if (IsEvil(database, c)) anyEvil = true;
            else anyOther = true;
        }

        if (!anyEvil || !anyOther) return;
        if (!database.Variables.TryGet(
                VariableTypeId.ModifyAngerInCompanyOfHatedCreaturesPerSecond, out int perSecond)) return;

        _rooms.Clear();
        foreach (CreatureState c in _holders)
        {
            if (InKnownRoom(c)) continue;
            RoomInstance? room = RoomIndex.RoomAt(world, database, c.HeldPlace.TileIndex);
            if (room is not null) _rooms.Add(room);
        }

        // DK2's turn is our tick plus one (ADR-0119).
        long turn = context.Tick.Value + 1;
        int delta = perSecond * SecondsPerApplication;
        foreach (RoomInstance room in _rooms)
        {
            if ((turn + room.TileIndices[0]) % PeriodTurns != 0 || !Mixed(database, room)) continue;
            foreach (CreatureState c in _holders)
            {
                if (!c.HeldPlace.In(room)) continue;
                CreatureDef definition = database.CreatureById(c.CreatureTypeId);
                if (definition.Flags.HasFlag(CreatureFlags.DoesntGetAngryWithEnemies)) continue;
                int applied = CreatureAngerRules.Apply(
                    world, definition, c, AngerBucket.Treatment, delta, AngerComplaint.HatedCompany);
                context.Emit(Name, "angered",
                    $"creature={c.Id} room={room.Definition.Name} delta={applied}");
            }
        }
    }

    private bool InKnownRoom(CreatureState c)
    {
        foreach (RoomInstance room in _rooms)
            if (c.HeldPlace.In(room)) return true;
        return false;
    }

    private bool Mixed(GameDatabase database, RoomInstance room)
    {
        bool evil = false, other = false;
        foreach (CreatureState c in _holders)
        {
            if (!c.HeldPlace.In(room)) continue;
            if (IsEvil(database, c)) evil = true;
            else other = true;
            if (evil && other) return true;
        }

        return false;
    }

    private static bool IsEvil(GameDatabase database, CreatureState c) =>
        database.CreatureById(c.CreatureTypeId).Flags.HasFlag(CreatureFlags.IsEvil);
}
