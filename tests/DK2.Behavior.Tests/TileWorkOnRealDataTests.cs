using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Behavior.Tests;

/// <summary>
/// The M3 tile rules run against the real game's data rather than a
/// transcription of it.
///
/// <c>DK2.Simulation.Tests.TileWorkTests</c> proves the rules are correct given
/// the numbers; these prove the numbers are the ones Dungeon Keeper 2 ships. If
/// the fixture in the simulation tests ever drifts from the install, these fail
/// and that one does not — which is the point of having both.
///
/// Skips when no installation is configured (DK2_DIR), so CI stays green
/// without proprietary assets.
/// </summary>
public class TileWorkOnRealDataTests
{
    private static GameDatabase? RealDatabase()
    {
        string? install = Dk2Install.Find();
        return install is null ? null : GameDatabaseLoader.Load(install);
    }

    /// <summary>A tile of terrain <paramref name="id"/> at its shipped starting health.</summary>
    private static TileState Tile(GameDatabase db, byte id, byte owner = 0) => new()
    {
        TerrainId = id,
        OwnerPlayerId = owner,
        Health = db.TerrainById(id).StartingHealth,
    };

    private static byte IdOf(GameDatabase db, string name) =>
        db.Terrain.Values.Single(t => t.Name == name).TerrainId;

    [Fact]
    public void TheTerrainNamesTheRulesRelyOn_AreAllPresent()
    {
        GameDatabase? db = RealDatabase();
        if (db is null) return; // skipped: no DK2 install

        foreach (string name in new[]
                 {
                     "Impenetrable Rock", "Rock", "Dirt Path", "Gold", "Gems",
                     "Claimed Path", "Reinforced Wall",
                 })
        {
            Assert.Single(db.Terrain.Values, t => t.Name == name);
        }
    }

    [Fact]
    public void TheFixtureNumbers_MatchTheInstall()
    {
        GameDatabase? db = RealDatabase();
        if (db is null) return;

        void Check(string name, ushort start, ushort max, byte toMax, byte toDead, ushort gold)
        {
            TerrainDef t = db.Terrain.Values.Single(x => x.Name == name);
            Assert.Equal(start, t.StartingHealth);
            Assert.Equal(max, t.MaxHealth);
            Assert.Equal(toMax, t.MaxHealthTypeTerrainId);
            Assert.Equal(toDead, t.DestroyedTypeTerrainId);
            Assert.Equal(gold, t.GoldValue);
        }

        Check("Impenetrable Rock", 1, 1, 1, 1, 0);
        Check("Rock", 165, 660, 9, 3, 0);
        Check("Dirt Path", 1, 660, 8, 3, 0);
        Check("Gold", 660, 660, 6, 3, 3000);
        Check("Gems", 100, 100, 7, 7, 3000);
        Check("Claimed Path", 660, 660, 2, 3, 0);
        Check("Reinforced Wall", 900, 900, 9, 2, 0);

        Assert.Equal(825, db[VariableTypeId.ClaimTileHealth]);
        Assert.Equal(-140, db[VariableTypeId.AttackTileHealth]);
        Assert.Equal(125, db[VariableTypeId.RepairTileHealth]);
        Assert.Equal(-300, db[VariableTypeId.MineGoldHealth]);
        Assert.Equal(-5000, db[VariableTypeId.DigRockHealth]);
        Assert.Equal(-130, db[VariableTypeId.DigOwnWallHealth]);
        Assert.Equal(-8, db[VariableTypeId.DigEnemyWallHealth]);
        Assert.Equal(5, db[VariableTypeId.FillInHealth]);
        Assert.Equal(50, db[VariableTypeId.ReinforceWallHealth]);
        Assert.Equal(22, db[VariableTypeId.RepairWallHealth]);
        Assert.Equal(25, db[VariableTypeId.GoldMinedFromGems]);
    }

    [Fact]
    public void TheDigClaimReinforceCycle_WorksOnRealData()
    {
        GameDatabase? db = RealDatabase();
        if (db is null) return;

        byte rock = IdOf(db, "Rock");
        TileState tile = Tile(db, rock);

        // TWO swings on real data, which is what the owner counted in the
        // original. The shipped DigRockHealth would do it in one; it is the
        // project's single ObservedCorrections entry, and asserting the count
        // here rather than looping is what would catch the correction being
        // removed or widened. See ADR-0023 and Q20.
        Assert.Equal(TileWorkOutcome.Progressed,
            TileWorkRules.Apply(db, ref tile, TileWorkKind.Dig, 1).Outcome);
        Assert.Equal(rock, tile.TerrainId);

        Assert.Equal(TileWorkOutcome.Destroyed,
            TileWorkRules.Apply(db, ref tile, TileWorkKind.Dig, 1).Outcome);
        Assert.Equal(IdOf(db, "Dirt Path"), tile.TerrainId);

        // Four swings to claim one tile against the real numbers (Q47), which is
        // the owner's "2-3-4 jumps MAX" (Q46) — asserted rather than looped, so
        // that a change to the application rate shows up here too.
        for (int i = 0; i < 3; i++)
            Assert.Equal(TileWorkOutcome.Progressed,
                TileWorkRules.Apply(db, ref tile, TileWorkKind.Claim, 1).Outcome);

        Assert.Equal(TileWorkOutcome.Transformed,
            TileWorkRules.Apply(db, ref tile, TileWorkKind.Claim, 1).Outcome);
        Assert.Equal(IdOf(db, "Claimed Path"), tile.TerrainId);
        Assert.Equal(1, tile.OwnerPlayerId);

        TileState wall = Tile(db, rock);
        int swings = 0;
        while (wall.TerrainId == rock)
        {
            TileWorkRules.Apply(db, ref wall, TileWorkKind.Reinforce, 1);
            swings++;
        }
        Assert.Equal(IdOf(db, "Reinforced Wall"), wall.TerrainId);

        // FIFTY swings: Rock 165 -> its maxHealth 660 is 495 health at 10 a
        // swing (ReinforceWallHealth 50, a fifth each). At five a second that
        // is 10.0 seconds of work.
        //
        // **The owner measured 14-15 seconds in the original (Q62)**, so this
        // is pinned as the model's prediction rather than as a confirmed
        // behaviour. Some of the gap is walking up and animating - claiming
        // takes 4 swings (0.8s) and was observed at "about 2 seconds", so
        // roughly 1.2s of overhead - but not 4.5s of it. The number is here so
        // that whichever way Q62 resolves, this test has to be edited
        // deliberately.
        Assert.Equal(50, swings);
    }

