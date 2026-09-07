// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>
/// Workers against the real game's data rather than a transcription of it.
///
/// Two jobs. The first is a drift guard: the Imp definition in
/// <c>DK2.Simulation.Tests.Dk2TerrainFixture</c> exists so that worker behaviour
/// can be tested with no installation present, and a transcription that quietly
/// stopped matching the game would make every one of those tests worthless. The
/// second is to run the real thing — imps digging a real campaign level — where
/// assumptions the synthetic map happens to satisfy get to fail.
///
/// Gated with [InstallFact], so it reports SKIPPED without an installation rather than passing
/// without proprietary assets.
/// </summary>
public class ImpsOnRealDataTests
{
    private static GameDatabase RealDatabase() =>
        GameDatabaseLoader.Load(Dk2InstallGate.Path);

    [InstallFact]
    public void EveryShippedCreatureHasSixTilePerceptionRange()
    {
        GameDatabase db = RealDatabase();

        Assert.Equal(46, db.Creatures.Count);
        Assert.All(db.Creatures.Values,
            creature => Assert.Equal(0x6000, creature.PerceptionRange.Raw));
    }

    [InstallFact]
    public void TheImpFixture_MatchesTheInstall()
    {
        GameDatabase db = RealDatabase();

        CreatureDef real = db.CreatureByName("Imp")
            ?? throw new InvalidOperationException("The install has no creature named Imp.");

        Assert.Equal(1, real.CreatureId);
        Assert.Equal(0xB000089Fu, (uint)real.Flags);

        Assert.Equal(7536, real.Speed.Raw);
        Assert.Equal(7536, real.RunSpeed.Raw);
        Assert.Equal(4096, real.ShuffleSpeed.Raw);
        Assert.Equal(2048, real.Height.Raw);
        Assert.Equal(16384, real.DistanceCanSee.Raw);
        Assert.Equal(0x6000, real.PerceptionRange.Raw);
        Assert.Equal(16384, real.DistanceCanHear.Raw);

        Assert.Equal(500, real.Hp);
        Assert.Equal(134213632, real.HungerRate.Raw); // 32767.0, the "never" sentinel
        Assert.Equal(32767, real.TimeAwake);
        Assert.Equal(0, real.TimeSleep);

        Assert.Equal(30, real.MeleeDamage);
        Assert.Equal(1433, real.MeleeRange.Raw);
        Assert.Equal(4915, real.MeleeRecharge.Raw);
        Assert.Equal(AttackType.MeleeBlade, real.MeleeAttackType);

        // How much an imp can carry, which is what decides how often it breaks
        // off mining to run to a treasury (Q31). InitialGoldHeld is transcribed
        // because it sits beside it in the record and is worth not losing;
        // nothing reads it, and what it means is UNKNOWN.
        Assert.Equal(1400, real.MaxGoldHeld);
        Assert.Equal(500, real.InitialGoldHeld);

        Assert.Equal(0, real.Pay);
        Assert.Equal(5, real.Fear);
        Assert.Equal(2, real.Threat);
        Assert.Equal(3000, real.ExpForNextLevel);
        Assert.Equal(1200, real.ExpPerSecond);
        Assert.Equal(30, real.ExpPerSecondTraining);
        Assert.Equal(50, real.UnhappyThreshold);
        Assert.Equal(JobClass.Scout, real.JobClass);
        Assert.Equal(FightStyle.NonFighter, real.FightStyle);
    }

