// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Simulation.Tests;

/// <summary>Creature update's first speed modifier (<c>0x0048962C..0x004896C2</c>): under a fifth of
/// its level-scaled maximum health a creature moves at half speed. Level 5, turn 1851: Thief 434 at
/// 150 of 1500 approached at 4912 a tick, half its walk of 9824.</summary>
public class LowHealthSpeedTests
{
    [Fact]
    public void ACreatureUnderAFifthOfItsHealthHasHalfItsSpeed_AtAFifthItsWhole()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(10, 10), [(byte)0, PlayerIds.FirstKeeper]);
        CreatureState goblin = world.SpawnArrived(db, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.FirstKeeper, new TilePos(5, 5));
        CreatureDef definition = db.CreatureById(goblin.CreatureTypeId);
        int fifth = CreatureLevelRules.MaxHealth(db, definition, goblin.Level) * 20 / 100;
        const int cap = 614 << 4;

        goblin.Health = fifth;
        Assert.Equal(cap, LowHealthSpeedRules.SpeedCap(db, definition, goblin, cap));
        goblin.Health = fifth - 1;
        Assert.Equal(307 << 4, LowHealthSpeedRules.SpeedCap(db, definition, goblin, cap));

        // The maximum is the level-scaled one.
        goblin.Level = 5;
        int fifthAtFive = CreatureLevelRules.MaxHealth(db, definition, 5) * 20 / 100;
        Assert.NotEqual(fifth, fifthAtFive);
        goblin.Health = fifthAtFive;
        Assert.Equal(cap, LowHealthSpeedRules.SpeedCap(db, definition, goblin, cap));
        goblin.Health = fifthAtFive - 1;
        Assert.Equal(307 << 4, LowHealthSpeedRules.SpeedCap(db, definition, goblin, cap));
    }

    // 0x00489639: a worker skips the whole branch.
    [Fact]
    public void AnImpUnderAFifthOfItsHealthKeepsItsSpeedAndItsRecharge()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(10, 10), [(byte)0, PlayerIds.FirstKeeper]);
        CreatureState imp = world.SpawnArrived(db, Dk2TerrainFixture.ImpCreatureId, PlayerIds.FirstKeeper, new TilePos(5, 5));
        CreatureDef definition = db.CreatureById(imp.CreatureTypeId);
        imp.Health = 1;
        imp.TimeLastUsedMelee = 100;
        Assert.Equal(614 << 4, LowHealthSpeedRules.SpeedCap(db, definition, imp, 614 << 4));
        LowHealthSpeedRules.DelayRecharge(db, definition, imp, 101);
        Assert.Equal(100, imp.TimeLastUsedMelee);
    }

    // 0x004896C8..0x00489705: on odd turns only, every used stamp moves on one; never-used stays.
    [Fact]
    public void AWeakCreaturesStampsMoveOnOneOnOddTurns()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(10, 10), [(byte)0, PlayerIds.FirstKeeper]);
        CreatureState goblin = world.SpawnArrived(db, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.FirstKeeper, new TilePos(5, 5));
        CreatureDef definition = db.CreatureById(goblin.CreatureTypeId);
        goblin.Health = 1;
        goblin.TimeLastUsedMelee = 100;
        goblin.SpellStamps[0] = 90;

        LowHealthSpeedRules.DelayRecharge(db, definition, goblin, 102);
        Assert.Equal((100L, 90L), (goblin.TimeLastUsedMelee, goblin.SpellStamps[0]));
        LowHealthSpeedRules.DelayRecharge(db, definition, goblin, 103);
        Assert.Equal((101L, 91L, CreatureState.NeverUsed), (goblin.TimeLastUsedMelee, goblin.SpellStamps[0], goblin.SpellStamps[1]));
    }
}
