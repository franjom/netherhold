// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The possession commands that set the creature's inputs (possession-arms-and-inputs.md §5):
/// sneak, hypnosis and sniper mode, and the readers each changes.</summary>
public sealed class PossessionInputTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;
    private const ushort East = 0x200, Level = 0x200;
    private const byte MesmerCreatureId = 50;

    /// <summary>A Goblin that can be hypnotised, and one immune to Turncoat as well.</summary>
    private static CreatureDef Mesmer() => Dk2TerrainFixture.Goblin() with
    {
        Name = "Mesmer", CreatureId = MesmerCreatureId,
        Flags = Dk2TerrainFixture.Goblin().Flags | CreatureFlags.CanBeHypnotized,
    };

    private static CreatureDef Stubborn() => Mesmer() with
    {
        Name = "Stubborn", CreatureId = MesmerCreatureId + 1,
        Flags = Mesmer().Flags | CreatureFlags.IsImmuneToTurncoat,
    };

    private static (GameDatabase Db, WorldState World) Open()
    {
        GameDatabase db = Dk2TerrainFixture.Database(Dk2TerrainFixture.Possessor(), Mesmer(), Stubborn());
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(30, 20),
            [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
        return (db, world);
    }

    private static CreatureState Possess(GameDatabase db, WorldState world, int x, int y, byte type = Dk2TerrainFixture.GoblinCreatureId)
    {
        CreatureState me = world.SpawnCreature(db, type, Keeper, new TilePos(x, y));
        me.Possessed = true;
        me.Heading = East;
        world.Player(Keeper).PossessedCreatureId = me.Id;
        return me;
    }

    private static GameSimulation Sim(GameDatabase db, WorldState world, params ISimulationSystem[] more)
    {
        var sim = new GameSimulation(db, world, 1, [new PossessionSystem(), .. more]);
        sim.Tick();
        return sim;
    }

    private static PossessionAim Aim(CreatureState me) => new(new(new(me.PosX), new(me.PosY), new(me.PosZ)), new(East), new(Level));

    private static void Own(WorldState world, int x, int y, byte owner) => world.Tile(x, y).OwnerPlayerId = owner;

    // ------------------------------------------------------------------ sneak

    [Fact]
    public void SneakDisguisesAsTheTilesOwnerAndASecondPressEndsIt_ConfirmedFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, 5, 10);
        Own(world, 20, 15, Rival);
        GameSimulation sim = Sim(db, world);

        sim.Tick(InputFrame.Of(new ToggleSneakCommand(Keeper, 20, 15)));
        Assert.Equal(Rival, me.SneakingAs);

        sim.Tick(InputFrame.Of(new ToggleSneakCommand(Keeper, 20, 15)));
        Assert.Equal(0, me.SneakingAs);
    }

    [Fact]
    public void SneakIsRefusedOnItsOwnOrANeutralTile_ConfirmedFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, 5, 10);
        Own(world, 20, 15, Keeper);
        GameSimulation sim = Sim(db, world);

        sim.Tick(InputFrame.Of(new ToggleSneakCommand(Keeper, 20, 15)));
        Assert.Equal(0, me.SneakingAs);
        sim.Tick(InputFrame.Of(new ToggleSneakCommand(Keeper, 21, 15)));
        Assert.Equal(0, me.SneakingAs);
    }

    [Fact]
    public void ARivalWatcherInSightStopsTheDisguise_ConfirmedFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, 5, 10);
        world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(7, 10));
        Own(world, 20, 15, Rival);
        GameSimulation sim = Sim(db, world);

        sim.Tick(InputFrame.Of(new ToggleSneakCommand(Keeper, 20, 15)));

        Assert.Equal(0, me.SneakingAs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheDisguisedAsPlayersCreaturesDoNotPickTheSneaker_ConfirmedFromBinary(bool disguised)
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, 5, 10);
        CreatureState rival = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(7, 10));
        if (disguised) me.SneakingAs = Rival;
        Assert.Equal(disguised, PossessionRules.IsDisguisedFrom(rival, me));

        new GameSimulation(db, world, 1, [new CombatSystem()]).Run(1);

        Assert.Equal(!disguised, rival.FightTargetId == me.Id);
    }

    [Fact]
    public void StrikingTheDisguisedAsPlayersCreatureEndsTheDisguise_ConfirmedFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, 10, 10);
        world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(11, 10));
        GameSimulation sim = Sim(db, world, new CombatSystem());
        me.SneakingAs = Rival;

        sim.Tick(InputFrame.Of(new ActPossessedCreatureCommand(Keeper, Aim(me), true, new(0))));

        Assert.Equal(0, me.SneakingAs);
    }

    [Fact]
    public void AnotherKeepersHandUnmasksTheSneakerAndLetsGo_ConfirmedFromBinary()
    {
        var (db, world) = Open();
        CreatureState sneaker = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(10, 10));
        sneaker.SneakingAs = Rival;
        Assert.True(HandRules.HasPickupPermission(world, db, sneaker, Rival));
        var sim = new GameSimulation(db, world, 1, [new HandSystem()]);

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Rival, sneaker.Id)));

        Assert.Equal(0, sneaker.SneakingAs);
        Assert.False(sneaker.InHand);
    }

    [Fact]
    public void LeavingPossessionEndsTheDisguiseAndSniperMode_ConfirmedFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, 5, 10);
        GameSimulation sim = Sim(db, world);
        me.SneakingAs = Rival;
        me.SniperMode = true;

        sim.Tick(InputFrame.Of(new LeavePossessionCommand(Keeper)));

        Assert.Equal(0, me.SneakingAs);
        Assert.False(me.SniperMode);
    }

    // ------------------------------------------------------------------ hypnosis

    [Fact]
    public void HypnosisTakesTheNearestEnemyInTheConeAndASecondPressReleasesIt_ConfirmedFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, 5, 10);
        CreatureState far = world.SpawnCreature(db, MesmerCreatureId, Rival, new TilePos(12, 10));
        CreatureState near = world.SpawnCreature(db, MesmerCreatureId, Rival, new TilePos(8, 10));
        CreatureState behind = world.SpawnCreature(db, MesmerCreatureId, Rival, new TilePos(4, 10));
        GameSimulation sim = Sim(db, world, new CreatureJobSystem());

        sim.Tick(InputFrame.Of(new HypnotiseCommand(Keeper, Aim(me))));

        Assert.Equal(near.Id, me.HypnotisedCreatureId);
        Assert.Equal(Keeper, near.OwnerPlayerId);
        Assert.Equal(Rival, near.HypnosisOwnerPlayerId);
        Assert.True(near.Hypnotised);
        Assert.Equal(me.Id, near.FollowLeaderId);
        Assert.All([far, behind], c => Assert.Equal(Rival, c.OwnerPlayerId));

        sim.Tick(InputFrame.Of(new HypnotiseCommand(Keeper, Aim(me))));

        Assert.Equal(0, me.HypnotisedCreatureId);
        Assert.Equal(Rival, near.OwnerPlayerId);
        Assert.False(near.Hypnotised);
    }

    [Fact]
    public void ACreatureThatCannotBeHypnotisedOrIsImmuneToTurncoatIsNotTaken_ConfirmedFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, 5, 10);
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(7, 10));
        CreatureState stubborn = world.SpawnCreature(db, (byte)(MesmerCreatureId + 1), Rival, new TilePos(9, 10));
        GameSimulation sim = Sim(db, world);

        sim.Tick(InputFrame.Of(new HypnotiseCommand(Keeper, Aim(me))));

        Assert.Equal(0, me.HypnotisedCreatureId);
        Assert.Equal(Rival, goblin.OwnerPlayerId);
        Assert.Equal(Rival, stubborn.OwnerPlayerId);
    }

    [Fact]
    public void LeavingPossessionReleasesTheHypnotisedCreature_ConfirmedFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, 5, 10);
        CreatureState held = world.SpawnCreature(db, MesmerCreatureId, Rival, new TilePos(8, 10));
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(new HypnotiseCommand(Keeper, Aim(me))));
        Assert.Equal(Keeper, held.OwnerPlayerId);

        sim.Tick(InputFrame.Of(new LeavePossessionCommand(Keeper)));

        Assert.Equal(Rival, held.OwnerPlayerId);
        Assert.False(held.Hypnotised);
        Assert.Equal(0, me.HypnotisedCreatureId);
    }

    [Fact]
    public void APossessedBlowCommandsTheHypnotisedCreatureUntilItHasAttackedAsOften_ConfirmedFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, 10, 10);
        CreatureState enemy = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(11, 10));
        CreatureState held = world.SpawnCreature(db, MesmerCreatureId, Keeper, new TilePos(8, 10));
        held.Hypnotised = true;
        held.HypnosisOwnerPlayerId = Rival;
        me.HypnotisedCreatureId = held.Id;
        GameSimulation sim = Sim(db, world);

        sim.Tick(InputFrame.Of(new ActPossessedCreatureCommand(Keeper, Aim(me), true, new(0))));

        Assert.True(held.AttackAbnormalTargets);
        Assert.Equal(enemy.Id, held.AttackingId);
        Assert.Equal(1, held.HypnotisedAttacks);

        held.AttackCreatureHits = 1;
        sim.Tick();
        Assert.Equal(CombatRules.NoTarget, held.AttackingId);
        Assert.Equal(0, held.HypnotisedAttacks);
        Assert.True(held.Hypnotised);
    }

    [Fact]
    public void AHypnotisedCreatureThatIsRemovedClearsItsPossessorsHold_ConfirmedFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, 10, 10);
        CreatureState held = world.SpawnCreature(db, MesmerCreatureId, Keeper, new TilePos(8, 10));
        held.Hypnotised = true;
        me.HypnotisedCreatureId = held.Id;

        Assert.True(world.RemoveCreature(held.Id));

        Assert.Equal(0, me.HypnotisedCreatureId);
    }

    // ------------------------------------------------------------------ sniper

    [Fact]
    public void SniperModeCapsARangeZeroSpellAtThreeTilesAndLeavesARangedOne_ConfirmedFromBinary()
    {
        CreatureSpellDef ranged = Dk2TerrainFixture.CreatureSpells()[0];
        CreatureSpellDef unranged = ranged with { Range = 0 };
        var me = new CreatureState();

        Assert.Equal(PossessionRules.FreeAimRange, PossessionRules.FreeAimRangeOf(ranged, me));
        Assert.Equal(0, PossessionRules.FreeAimRangeOf(unranged, me));
        me.SniperMode = true;
        Assert.Equal(PossessionRules.FreeAimRange, PossessionRules.FreeAimRangeOf(unranged, me));
    }

    [Fact]
    public void ASniperFreeAimShotDoublesItsContactDamage_ConfirmedFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, 10, 10, Dk2TerrainFixture.WarlockCreatureId);
        GameSimulation sim = Sim(db, world, new CombatSystem());
        sim.Tick(InputFrame.Of(new SetSniperModeCommand(Keeper, true)));
        Assert.True(me.SniperMode);

        sim.Tick(InputFrame.Of(new ActPossessedCreatureCommand(Keeper, Aim(me), false, new(0))));

        ShotState shot = Assert.Single(world.Shots);
        Assert.True(shot.Sniper);
        Assert.Equal(shot.Data1 * 2, ShotRules.ContactAmount(shot));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScanCSkipsARegisteredAttackerDisguisedAsTheSearchersSide_ConfirmedFromBinary(bool disguised)
    {
        var (db, world) = Open();
        CreatureState sneaker = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 10));
        CreatureState searcher = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(6, 10));
        Assert.True(CombatRules.Register(searcher, sneaker.Id, ranged: false));
        if (disguised) sneaker.SneakingAs = Rival;

        new GameSimulation(db, world, 1, [new CombatSystem()]).Run(1);

        Assert.Equal(!disguised, searcher.FightTargetId == sneaker.Id);
    }

    [Fact]
    public void CastingAtTheDisguisedAsPlayersCreatureEndsTheDisguise_ConfirmedFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, 5, 10, Dk2TerrainFixture.WarlockCreatureId);
        world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(8, 10));
        GameSimulation sim = Sim(db, world, new CombatSystem());
        me.SneakingAs = Rival;

        sim.Tick(InputFrame.Of(new ActPossessedCreatureCommand(Keeper, Aim(me), false, new(0))));

        Assert.Equal(0, me.SneakingAs);
    }

    [Fact]
    public void AFreedHypnotisedCreatureClearsTheHoldOnlyOnce_ConfirmedFromBinary()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, 10, 10);
        CreatureState first = world.SpawnCreature(db, MesmerCreatureId, Keeper, new TilePos(8, 10));
        CreatureState second = world.SpawnCreature(db, MesmerCreatureId, Keeper, new TilePos(7, 10));
        first.Hypnotised = true;
        me.HypnotisedCreatureId = first.Id;

        PossessionRules.CreatureFreed(world, first);   // its death
        Assert.Equal(0, me.HypnotisedCreatureId);
        second.Hypnotised = true;
        me.HypnotisedCreatureId = second.Id;
        Assert.True(world.RemoveCreature(first.Id));   // its corpse, later

        Assert.Equal(second.Id, me.HypnotisedCreatureId);
    }

    [Fact]
    public void EveryAttackLaunchedInAFightIsCounted_ConfirmedFromBinary()
    {
        var (db, world) = Open();
        CreatureState a = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 10));
        CreatureState b = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(5, 10));
        b.PosX = a.PosX + 1000;   // touching, inside melee reach

        new GameSimulation(db, world, 1, [new CombatSystem(), new CreatureHealthSystem()]).Run(40);

        Assert.True(a.AttackCreatureHits > 0);
    }

    [Fact]
    public void InputFieldsSurviveASnapshot()
    {
        var (db, world) = Open();
        CreatureState me = Possess(db, world, 3, 10);
        me.SneakingAs = 4;
        me.SniperMode = true;
        me.HypnotisedCreatureId = 9;
        me.HypnosisOwnerPlayerId = 5;
        me.Hypnotised = true;
        me.AttackCreatureHits = 6;
        me.HypnotisedAttacks = 7;

        var sim = new GameSimulation(db, world, 1, []);
        GameSimulation copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, []);
        CreatureState back = copy.World.CreatureById(me.Id)!;

        Assert.Equal(sim.StateHash(), copy.StateHash());
        Assert.Equal((4, true, 9, 5, true, 6, 7),
            (back.SneakingAs, back.SniperMode, back.HypnotisedCreatureId, back.HypnosisOwnerPlayerId, back.Hypnotised,
             back.AttackCreatureHits, back.HypnotisedAttacks));
    }
}
