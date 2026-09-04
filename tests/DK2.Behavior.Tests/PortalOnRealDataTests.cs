// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Systems;

namespace DK2.Behavior.Tests;

/// <summary>
/// The portal, against the shipped v1.70 install rather than a transcription.
///
/// <para>Two jobs. The first is the usual drift guard: <c>Dk2TerrainFixture</c>
/// now carries a Portal room and a Goblin, and a transcription can rot, so
/// every number the portal rules read is re-read out of the real files here.
/// That guard has caught a factor-of-4096 error before.</para>
///
/// <para>The second is more interesting: <b>several of the portal's rules rest
/// on claims about the shipped data as a whole</b>, not just about one record.
/// That ThreeByThree picks out exactly the two portals. That player 0 is nobody.
/// That no level uses RESEARCHABLE for a creature. That no level lists the same
/// creature twice in its pool. Each of those is a sentence in a doc comment that
/// something in <see cref="PortalRules"/> relies on, and each is checked here
/// against all 66 levels — because a claim nothing checks may already be
/// false.</para>
///
/// <para>Skips when no installation is configured (DK2_DIR).</para>
/// </summary>
public class PortalOnRealDataTests
{
    private const byte GoblinId = 7;

    private static string? Install() => Dk2Install.Find();

    private static IEnumerable<string> CampaignLevels(string install)
    {
        string maps = Path.Combine(install, "Data", "editor", "maps");
        foreach (string file in Directory.GetFiles(maps, "*Variables.kld"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            yield return name[..^"Variables".Length];
        }
    }

    // ---- the drift guard --------------------------------------------------

    [Fact]
    public void ThePortalRecordIsWhatTheFixtureSaysItIs()
    {
        string? install = Install();
        if (install is null) return;

        RoomDef portal = GameDatabaseLoader.Load(install).Room(RoomTypeId.Portal);

        Assert.Equal("Portal", portal.Name);
        Assert.Equal(12, portal.TerrainId);
        Assert.Equal(3, portal.RecommendedSizeX);
        Assert.Equal(3, portal.RecommendedSizeY);
        Assert.Equal(TileConstruction.ThreeByThree, portal.Construction);

        // The three bits that make a portal something you find rather than
        // something you build. 0x1004 exactly: PlaceableOnLand and HasFlame.
        Assert.Equal(0x1004u, (uint)portal.Flags);
        Assert.False(portal.IsBuildable);
        Assert.Equal(0, portal.Cost);
    }

    [Fact]
    public void TheGoblinRecordIsWhatTheFixtureSaysItIs()
    {
        string? install = Install();
        if (install is null) return;

        CreatureDef goblin = GameDatabaseLoader.Load(install).CreatureById(GoblinId);

        Assert.Equal("Goblin", goblin.Name);
        Assert.Equal(1500, goblin.Hp);
        Assert.Equal(170, goblin.MeleeDamage);
        Assert.Equal(500, goblin.Pay);
        Assert.Equal(50, goblin.Fear);
        Assert.Equal(37, goblin.Threat);
        Assert.Equal(2867, goblin.Speed.Raw);
        Assert.False(goblin.Flags.HasFlag(CreatureFlags.IsWorker));

        // The two flags the entrance itself tests, and the fixture Goblin has
        // to carry them or it never comes out of a portal at all.
        Assert.True(goblin.Flags.HasFlag(CreatureFlags.AvailableViaPortal));
        Assert.False(goblin.Flags.HasFlag(CreatureFlags.IsUnique));
        Assert.True(PortalRules.CanComeFromPortal(goblin));

        // One slot, wanting a Lair of any size. This is the fact that makes
        // level 1 produce nothing until its keeper builds a lair.
        Assert.Equal((byte)RoomTypeId.Lair, (byte)goblin.Attractions[0].RoomId);
        Assert.Equal(0, goblin.Attractions[0].RoomSize);
        Assert.NotEqual(0u, goblin.Attractions[0].Present);
        Assert.Equal(0, goblin.Attractions[1].RoomId);
    }

    // ---- level 1, end to end ---------------------------------------------

    [Fact]
    public void LevelOneUnlocksOneCreatureAndFifteenOfIt()
    {
        string? install = Install();
        if (install is null) return;

        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(install, "Level1");

        // Player 3 is level 1's keeper. The whole pool is fifteen Goblins, and
        // the whole creature availability set is that one creature.
        Assert.Equal(15, PortalRules.PoolLimit(db, 3, GoblinId));
        Assert.True(PortalRules.IsUnlocked(db, 3, GoblinId));

        Assert.Single(db.Variables.CreaturePool);
        Assert.Single(db.Variables.Availabilities, a => a.Kind == 2);

        // And every other creature is barred, which is what makes it a tutorial.
        foreach (CreatureDef creature in db.Creatures.Values)
        {
            if (creature.CreatureId == GoblinId) continue;
            Assert.False(PortalRules.IsUnlocked(db, 3, creature.CreatureId));
        }
    }

    [Fact]
    public void LevelOnesPortalRunsAtEighteenSecondsAndNotTwentyFive()
    {
        string? install = Install();
        if (install is null) return;

        // The per-level override is the reason this work needed ADR-0040 first.
        Assert.Equal(
            25, GameDatabaseLoader.Load(install)[VariableTypeId.EntranceGenerationSpeedSeconds]);

        GameDatabase level1 = GameDatabaseLoader.LoadForCampaignLevel(install, "Level1");
        Assert.Equal(18, level1[VariableTypeId.EntranceGenerationSpeedSeconds]);

        Assert.Equal(
            18 * SimulationTiming.TicksPerSecond,
            PortalRules.SpawnIntervalTicks(level1, 0));
    }

    /// <summary>
    /// The whole mechanic on the real level 1 map: find its portal, give its
    /// keeper a lair, and watch a goblin come out.
    ///
    /// <para>This is the only test that exercises the loader, the map, the
    /// per-level variables, the room index and the portal system together, and
    /// it is the one that would have caught the per-level-variables defect on
    /// its own.</para>
    /// </summary>
    [Fact]
    public void LevelOnesOwnPortalProducesAGoblinOnceThereIsALair()
    {
        string? install = Install();
        if (install is null) return;

        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(install, "Level1");
        LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, "Level1");
        if (map is null) return;

        var world = WorldState.FromMap(db, map, [(byte)0, (byte)3, (byte)4, (byte)5, (byte)6]);

        // Level 1's keeper is player 3 and its portal starts unclaimed, so hand
        // it to them the way claiming would. Nine tiles of portal terrain.
        RoomInstance portal = RoomIndex.Build(world, db)
            .First(r => PortalRules.IsKeeperPortal(r.Definition));
        foreach (int index in portal.TileIndices)
            world.TileAtIndex(index).OwnerPlayerId = 3;

        // A lair, on a tile the keeper already owns.
        RoomDef lair = db.Room(RoomTypeId.Lair);
        int floor = FirstTileOwnedBy(world, db, 3);
        RoomRules.Place(db, ref world.TileAtIndex(floor), lair, 3);

        var sim = new GameSimulation(db, world, seed: 5, [new PortalSystem()]);
        sim.Run(18 * SimulationTiming.TicksPerSecond);

        CreatureState arrived = Assert.Single(world.Creatures);
        Assert.Equal(GoblinId, arrived.CreatureTypeId);
        Assert.Equal((byte)3, arrived.OwnerPlayerId);
    }

