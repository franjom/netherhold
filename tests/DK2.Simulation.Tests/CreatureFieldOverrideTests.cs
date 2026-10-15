// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using Xunit;

namespace DK2.Simulation.Tests;

/// <summary>VARIABLES id 77 (ADR-0491): a level sets one definition field of one creature type,
/// <c>0x0051DD10</c> then <c>0x00494A70</c>, in file order. Level 1's Lord of the Land gets Threat 125
/// and MeleeDamage 200.</summary>
public class CreatureFieldOverrideTests
{
    private static GlobalVariables Level(params CreatureFieldOverrideEntry[] entries)
    {
        var level = new GlobalVariables();
        level.CreatureFieldOverrides.AddRange(entries);
        return level;
    }

    [Fact]
    public void TheLevelsRecordsSetTheNamedFieldsOfTheNamedType_AndNoOther()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        CreatureDef goblin = database.CreatureById(Dk2TerrainFixture.GoblinCreatureId);
        CreatureDef warlock = database.CreatureById(Dk2TerrainFixture.WarlockCreatureId);

        GameDatabase level = database.ForLevel(Level(
            new(Dk2TerrainFixture.GoblinCreatureId, (int)CreatureStatId.MeleeDamage, 200),
            new(Dk2TerrainFixture.GoblinCreatureId, (int)CreatureStatId.Threat, 125),
            new(Dk2TerrainFixture.GoblinCreatureId, 21, 6144)));

        CreatureDef changed = level.CreatureById(Dk2TerrainFixture.GoblinCreatureId);
        Assert.Equal((ushort)200, changed.MeleeDamage);
        Assert.Equal((ushort)125, changed.Threat);
        Assert.Equal(new Fp32(6144), changed.Speed);
        Assert.Equal(goblin.Hp, changed.Hp);
        Assert.Equal(warlock, level.CreatureById(Dk2TerrainFixture.WarlockCreatureId));
        // The shared database is untouched.
        Assert.Equal(goblin, database.CreatureById(Dk2TerrainFixture.GoblinCreatureId));
    }

    [Fact]
    public void ALaterRecordForTheSameFieldWins_AndAnUnknownTypeIsSkipped()
    {
        GameDatabase database = Dk2TerrainFixture.Database();

        GameDatabase level = database.ForLevel(Level(
            new(Dk2TerrainFixture.GoblinCreatureId, (int)CreatureStatId.Fear, 0),
            new(Dk2TerrainFixture.GoblinCreatureId, (int)CreatureStatId.Fear, 5000),
            new(250, (int)CreatureStatId.Health, 50)));

        Assert.Equal((ushort)5000, level.CreatureById(Dk2TerrainFixture.GoblinCreatureId).Fear);
    }

    /// <summary>Selector 30 stores whole tiles raw (<c>0x00494C56</c>); ours holds the hearing distance
    /// in the file's 12.12 units, so seven tiles is 7 &lt;&lt; 12. Health is a word, as loaded.</summary>
    [Fact]
    public void TheHearingDistanceIsInWholeTiles_AndHealthIsTheRawValue()
    {
        GameDatabase database = Dk2TerrainFixture.Database();

        CreatureDef goblin = database.ForLevel(Level(
            new(Dk2TerrainFixture.GoblinCreatureId, 30, 7),
            new(Dk2TerrainFixture.GoblinCreatureId, (int)CreatureStatId.Health, 50))).CreatureById(Dk2TerrainFixture.GoblinCreatureId);

        Assert.Equal(new Fp32(7 << 12), goblin.DistanceCanHear);
        Assert.Equal((ushort)50, goblin.Hp);
    }

    [Fact]
    public void AnUnmappedSelectorIsRefused()
    {
        GameDatabase database = Dk2TerrainFixture.Database();

        Assert.Throws<InvalidDataException>(() => database.ForLevel(Level(
            new CreatureFieldOverrideEntry(Dk2TerrainFixture.GoblinCreatureId, 7, 1))));
    }
}