    [Fact]
    public void NoTerrainInTheGame_UpgradesItselfOutOfExistenceAtRest()
    {
        GameDatabase? db = RealDatabase();
        if (db is null) return;

        // Six terrain types ship at maximum health with a max-health type that
        // is something else. Every one of them would mutate on the first tick
        // under a level-triggered upgrade rule, which is the evidence that the
        // rule has to be work-triggered instead (Q18).
        List<string> atRestButPointingElsewhere = db.Terrain.Values
            .Where(t => t.StartingHealth >= t.MaxHealth && t.MaxHealthTypeTerrainId != t.TerrainId)
            .Select(t => t.Name)
            .ToList();

        Assert.NotEmpty(atRestButPointingElsewhere);
        Assert.Contains("Claimed Path", atRestButPointingElsewhere);

        // The rule we actually implement leaves all of them alone.
        foreach (TerrainDef t in db.Terrain.Values)
        {
            TileState tile = Tile(db, t.TerrainId, owner: 1);
            byte before = tile.TerrainId;
            TileWorkRules.Apply(db, ref tile, TileWorkKind.RepairTile, 1);
            TileWorkRules.Apply(db, ref tile, TileWorkKind.RepairWall, 1);
            Assert.Equal(before, tile.TerrainId);
        }
    }

    [Fact]
    public void EveryTerrainTransitionTargetInTheGame_Resolves()
    {
        GameDatabase? db = RealDatabase();
        if (db is null) return;

        foreach (TerrainDef t in db.Terrain.Values)
        {
            Assert.True(db.Terrain.ContainsKey(t.MaxHealthTypeTerrainId),
                $"{t.Name} upgrades to unknown terrain {t.MaxHealthTypeTerrainId}");
            Assert.True(db.Terrain.ContainsKey(t.DestroyedTypeTerrainId),
                $"{t.Name} is destroyed into unknown terrain {t.DestroyedTypeTerrainId}");
        }
    }

    [Fact]
    public void DiggingOutARealCampaignLevel_LeavesOnlyFloorAndImpenetrableRock()
    {
        GameDatabase? db = RealDatabase();
        if (db is null) return;

        string install = Dk2Install.Find()!;
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(install, "level1")!;
        WorldState world = WorldState.FromMap(
            db, map, map.Tiles.Select(t => t.PlayerId).Distinct());

        var sim = new GameSimulation(db, world, seed: 1, [new Simulation.Systems.TileWorkSystem()]);

        // Throw every applicable kind of removal at every tile, pass after pass,
        // until nothing more can be removed. The terrain graph has to terminate:
        // every destroyed type must eventually reach something that cannot be
        // dug, or an imp in the real game would grind at a tile forever.
        //
        // The bound is generous on purpose, and it is the slowest job in the game
        // that sets it: a reinforced wall belonging to another player is 900
        // health at DigEnemyWallHealth = -8 a second, and at five applications a
        // second (Q47) that is 563 swings before it even reverts to rock. Each
        // pass here queues one application per tile, so the bound is in swings.
        const int maxPasses = 1200;
        int passes = 0;
        bool workRemains = true;

        while (workRemains && passes < maxPasses)
        {
            workRemains = false;
            for (int i = 0; i < world.TileCount; i++)
            {
                ref TileState tile = ref world.TileAtIndex(i);
                foreach (TileWorkKind kind in new[]
                         {
                             TileWorkKind.Dig, TileWorkKind.MineGold,
                             TileWorkKind.DigOwnWall, TileWorkKind.DigEnemyWall,
                         })
                {
                    // Gem faces are inexhaustible by design; mining one forever
                    // is correct behaviour, not a failure to converge.
                    if (db.TerrainById(tile.TerrainId).IsImpenetrable) continue;

                    if (TileWorkRules.Applies(db, tile, kind, 1))
                    {
                        world.QueueWork(new TileWorkOrder(i, kind, 1));
                        workRemains = true;
                    }
                }
            }
            sim.Tick();
            passes++;
        }

        Assert.False(workRemains, $"the terrain graph had not settled after {maxPasses} passes");

        for (int i = 0; i < world.TileCount; i++)
        {
            TerrainDef t = db.TerrainById(world.TileAtIndex(i).TerrainId);
            Assert.True(!t.IsSolid || t.IsImpenetrable || t.IsRoom,
                $"tile {i} is still diggable {t.Name} after six passes");
        }
    }
}
