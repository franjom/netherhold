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
    [InstallFact]
    public void ManaPressureUsesTheShippedWarningIntervalAndImpUpkeep()
    {
        GameDatabase db = RealDatabase();
        Assert.Equal(10, db[VariableTypeId.InsufficientManaImpEvaluationPeriodSeconds]);
        Assert.Equal(100, db[VariableTypeId.LowManaWarningThreshold]);
        Assert.Equal(7, db.RequireKeeperSpell(KeeperSpellName.CreateImp).ManaUpkeepPerSecond);
        var result = ManaPressureRules.Advance(default, 4, 0, -7, 5,
            (uint)db[VariableTypeId.MinimumImpThreshold],
            (ushort)db[VariableTypeId.LowManaWarningThreshold],
            (byte)db[VariableTypeId.InsufficientManaImpEvaluationPeriodSeconds], 4);
        Assert.Equal(44u, result.State.WarningDeadline);
    }

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
        Assert.Equal(573, real.FootprintRadius.Raw);
        Assert.Equal(1, real.SizeClass);
        Assert.Equal(4096, real.TurnRate);

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

    /// <summary>
    /// <b>A gate is never converted; every other rival room is.</b> DK2's enemy-room test
    /// <c>0x00454040</c> takes a room whose terrain is Attackable, and one that is not only once
    /// ruined (<c>+0x9A</c> bit 0), which only COLLAPSE_HERO_GATE does and we do not build. In
    /// Level 1's capture no imp ever targets the heroes' 2x2 gate (ADR-0299).
    /// </summary>
    [InstallFact]
    public void ARoomWhoseTerrainIsNotAttackable_IsNeverConverted()
    {
        GameDatabase db = RealDatabase();
        const byte rival = 1;
        const byte keeper = 3;

        var gates = new List<string>();
        foreach (TerrainDef terrain in db.Terrain.Values.Where(t => t.IsRoom && !t.IsSolid))
        {
            var tile = new TileState { TerrainId = terrain.TerrainId, OwnerPlayerId = rival, Health = terrain.StartingHealth };
            bool converts = TileWorkRules.Applies(db, tile, TileWorkKind.ConvertRoom, keeper);
            if (!terrain.IsAttackable)
            {
                Assert.False(converts, $"{terrain.Name} is not Attackable and was converted");
                gates.Add(terrain.Name);
            }
            else if (terrain.TerrainId != ManaRules.DungeonHeartTerrainId(db))
            {
                Assert.True(converts, $"{terrain.Name} is Attackable and was not converted");
            }
        }

        // The five gates of the shipped data, so a reading that matched none would fail here.
        Assert.Equal(5, gates.Count);
        Assert.Contains(gates, n => n.Contains("2x2"));
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
        // The first keeper's imp: level 1's first such edge is on the heroes' land, and a hero's imp
        // runs its standing job rather than the imp tasks (ADR-0216).
        const byte keeper = PlayerIds.FirstKeeper;
        if (!world.HasPlayer(keeper)) return;
        (TilePos stand, TilePos rock) = FindDiggableEdge(db, world, keeper)
            ?? throw new InvalidOperationException("level1 has no floor tile adjacent to diggable rock.");

        CreatureState imp = world.SpawnCreature(
            db, db.CreatureByName("Imp")!.CreatureId, keeper, stand);

        var sim = new GameSimulation(db, world, 7, SimulationSystems.Default());
        sim.Tick(InputFrame.Of(TagTilesCommand.Tag(keeper, rock)));
        // Rock goes in one application (Q20), three ticks in at DK2's cadence (ADR-0280); the rest is the walk.
        int ticks = WorkCadence.FirstApplicationTicks + WorkCadence.SecondApplicationGap(TileWorkKind.Dig)
            + SimulationTiming.TicksFromSeconds(1);
        sim.Run(ticks);

        Assert.False(db.TerrainById(world.Tile(rock).TerrainId).IsSolid,
            $"{rock} is still solid after {ticks} ticks");

        // This test used to also require more work afterwards. That held only while the imp was
        // the heroes' own, on the heroes' land. No floor of level 1's keepers borders diggable
        // rock, so the keeper's imp stands on land that is not its keeper's, and a worker with
        // nowhere of its keeper's to stand runs out of jobs
        // (ImpTests.AnImpWithNothingToDo_StaysWhereItIs).
        Assert.Equal(keeper, imp.OwnerPlayerId);
    }

    private static (TilePos Stand, TilePos Rock)? FindDiggableEdge(GameDatabase db, WorldState world, byte keeper)
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

                    if (TileWorkRules.Applies(db, candidate, TileWorkKind.Dig, keeper)
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