    private static int FirstTileOwnedBy(WorldState world, GameDatabase db, byte playerId)
    {
        for (int i = 0; i < world.TileCount; i++)
        {
            ref TileState tile = ref world.TileAtIndex(i);
            if (tile.OwnerPlayerId != playerId) continue;
            TerrainDef terrain = db.TerrainById(tile.TerrainId);
            if (terrain.IsSolid || terrain.IsRoom) continue;
            return i;
        }
        throw new InvalidOperationException($"no plain owned floor for player {playerId}");
    }

    // ---- the claims the rules rest on, across all 66 levels ---------------

    [Fact]
    public void ThreeByThreeIsExactlyThePortalAndTheHeroPortal()
    {
        string? install = Install();
        if (install is null) return;

        string[] names = GameDatabaseLoader.Load(install).Rooms.Values
            .Where(r => r.Construction == TileConstruction.ThreeByThree)
            .OrderBy(r => r.RoomId)
            .Select(r => r.Name)
            .ToArray();

        // PortalRules.IsEntrance names two rooms by id and justifies it by this.
        Assert.Equal(["Portal", "Hero Portal"], names);
    }

    [Fact]
    public void NoLevelEverMakesACreatureMerelyResearchable()
    {
        string? install = Install();
        if (install is null) return;

        // PortalRules.IsUnlocked accepts RESEARCHABLE as well as AVAILABLE,
        // following OpenKeeper. If no level uses it, the two readings can never
        // disagree — and if one ever does, this is where it surfaces rather
        // than in a portal quietly producing something.
        int checkedLevels = 0;
        foreach (string level in CampaignLevels(install))
        {
            GlobalVariables? vars = GameDatabaseLoader.LoadCampaignVariables(install, level);
            if (vars is null) continue;
            checkedLevels++;

            foreach (AvailabilityEntry entry in vars.Availabilities)
            {
                if (entry.Kind != 2) continue;
                Assert.True(
                    entry.Value is 1 or 3,
                    $"{level}: creature {entry.TypeId} has availability {entry.Value}, " +
                    "which is neither UNAVAILABLE nor AVAILABLE");
            }
        }

        Assert.True(checkedLevels > 50, $"expected the whole campaign, read {checkedLevels}");
    }

