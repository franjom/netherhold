// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>What a level writes straight into DK2's profile during play (specials 129 and 140), kept
/// on the world for the host to apply; the profile itself lives outside the simulation.</summary>
public struct CampaignCarry
{
    /// <summary>Locate Hidden Land activations, DK2's byte <c>0x00759B7C</c>; the host applies the
    /// unlock for the played map (<c>0x004A4F70</c>, CampaignProgression.LocateHiddenLand).</summary>
    public int HiddenLandFound;
    /// <summary>Set when special 140 stored a creature (<c>0x004A3FC0</c> arm 0x8C).</summary>
    public bool TransferStored;
    public byte TransferCreatureTypeId;
    public byte TransferCreatureLevel;

    public readonly void HashInto(ref Hash64 hash)
    {
        hash.Add(HiddenLandFound); hash.Add(TransferStored ? (byte)1 : (byte)0);
        hash.Add(TransferCreatureTypeId); hash.Add(TransferCreatureLevel);
    }

    public readonly void Write(BinaryWriter w)
    {
        w.Write(HiddenLandFound); w.Write(TransferStored); w.Write(TransferCreatureTypeId); w.Write(TransferCreatureLevel);
    }

    public static CampaignCarry Read(BinaryReader r) => new()
    {
        HiddenLandFound = r.ReadInt32(), TransferStored = r.ReadBoolean(),
        TransferCreatureTypeId = r.ReadByte(), TransferCreatureLevel = r.ReadByte(),
    };
}

/// <summary>The campaign's carry-over creature (docs/research/campaign-map-and-progression.md §4).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/campaign-map-and-progression.md")]
public static class TransferCreatureRules
{
    /// <summary>DK2's game mode byte <c>0x0075AD4B</c> for the single-player campaign.</summary>
    public const byte CampaignMode = 1;

    /// <summary><c>Level_InitTriggersAndStart</c> <c>0x0050C67D</c>–<c>0x0050C7BF</c>, before the first
    /// turn of a level that is not a restored save: the stored type is created for the keeper at the
    /// heart's tile centre two tiles toward lower Y, facing a random quarter turn from the world
    /// stream, then levelled (level − 1) times. The caller clears the profile whatever happens.</summary>
    public static CreatureState? Arrive(GameSimulation simulation, PlayerId keeper, byte creatureTypeId, byte level)
    {
        WorldState world = simulation.World;
        GameDatabase database = simulation.Database;
        if (creatureTypeId == 0 || !database.Creatures.ContainsKey(creatureTypeId)) return null;
        int heart = Combat.HeartSites.PositionOf(world, database, keeper);
        if (heart < 0) return null;
        int x = (heart % world.Width) * 0x1000 + 0x800, y = (heart / world.Width) * 0x1000 + 0x800 - 0x2000;
        var tile = new TilePos(x >> 12, y >> 12);
        if (y < 0 || !world.Contains(tile)) return null;

        CreatureState creature = world.SpawnCreature(database, creatureTypeId, keeper, tile);
        creature.PosX = x;
        creature.PosY = y;
        creature.Heading = (simulation.Rng.NextInt(4) << 9) & 0x7FF;
        CreatureDef definition = database.CreatureById(creatureTypeId);
        var hooks = new StateHookContext(world, database);
        for (int i = 1; i < level; i++) CreatureLevelRules.LevelUp(hooks, definition, creature);
        return creature;
    }
}
