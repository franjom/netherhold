// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>The fixture database with sound categories on objects, which the transcribed
/// definitions leave empty; the names are the shipped ones (the <c>*SoundCategoriesAreTheShippedNames</c> tests
/// read the real files).</summary>
internal static class SoundFixture
{
    public const byte RatObjectId = 11, BatObjectId = 12;

    public static GameDatabase Database(IReadOnlyDictionary<byte, string>? objects = null, params CreatureDef[] creatures)
    {
        GameDatabase basis = Dk2TerrainFixture.Database(creatures);
        var objectDefs = basis.Objects.Values.ToList();
        foreach ((byte id, string category) in objects ?? new Dictionary<byte, string>())
        {
            int at = objectDefs.FindIndex(o => o.ObjectId == id);
            if (at >= 0) objectDefs[at] = objectDefs[at] with { SoundCategory = category };
            else objectDefs.Add(Plain(id, category));
        }
        return new GameDatabase(basis.Terrain.Values, basis.Rooms.Values, basis.Creatures.Values, basis.Variables,
            basis.KeeperSpells.Values, objectDefs, basis.Doors.Values, basis.Traps.Values, basis.CreatureSpells.Values, basis.Shots.Values,
            basis.Effects.Values, basis.EffectElements.Values, basis.Level, basis.CameraPathFrames);
    }

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
