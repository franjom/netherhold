// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Systems;
using DK2.TestSupport;

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
/// <para>Gated with [InstallFact], so it reports SKIPPED without an installation rather than passing.</para>
/// </summary>
public class PortalOnRealDataTests
{
    private const byte GoblinId = 7;

    private static string Install() => Dk2InstallGate.Path;

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

    [InstallFact]
    public void ThePortalRecordIsWhatTheFixtureSaysItIs()
    {
        string install = Install();

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

    [InstallFact]
    public void TheGoblinRecordIsWhatTheFixtureSaysItIs()
    {
        string install = Install();

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
        Assert.True(PortalRules.CanComeFromPortal(
            goblin, GameDatabaseLoader.Load(install!).Room(RoomTypeId.Portal)));

        // One slot, wanting a Lair of any size: size 0, which a keeper with no
        // Lair meets (ADR-0499, TheShippedGoblinNeedsNoLairToBeAttracted).
        Assert.Equal((byte)RoomTypeId.Lair, (byte)goblin.Attractions[0].RoomId);
        Assert.Equal(0, goblin.Attractions[0].RoomSize);
        Assert.NotEqual(0u, goblin.Attractions[0].Present);
        Assert.Equal(0, goblin.Attractions[1].RoomId);
    }

    // ---- level 1, end to end ---------------------------------------------

    [InstallFact]
    public void LevelOneUnlocksOneCreatureAndFifteenOfIt()
    {
        string install = Install();

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

    [InstallFact]
    public void LevelOnesPortalRunsAtEighteenSecondsAndNotTwentyFive()
    {
        string install = Install();

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
    [InstallFact]
    public void LevelOnesOwnPortalProducesAGoblinOnceThereIsALair()
    {
        string install = Install();

        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(install, "Level1");
        LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, "Level1");
        Assert.NotNull(map);

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

    /// <summary>The shipped Goblin asks for a Lair of size 0, which no Lair at all meets
    /// (0x004DC810 refuses only a total below the size). DK2's Level 4 keeper claimed its
    /// portal at 787 and had a Goblin at 887, with its first Lair at 1100.</summary>
    [InstallFact]
    public void TheShippedGoblinNeedsNoLairToBeAttracted()
    {
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(Install(), "Level4");
        CreatureDef goblin = db.CreatureById(GoblinId);

        Assert.Equal(new CreatureAttraction(1, (byte)RoomTypeId.Lair, 0), goblin.Attractions[0]);
        Assert.True(PortalRules.IsAttracted(goblin, []));
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

    [InstallFact]
    public void ThreeByThreeIsExactlyThePortalAndTheHeroPortal()
    {
        string install = Install();

        string[] names = GameDatabaseLoader.Load(install).Rooms.Values
            .Where(r => r.Construction == TileConstruction.ThreeByThree)
            .OrderBy(r => r.RoomId)
            .Select(r => r.Name)
            .ToArray();

        // PortalRules.IsEntrance names two rooms by id and justifies it by this.
        Assert.Equal(["Portal", "Hero Portal"], names);
    }

    [InstallFact]
    public void NoLevelEverMakesACreatureMerelyResearchable()
    {
        string install = Install();

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

    [InstallFact]
    public void NoLevelListsTheSameCreatureTwiceInItsPool()
    {
        string install = Install();

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
    [InstallFact]
    public void TheReservedPlayerIdsAreWhatTheCampaignSays()
    {
        string install = Install();

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

            Assert.NotNull(map);
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
    [InstallFact]
    public void EveryShippedPortalIsThreeByThreeTerrain()
    {
        string install = Install();

        GameDatabase db = GameDatabaseLoader.Load(install);
        byte portalTerrain = db.Room(RoomTypeId.Portal).TerrainId;

        int levelsWithPortals = 0;
        int portals = 0;
        foreach (string level in CampaignLevels(install))
        {
            LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, level);
            Assert.NotNull(map);
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

    [InstallFact]
    public void TheCampaignUnlocksNineCreaturesOnlyTheFlagGateKeepsOut()
    {
        string install = Install();

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
        RoomDef keeperPortal = db.Room(RoomTypeId.Portal);
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
                if (!PortalRules.CanComeFromPortal(creature, keeperPortal))
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

    /// <summary>
    /// The Hero Portal's own creature flag, on the shipped records — and the
    /// claim that makes it trustworthy: <b>the two flags partition the
    /// portal-generated roster.</b>
    ///
    /// <para>The nine creatures carrying <c>AvailableViaHeroPortal</c> are
    /// exactly the nine that
    /// <see cref="TheCampaignUnlocksNineCreaturesOnlyTheFlagGateKeepsOut"/>
    /// arrives at from the opposite direction — the creatures the campaign makes
    /// available to keepers that only the Portal's flag gate keeps out. Two
    /// independent reads of the shipped data meeting on the same nine names is
    /// the evidence that bit 3 of the second flag field is what the binary's
    /// <c>def+0x68a &amp; 8</c> tests.</para>
    ///
    /// <para><b>And the Dwarf is not among them</b>, which is the detail that
    /// rejected the near miss. Bit 30 of the <i>first</i> flag dword selects
    /// these nine plus the Dwarf and is named <c>MpdRandomInvader</c> from
    /// OpenKeeper; gating a Hero Portal on it would have it emitting the heroes'
    /// worker. Each side's portal excludes its own worker.</para>
    /// </summary>
    [InstallFact]
    public void NineCreaturesCarryTheHeroPortalFlagAndTheyAreTheNineThePortalRefuses()
    {
        string install = Install();

        GameDatabase db = GameDatabaseLoader.Load(install);
        RoomDef portal = db.Room(RoomTypeId.Portal);
        RoomDef heroPortal = db.Room(RoomTypeId.HeroPortal);

        var heroSide = new SortedSet<string>();
        foreach (CreatureDef creature in db.Creatures.Values)
        {
            if (creature.Flags2.HasFlag(CreatureFlags2.AvailableViaHeroPortal))
                heroSide.Add(creature.Name);
        }

        Assert.Equal(
            new SortedSet<string>
            {
                "Elven Archer", "Fairy", "Giant", "Guard", "Knight",
                "Monk", "Royal Guard", "Thief", "Wizard",
            },
            heroSide);

        // The heroes' worker is excluded, exactly as the Imp is on the other
        // side. This is the assertion that fails if anyone re-points the flag at
        // the neighbouring bit 30.
        Assert.False(db.Creatures.Values
            .Single(c => c.Name == "Dwarf")
            .Flags2.HasFlag(CreatureFlags2.AvailableViaHeroPortal));

        // Disjoint in both directions, and by room type rather than by owner —
        // so this holds whoever happens to hold the room.
        foreach (CreatureDef creature in db.Creatures.Values)
        {
            bool fromPortal = PortalRules.CanComeFromPortal(creature, portal);
            bool fromHeroPortal = PortalRules.CanComeFromPortal(creature, heroPortal);

            Assert.False(
                fromPortal && fromHeroPortal,
                $"{creature.Name} may come out of both portals");
        }

        // Nothing else in the record's last dword is set: four bits in use, and
        // the top three bytes zero on all 46. A fifth bit appearing is a fact
        // about the data that changed and wants reading rather than absorbing.
        foreach (CreatureDef creature in db.Creatures.Values)
        {
            Assert.Equal(0u, (uint)creature.Flags2 & ~0xFu);
        }
    }

    /// <summary>
    /// Frosty's Castle is built around keepers using Hero Portals, and that is
    /// the strongest evidence for <see cref="CreatureFlags2.AvailableViaHeroPortal"/>
    /// — a level designer's intent rather than a coincidence of bits.
    ///
    /// <para>The map's only entrances are <b>four unowned Hero Portals</b>, and
    /// the level pools, for every keeper from 3 to 6, exactly the nine creatures
    /// carrying the flag. <b>The Dwarf (14) is absent</b>, which is the creature
    /// the rejected candidate bit (bit 30 of the first flag dword, our
    /// <c>MpdRandomInvader</c>) would have added.</para>
    ///
    /// <para>Pinned because the whole hero-portal rule leans on it, and because
    /// it is the sort of claim about the shipped data that nothing would
    /// otherwise notice going stale.</para>
    ///
    /// <para><b>CONFIRMED IN PLAY (owner, 2026-09-07, Q211):</b> claiming one on
    /// this map really does give a keeper the mercenary roster, and <b>no Dwarf
    /// arrives</b> — so this test's two assertions are now backed by the running
    /// game and not only by the file. The room is called <b>"Mercenary
    /// Portal"</b> in game; <i>"Hero Portal"</i> is the editor's label in
    /// <c>Rooms.kwd</c>, which is what these identifiers are named from.</para>
    /// </summary>
    [InstallFact]
    public void FrostysCastleHasOnlyHeroPortalsAndPoolsExactlyTheirNineCreatures()
    {
        string install = Install();

        const string Level = "Frosty's Castle";

        GameDatabase db = GameDatabaseLoader.Load(install);
        byte portalTerrain = db.Room(RoomTypeId.Portal).TerrainId;
        byte heroTerrain = db.Room(RoomTypeId.HeroPortal).TerrainId;

        LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, Level);

        Assert.NotNull(map);
        Assert.NotNull(map);

        int heroTiles = 0;
        int keeperPortalTiles = 0;
        foreach (MapTile tile in map!.Tiles)
        {
            if (tile.TerrainId == heroTerrain) heroTiles++;
            if (tile.TerrainId == portalTerrain) keeperPortalTiles++;
        }

        // Four 3x3 Hero Portals and not one Portal, so a keeper here has no
        // other way to obtain a creature.
        Assert.Equal(36, heroTiles);
        Assert.Equal(0, keeperPortalTiles);

        GlobalVariables? vars = GameDatabaseLoader.LoadCampaignVariables(install, Level);
        Assert.NotNull(vars);

        // Every keeper's pool, and they are all the same nine.
        foreach (byte keeper in new byte[] { 3, 4, 5, 6 })
        {
            var pooled = new SortedSet<string>();
            foreach (CreaturePoolEntry entry in vars!.CreaturePool)
            {
                if (entry.PlayerId != keeper) continue;
                pooled.Add(db.CreatureById((byte)entry.CreatureId).Name);
            }

            Assert.Equal(
                new SortedSet<string>
                {
                    "Elven Archer", "Fairy", "Giant", "Guard", "Knight",
                    "Monk", "Royal Guard", "Thief", "Wizard",
                },
                pooled);

            // The same nine the flag selects, and the Dwarf in neither.
            foreach (string name in pooled)
            {
                CreatureDef creature = db.Creatures.Values.Single(c => c.Name == name);
                Assert.True(
                    creature.Flags2.HasFlag(CreatureFlags2.AvailableViaHeroPortal),
                    $"{name} is pooled on {Level} but carries no hero portal flag");
            }

            Assert.DoesNotContain("Dwarf", pooled);
        }
    }

    [InstallFact]
    public void NoShippedLevelEverPoolsOrUnlocksAUniqueCreature()
    {
        string install = Install();

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

    [InstallFact]
    public void TheThirteenUniqueCreaturesAllCarryThePortalFlag()
    {
        string install = Install();

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

    /// <summary>
    /// Both portal types raise the creature cap, on the shipped data.
    ///
    /// <para><b>CONFIRMED from the binary, 2026-09-07.</b> DK2's capacity
    /// routine walks the player's own room list twice — once counting room type
    /// 3 (Portal), once type <c>0x19</c> (Hero Portal) — and adds the two before
    /// applying the per-additional rate. This project counted only type 3, so a
    /// keeper holding a Hero Portal received its arrivals but not its five extra
    /// places.</para>
    ///
    /// <para>Both are the only two <c>ThreeByThree</c> rooms in the game, and
    /// their terrains are 12 and 40.</para>
    /// </summary>
    [InstallFact]
    public void AHeroPortalCountsTowardTheCapJustLikeAPortal()
    {
        string install = Install();

        GameDatabase db = GameDatabaseLoader.Load(install);

        RoomDef portal = db.Room(RoomTypeId.Portal);
        RoomDef heroPortal = db.Room(RoomTypeId.HeroPortal);

        Assert.True(PortalRules.IsEntrance(portal));
        Assert.True(PortalRules.IsEntrance(heroPortal));

        // The two terrains the ownership reset tests against.
        Assert.Equal(12, portal.TerrainId);
        Assert.Equal(40, heroPortal.TerrainId);
        Assert.True(PortalRules.IsPortalTerrain(db, portal.TerrainId));
        Assert.True(PortalRules.IsPortalTerrain(db, heroPortal.TerrainId));

        // And no other room shares either terrain, which is what makes a
        // terrain test a safe stand-in for a room lookup on the hot path.
        int entrances = 0;
        foreach (RoomDef room in db.Rooms.Values)
        {
            if (PortalRules.IsPortalTerrain(db, room.TerrainId)) entrances++;
        }
        Assert.Equal(2, entrances);

        // 15 for the first and 5 for each after, counting both types together.
        Assert.Equal(15, db[VariableTypeId.CreaturesSupportedByFirstPortal]);
        Assert.Equal(5, db[VariableTypeId.CreaturesSupportedPerAdditionalPortal]);
        Assert.Equal(0, PortalRules.CreatureCapacity(db, 0));
        Assert.Equal(15, PortalRules.CreatureCapacity(db, 1));
        Assert.Equal(20, PortalRules.CreatureCapacity(db, 2));
    }
}