    [Fact]
    public void NoLevelListsTheSameCreatureTwiceInItsPool()
    {
        string? install = Install();
        if (install is null) return;

        // PortalRules.PoolLimit takes the last matching entry. Nothing should
        // ever depend on that, and this is why it does not.
        int checkedLevels = 0;
        foreach (string level in CampaignLevels(install))
        {
            GlobalVariables? vars = GameDatabaseLoader.LoadCampaignVariables(install, level);
            if (vars is null) continue;
            checkedLevels++;

            List<(int, int)> keys = vars.CreaturePool
                .Select(e => (e.PlayerId, e.CreatureId)).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
        }

        Assert.True(checkedLevels > 50, $"expected the whole campaign, read {checkedLevels}");
    }

    /// <summary>
    /// <see cref="PlayerIds"/>, checked against the whole campaign — the fact
    /// that lets <see cref="PortalSystem"/> tell a keeper's portal from an
    /// unclaimed one.
    ///
    /// <para><b>The obvious argument for it is wrong, and it is worth recording
    /// why.</b> "Unclaimed things belong to player 0" fails twice over on the
    /// shipped maps. Solid rock carries a <i>non-zero</i> owner all over the
    /// campaign, because DK2 colours the walls bordering a keeper's territory in
    /// that keeper's colour (<c>TerrainFlags.PlayerColouredWall</c> exists for
    /// exactly that) — so ownership of rock is a rendering fact, not a claim
    /// about who holds the tile. And an unclaimed <i>room</i> is not player 0 at
    /// all: it is player 2, the neutral player.</para>
    ///
    /// <para><b>What actually holds</b> is the four-way split: 0 unowned, 1 the
    /// hero side, 2 neutral, 3 and up keepers. Two campaign-wide facts pin it —
    /// only players 3 to 7 ever hold a dungeon heart, and only players 3 to 7
    /// are ever dealt a creature pool or creature availability — and OpenKeeper's
    /// <c>Player</c> constants agree from the other direction.</para>
    /// </summary>
    [Fact]
    public void TheReservedPlayerIdsAreWhatTheCampaignSays()
    {
        string? install = Install();
        if (install is null) return;

        GameDatabase db = GameDatabaseLoader.Load(install);
        byte heartTerrain = db.Room(RoomTypeId.DungeonHeart).TerrainId;
        byte portalTerrain = db.Room(RoomTypeId.Portal).TerrainId;

        int levelsRead = 0;
        int hearts = 0;
        int neutralPortalTiles = 0;
        var poolPlayers = new SortedSet<int>();

        foreach (string level in CampaignLevels(install))
        {
            GlobalVariables? vars = GameDatabaseLoader.LoadCampaignVariables(install, level);
            if (vars is not null)
            {
                // Nobody below a keeper is ever dealt creatures. This is the
                // half that makes PortalSystem's gate a no-op for correctness
                // and a decision only about the clock.
                foreach (CreaturePoolEntry entry in vars.CreaturePool)
                {
                    Assert.True(PlayerIds.IsKeeper((byte)entry.PlayerId),
                        $"{level}: creature pool for player {entry.PlayerId}");
                    poolPlayers.Add(entry.PlayerId);
                }

                foreach (AvailabilityEntry entry in vars.Availabilities)
                {
                    if (entry.Kind != 2) continue;
                    Assert.True(PlayerIds.IsKeeper((byte)entry.PlayerId),
                        $"{level}: creature availability for player {entry.PlayerId}");
                }
            }

            LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, level);
            if (map is null) continue;
            levelsRead++;

            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    MapTile tile = map[x, y];

                    if (tile.TerrainId == heartTerrain)
                    {
                        // A dungeon heart is the mark of a keeper, and only a
                        // keeper ever holds one.
                        Assert.True(PlayerIds.IsKeeper((byte)tile.PlayerId),
                            $"{level}: dungeon heart at ({x},{y}) owned by player {tile.PlayerId}");
                        hearts++;
                    }
                    else if (tile.TerrainId == portalTerrain && tile.PlayerId == PlayerIds.Neutral)
                    {
                        neutralPortalTiles++;
                    }
                }
            }
        }

        Assert.True(levelsRead > 50, $"expected the whole campaign, read {levelsRead}");
        Assert.True(hearts > 50, $"expected many dungeon-heart tiles, saw {hearts}");

        // Unclaimed portals are the neutral player's, and there are far more of
        // them than of any other owner — which is what "found and claimed"
        // looks like in the shipped data.
        Assert.True(neutralPortalTiles > 500,
            $"expected most portals to start neutral, saw {neutralPortalTiles} tiles");

        // Keepers run 3 to 7 and no further.
        Assert.Equal([3, 4, 5, 6, 7], poolPlayers.ToArray());
    }

    /// <summary>
    /// What the whole campaign's portals look like: how many levels have one,
    /// and whether any starts out belonging to a keeper.
    ///
    /// <para>Not a rule — a survey, and it exists because "a portal is claimed
    /// rather than built" is a claim about the shipped maps and not only about
    /// the room record's flags. If some level ships a portal already owned, the
    /// claiming path is not the only way a keeper gets one.</para>
    /// </summary>
    [Fact]
    public void EveryShippedPortalIsThreeByThreeTerrain()
    {
        string? install = Install();
        if (install is null) return;

        GameDatabase db = GameDatabaseLoader.Load(install);
        byte portalTerrain = db.Room(RoomTypeId.Portal).TerrainId;

        int levelsWithPortals = 0;
        int portals = 0;
        foreach (string level in CampaignLevels(install))
        {
            LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, level);
            if (map is null) continue;

            var world = WorldState.FromMap(db, map, [(byte)0]);
            List<RoomInstance> found = RoomIndex.Build(world, db)
                .Where(r => r.Definition.TerrainId == portalTerrain)
                .ToList();
            if (found.Count == 0) continue;

            levelsWithPortals++;
            portals += found.Count;

            // Every one of them nine tiles in a 3x3, so the centre tile the
            // spawn clock lives on is always a real tile of the room.
            foreach (RoomInstance portal in found)
            {
                Assert.Equal(9, portal.TileCount);
                Assert.True(PortalRules.CanProduce(world, portal),
                    $"{level}: a portal at ({portal.MinX},{portal.MinY}) cannot produce");
            }
        }

        Assert.True(levelsWithPortals > 20,
            $"expected many levels with portals, found {levelsWithPortals}");
        Assert.True(portals >= levelsWithPortals);
    }

    // ---- gate 0: the creature flags, across the whole campaign ------------

    [Fact]
    public void TheCampaignUnlocksNineCreaturesOnlyTheFlagGateKeepsOut()
    {
        string? install = Install();
        if (install is null) return;

        // PortalRules.CanComeFromPortal exists because of these nine. On the
        // three Frosty levels every keeper from 3 to 6 has the hero roster both
        // AVAILABLE and in its creature pool — so the level gate and the pool
        // gate both pass — and none of the nine carries AvailableViaPortal.
        // Without the flag gate a keeper's portal there produces Knights.
        //
        // Asserted as an exact set, in both directions: if a tenth appears, or
        // one of these stops being unlocked, that is a fact about the shipped
        // campaign that changed and it should be read again rather than
        // absorbed.
        GameDatabase db = GameDatabaseLoader.Load(install);
        var blocked = new SortedSet<string>();
        int checkedLevels = 0;

        foreach (string level in CampaignLevels(install))
        {
            GlobalVariables? vars = GameDatabaseLoader.LoadCampaignVariables(install, level);
            if (vars is null) continue;
            checkedLevels++;

            foreach (AvailabilityEntry entry in vars.Availabilities)
            {
                if (entry.Kind != 2 || entry.Value != 3) continue;
                if (!PlayerIds.IsKeeper((byte)entry.PlayerId)) continue;

                CreatureDef creature = db.CreatureById((byte)entry.TypeId);
                if (!PortalRules.CanComeFromPortal(creature))
                    blocked.Add(creature.Name);
            }
        }

        Assert.True(checkedLevels > 50, $"expected the whole campaign, read {checkedLevels}");
        Assert.Equal(
            new SortedSet<string>
            {
                "Elven Archer", "Fairy", "Giant", "Guard", "Knight",
                "Monk", "Royal Guard", "Thief", "Wizard",
            },
            blocked);
    }

    [Fact]
    public void NoShippedLevelEverPoolsOrUnlocksAUniqueCreature()
    {
        string? install = Install();
        if (install is null) return;

        // Which is why the IsUnique half of the flag gate never fires on
        // shipped data, and is therefore unfalsifiable from the files: it rests
        // on the binary alone (ADR-0046). It is implemented because all
        // thirteen uniques *do* carry AvailableViaPortal, so the day a level
        // pools one, the flag alone would hand it over. This test is what makes
        // "never fires" a checked claim rather than an assumption.
        GameDatabase db = GameDatabaseLoader.Load(install);
        int checkedLevels = 0;

        foreach (string level in CampaignLevels(install))
        {
            GlobalVariables? vars = GameDatabaseLoader.LoadCampaignVariables(install, level);
            if (vars is null) continue;
            checkedLevels++;

            foreach (AvailabilityEntry entry in vars.Availabilities)
            {
                if (entry.Kind != 2) continue;
                Assert.False(
                    db.CreatureById((byte)entry.TypeId).Flags.HasFlag(CreatureFlags.IsUnique),
                    $"{level}: availability lists unique creature {entry.TypeId}");
            }

            foreach (CreaturePoolEntry entry in vars.CreaturePool)
            {
                Assert.False(
                    db.CreatureById((byte)entry.CreatureId).Flags.HasFlag(CreatureFlags.IsUnique),
                    $"{level}: pool lists unique creature {entry.CreatureId}");
            }
        }

        Assert.True(checkedLevels > 50, $"expected the whole campaign, read {checkedLevels}");
    }

    [Fact]
    public void TheThirteenUniqueCreaturesAllCarryThePortalFlag()
    {
        string? install = Install();
        if (install is null) return;

        // The premise of the paragraph above, checked. If uniques did not carry
        // the portal flag, DK2 would not have needed a second test and neither
        // would we.
        GameDatabase db = GameDatabaseLoader.Load(install);
        List<CreatureDef> uniques = db.Creatures.Values
            .Where(c => c.Flags.HasFlag(CreatureFlags.IsUnique)).ToList();

        Assert.Equal(13, uniques.Count);
        Assert.All(uniques, c =>
            Assert.True(c.Flags.HasFlag(CreatureFlags.AvailableViaPortal), c.Name));
    }
}
