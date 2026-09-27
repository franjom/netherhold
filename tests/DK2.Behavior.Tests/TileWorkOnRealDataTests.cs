// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

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
/// Gated with [InstallFact], so it reports SKIPPED without an installation rather than passing
/// without proprietary assets.
/// </summary>
public class TileWorkOnRealDataTests
{
    private static GameDatabase RealDatabase() =>
        GameDatabaseLoader.Load(Dk2InstallGate.Path);

    /// <summary>A tile of terrain <paramref name="id"/> at its shipped starting health.</summary>
    private static TileState Tile(GameDatabase db, byte id, byte owner = 0) => new()
    {
        TerrainId = id,
        OwnerPlayerId = owner,
        Health = db.TerrainById(id).StartingHealth,
    };

    private static byte IdOf(GameDatabase db, string name) =>
        db.Terrain.Values.Single(t => t.Name == name).TerrainId;

    [InstallFact]
    public void TheTerrainNamesTheRulesRelyOn_AreAllPresent()
    {
        GameDatabase db = RealDatabase();

        foreach (string name in new[]
                 {
                     "Impenetrable Rock", "Rock", "Dirt Path", "Gold", "Gems",
                     "Claimed Path", "Reinforced Wall",
                 })
        {
            Assert.Single(db.Terrain.Values, t => t.Name == name);
        }
    }

    [InstallFact]
    public void TheFixtureNumbers_MatchTheInstall()
    {
        GameDatabase db = RealDatabase();

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

    [InstallFact]
    public void TheDigClaimReinforceCycle_WorksOnRealData()
    {
        GameDatabase db = RealDatabase();

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

        // Five swings to claim one tile against the real numbers, CONFIRMED by
        // run 15's health trace (ADR-0133) — asserted rather than looped, so that
        // a change to the application rate shows up here too.
        for (int i = 0; i < 4; i++)
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

        // FIFTY-FIVE, on the real shipped data: Rock's 165 up to its maxHealth
        // of 660 is 495 at 9 a swing, and 495 / 9 is exactly 55. This has been
        // wrong twice (66, then 50) and a stopwatch was the evidence both
        // times; run 15 counted the steps in the tile's own health word, so it
        // is CONFIRMED rather than fitted. If it changes again, change it from
        // a health trace and not from seconds (ADR-0133, Q62).
        Assert.Equal(55, swings);
    }

    [InstallFact]
    public void NoTerrainInTheGame_UpgradesItselfOutOfExistenceAtRest()
    {
        GameDatabase db = RealDatabase();

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
            TileWorkRules.Apply(db, ref tile, TileWorkKind.RepairWall, 1);
            Assert.Equal(before, tile.TerrainId);
        }
    }

    [InstallFact]
    public void EveryTerrainTransitionTargetInTheGame_Resolves()
    {
        GameDatabase db = RealDatabase();

        foreach (TerrainDef t in db.Terrain.Values)
        {
            Assert.True(db.Terrain.ContainsKey(t.MaxHealthTypeTerrainId),
                $"{t.Name} upgrades to unknown terrain {t.MaxHealthTypeTerrainId}");
            Assert.True(db.Terrain.ContainsKey(t.DestroyedTypeTerrainId),
                $"{t.Name} is destroyed into unknown terrain {t.DestroyedTypeTerrainId}");
        }
    }