    [InstallFact]
    public void TheWorkerFlag_FallsOnExactlyTheImpAndTheDwarf()
    {
        GameDatabase db = RealDatabase();

        // ImpTaskSystem keys on the flag rather than on creature id 1, for the
        // same reason the tile rules never name terrain id 2. This is what makes
        // that keying meaningful: the flag picks out precisely the two creatures
        // the game treats as workers, and the hero Dwarf is one of them.
        string[] workers = db.Creatures.Values
            .Where(c => c.Flags.HasFlag(CreatureFlags.IsWorker))
            .Select(c => c.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Dwarf", "Imp"], workers);
    }

    [InstallFact]
    public void TheImpsOwnBalanceVariables_AreAllInTheGlobalTable()
    {
        GameDatabase db = RealDatabase();

        // Only the first is read today. The rest are named here because they are
        // what the unimplemented half of imp behaviour will be built from, and
        // because a missing one should surface as a failing test rather than as
        // a KeyNotFoundException in the middle of a run.
        Assert.Equal(5, db[VariableTypeId.ImpIdleDelayBeforeReevaluationSeconds]);
        Assert.Equal(4, db[VariableTypeId.MinimumImpThreshold]);
        Assert.Equal(5, db[VariableTypeId.TimeBeforeFreeImpGeneratedSeconds]);
        Assert.Equal(10, db[VariableTypeId.ImpPopCountdownTimeSeconds]);
        Assert.Equal(20, db[VariableTypeId.ImpExperienceGainPerSecond]);
    }

    [InstallFact]
    public void NoTerrainInTheGame_AdmitsTwoKindsOfMarkedWork()
    {
        GameDatabase db = RealDatabase();

        foreach (TerrainDef terrain in db.Terrain.Values)
        {
            foreach (byte owner in new byte[] { 0, 1, 5 })
            {
                var tile = new TileState
                {
                    TerrainId = terrain.TerrainId,
                    OwnerPlayerId = owner,
                    Health = terrain.StartingHealth,
                };

                int applicable = TileWorkRules.TaggedWork
                    .Count(k => TileWorkRules.Applies(db, tile, k, 1));

                Assert.True(applicable <= 1,
                    $"{terrain.Name} (owner {owner}) admits {applicable} kinds of marked work");
            }
        }
    }

    [InstallFact]
    public void ImpsDigOutMarkedRock_OnARealCampaignLevel()
    {
        string install = Dk2InstallGate.Path;

        GameDatabase db = GameDatabaseLoader.Load(install);
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(install, "level1")!;
        WorldState world = WorldState.FromMap(
            db, map, map.Tiles.Select(t => t.PlayerId).Distinct());

        // Find somewhere an imp could actually stand and work: a floor tile with
        // diggable rock beside it. Taken from the level rather than chosen, so
        // this test is about the shipped map and not about a layout we invented.
        (TilePos stand, TilePos rock) = FindDiggableEdge(db, world)
            ?? throw new InvalidOperationException("level1 has no floor tile adjacent to diggable rock.");

        byte keeper = world.Tile(stand).OwnerPlayerId;
        if (!world.HasPlayer(keeper)) return;

        CreatureState imp = world.SpawnCreature(
            db, db.CreatureByName("Imp")!.CreatureId, keeper, stand);

        var sim = new GameSimulation(db, world, 7, SimulationSystems.Default());
        sim.Tick(InputFrame.Of(TagTilesCommand.Tag(keeper, rock)));
        sim.Run(SimulationTiming.WorkApplicationPeriodTicks * 4);

        Assert.False(db.TerrainById(world.Tile(rock).TerrainId).IsSolid,
            $"{rock} is still solid after four work periods");

        // Not idle afterwards: on a real level there is always more to do, and
        // an imp that has finished what was marked goes on to claim and
        // reinforce. Only a worker with nowhere of its keeper's to stand runs
        // out of jobs — see ImpTests.AnImpWithNothingToDo_StaysWhereItIs.
        Assert.NotEqual(CreatureActivity.Idle, imp.Activity);
    }

    private static (TilePos Stand, TilePos Rock)? FindDiggableEdge(GameDatabase db, WorldState world)
    {
        for (int y = 1; y < world.Height - 1; y++)
        {
            for (int x = 1; x < world.Width - 1; x++)
            {
                if (db.TerrainById(world.Tile(x, y).TerrainId).IsSolid) continue;

                foreach ((int dx, int dy) in new[] { (0, -1), (-1, 0), (1, 0), (0, 1) })
                {
                    var beside = new TilePos(x + dx, y + dy);
                    ref TileState candidate = ref world.Tile(beside);

                    if (TileWorkRules.Applies(db, candidate, TileWorkKind.Dig, world.Tile(x, y).OwnerPlayerId)
                        && db.TerrainById(candidate.TerrainId).IsTaggable)
                    {
                        return (new TilePos(x, y), beside);
                    }
                }
            }
        }

        return null;
    }
}
