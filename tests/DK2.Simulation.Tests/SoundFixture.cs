// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>The fixture database with sound categories on objects, doors and traps, which the transcribed
/// definitions leave empty; the names are the shipped ones (the <c>*SoundCategoriesAreTheShippedNames</c> tests
/// read the real files).</summary>
internal static class SoundFixture
{
    public const byte RatObjectId = 11, BatObjectId = 12;

    /// <summary>The shipped doors' categories and impact bytes (<c>Doors.kwd</c>), for the two the fixture carries and the rest.</summary>
    public static readonly IReadOnlyDictionary<byte, string> DoorCategories = new Dictionary<byte, string>
    {
        [1] = "DOOR_WOOD", [2] = "DOOR_STEEL", [3] = "DOOR_BRACED", [4] = "DOOR_MAGIC", [5] = "DOOR_SECRET", [6] = "DOOR_BARRICADE",
    };

    /// <summary>The shipped traps' categories (<c>Traps.kwd</c>) for the fixture's traps.</summary>
    public static readonly IReadOnlyDictionary<byte, string> TrapCategories = new Dictionary<byte, string>
    {
        [1] = "TRAP_GAS", [2] = "TRAP_BOULDER", [3] = "TRAP_SENTRY", [4] = "TRAP_ALARM", [5] = "TRAP_FIREBURST", [6] = "TRAP_LIGHTNING",
        [8] = "TRAP_FEAR", [9] = "TRAP_TRIGGER", [10] = "TRAP_SPIKE", [14] = "TRAP_SENTRY", [16] = "TRAP_JACKINBOX",
    };

    public static GameDatabase Database(IReadOnlyDictionary<byte, string>? objects = null, params CreatureDef[] creatures) =>
        Database(objects, doors: true, traps: true, creatures);

    /// <summary>With the shipped door and trap categories when asked for, and <paramref name="impactKinds"/> as the doors' impact bytes.</summary>
    /// <summary>The same database with every door's impact byte set (4 and 5, the Steel and Magic doors', ask the boulder for 0x15A).</summary>
    public static GameDatabase WithDoorImpact(GameDatabase basis, byte impactKind) => new(basis.Terrain.Values, basis.Rooms.Values,
        basis.Creatures.Values, basis.Variables, basis.KeeperSpells.Values, basis.Objects.Values,
        basis.Doors.Values.Select(d => d with { ImpactKind = impactKind }), basis.Traps.Values, basis.CreatureSpells.Values,
        basis.Shots.Values, basis.Effects.Values, basis.EffectElements.Values, basis.Level, basis.CameraPathFrames);

    public static GameDatabase Database(IReadOnlyDictionary<byte, string>? objects, bool doors, bool traps, params CreatureDef[] creatures)
    {
        GameDatabase basis = Dk2TerrainFixture.Database(creatures);
        var objectDefs = basis.Objects.Values.ToList();
        foreach ((byte id, string category) in objects ?? new Dictionary<byte, string>())
        {
            int at = objectDefs.FindIndex(o => o.ObjectId == id);
            if (at >= 0) objectDefs[at] = objectDefs[at] with { SoundCategory = category };
            else objectDefs.Add(Plain(id, category));
        }
        IEnumerable<DoorDef> doorDefs = basis.Doors.Values.Select(d =>
            doors && DoorCategories.TryGetValue(d.DoorId, out string? category) ? d with { SoundCategory = category, ImpactKind = ImpactOf(d.DoorId) } : d);
        IEnumerable<TrapDef> trapDefs = basis.Traps.Values.Select(t =>
            traps && TrapCategories.TryGetValue(t.TrapId, out string? category) ? t with { SoundCategory = category } : t);
        return new GameDatabase(basis.Terrain.Values, basis.Rooms.Values, basis.Creatures.Values, basis.Variables,
            basis.KeeperSpells.Values, objectDefs, doorDefs, trapDefs, basis.CreatureSpells.Values, basis.Shots.Values,
            basis.Effects.Values, basis.EffectElements.Values, basis.Level, basis.CameraPathFrames);
    }

    /// <summary>Doors.kwd offset 0x226 of the shipped doors.</summary>
    private static byte ImpactOf(byte doorId) => doorId switch { 1 or 3 => 3, 2 => 4, 4 => 5, _ => 2 };

    private static ObjectDef Plain(byte id, string category) => new()
    {
        Name = category, ObjectId = id, SoundCategory = category, Flags = 0, Hp = 1, ManaValue = 0, RoomCapacity = 0,
        StartState = 0, PickUpPriority = 0, Width = new Fp32(128), Height = new Fp32(128), Mass = new Fp32(4096),
        Speed = new Fp32(0), NameStringId = 0, TooltipStringId = 0,
    };

    public static ThingSoundRequest[] Sounds(ListEventSink events) =>
        events.Events.Where(e => e.ThingSound.HasValue).Select(e => e.ThingSound!.Value).ToArray();

    public static FpPosition3 Centre(WorldState world, int tileIndex, int height = 0x1000) =>
        new(new Fp32(CreatureState.CentreOf(tileIndex % world.Width)), new Fp32(CreatureState.CentreOf(tileIndex / world.Width)),
            new Fp32(height));
}