    [InstallFact]
    public void DiggingOutARealCampaignLevel_LeavesOnlyFloorAndImpenetrableRock()
    {
        GameDatabase db = RealDatabase();

        string install = Dk2InstallGate.Path;
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

    /// <summary>
    /// The two indestructible terrains, on the shipped table.
    ///
    /// <para>CONFIRMED (owner, 2026-09-04): <i>"there are 2 categories (that I
    /// know of) of indestructable tiles, that can't be dug through by imps, solid
    /// rock and a gem tile. gem tile is an infinite gold source and solid rock is
    /// the boundary to limit expansion on the map."</i></para>
    ///
    /// <para>The data says it two different ways, which is why this is worth
    /// pinning rather than assuming. Impenetrable Rock is <b>not taggable</b>, so
    /// a keeper cannot even mark it and no rule ever runs. Gems <i>are</i>
    /// taggable and carry a gold value, so mining applies — but their
    /// <see cref="TerrainDef.DestroyedTypeTerrainId"/> is themselves, so the tile
    /// survives every swing. One boundary, one infinite seam, no special
    /// case.</para>
    ///
    /// <para>Also asserted: those are the only two <i>diggable-looking</i>
    /// terrains that behave this way, so "two categories" is the whole list for
    /// anything a keeper meets while digging.</para>
    /// </summary>
    [InstallFact]
    public void ImpenetrableRockIsUnmarkableAndGemsAreInexhaustible()
    {
        GameDatabase db = RealDatabase();

        TerrainDef rock = db.TerrainById(IdOf(db, "Impenetrable Rock"));
        Assert.True(rock.IsSolid);
        Assert.True(rock.IsImpenetrable);
        Assert.False(rock.IsTaggable);

        TerrainDef gems = db.TerrainById(IdOf(db, "Gems"));
        Assert.True(gems.IsSolid);
        Assert.True(gems.IsImpenetrable);
        Assert.True(gems.IsTaggable);
        Assert.True(gems.GoldValue > 0);
        Assert.Equal(gems.TerrainId, gems.DestroyedTypeTerrainId);

        // The contrast that makes it a claim: a plain gold seam is exhaustible.
        TerrainDef gold = db.TerrainById(IdOf(db, "Gold"));
        Assert.True(gold.GoldValue > 0);
        Assert.False(gold.IsImpenetrable);
        Assert.NotEqual(gold.TerrainId, gold.DestroyedTypeTerrainId);

        // And no third case hides among the terrain a keeper can mark: every
        // taggable, impenetrable terrain is either Gems or one of the engine's
        // internal markers, which no map places as ground.
        string[] internalMarkers = ["Fog Of War", "Claim Scan"];
        foreach (TerrainDef t in db.Terrain.Values.Where(t => t.IsTaggable && t.IsImpenetrable))
            Assert.True(
                t.Name == "Gems" || internalMarkers.Contains(t.Name),
                $"{t.Name} is taggable and impenetrable and is neither Gems nor an internal marker");
    }

    /// <summary>
    /// <b>A level-1 keeper is not walled in by their own wall.</b>
    ///
    /// <para>The real-data form of Q95 / ADR-0045, and the reason that defect
    /// mattered rather than merely being untidy. Level 1's dungeon heart is
    /// enclosed on every side, and the only tile between it and the rest of the
    /// level is <b>the keeper's own reinforced wall</b> at (18,28). Dig it and
    /// the owner byte used to survive onto the Dirt Path underneath — which the
    /// keeper could then neither claim (the owner already matched) nor treat as
    /// their own land (the terrain is not ownable). Their territory could not
    /// grow through it, so nothing beyond the heart was ever reachable: not the
    /// portal, not a lair, not a treasury.</para>
    ///
    /// <para>Asserted on the shipped map rather than a fixture, because the thing
    /// that made it fatal is a fact about level 1's layout and not about the
    /// rule.</para>
    /// </summary>
    [InstallFact]
    public void ALevelOneKeeperCanClaimTheGroundUnderTheirOwnWall()
    {
        string install = Dk2InstallGate.Path;

        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(install, "Level1");
        LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, "Level1");
        Assert.NotNull(map);

        var world = WorldState.FromMap(db, map, [(byte)0, (byte)3]);
        var wall = new TilePos(18, 28);

        // The premise: it really is the keeper's own reinforced wall, and it
        // really is solid. If the map ever changes, this fails here rather than
        // silently testing an ordinary rock.
        ref TileState tile = ref world.Tile(wall);
        Assert.Equal((byte)3, tile.OwnerPlayerId);
        Assert.True(db.TerrainById(tile.TerrainId).IsSolid);
        Assert.True(db.TerrainById(tile.TerrainId).IsOwnable);
        Assert.True(TileWorkRules.Applies(db, tile, TileWorkKind.DigOwnWall, 3));

        // Dig it down, then dig the rock it leaves.
        for (int i = 0; i < 5000 && db.TerrainById(world.Tile(wall).TerrainId).IsSolid; i++)
        {
            ref TileState t = ref world.Tile(wall);
            TileWorkKind kind = TileWorkRules.Applies(db, t, TileWorkKind.DigOwnWall, 3)
                ? TileWorkKind.DigOwnWall
                : TileWorkKind.Dig;
            TileWorkRules.Apply(db, ref t, kind, 3);
        }

        Assert.False(db.TerrainById(world.Tile(wall).TerrainId).IsSolid);

        // And the whole point: the keeper can take the ground they opened.
        Assert.Equal(PlayerIds.Neutral, world.Tile(wall).OwnerPlayerId);
        Assert.True(TileWorkRules.Applies(db, world.Tile(wall), TileWorkKind.Claim, 3));
    }
}
