// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;
using static DK2.Simulation.Tests.SoundFixture;

namespace DK2.Simulation.Tests;

/// <summary>The shot's own sounds of docs/research/sound-effects-combat.md section 3, all in the shot definition's category
/// (<c>+0x72</c>): <c>Shot_InitialiseFromDefinition</c> (<c>0x004AAF1E</c>, <c>0x004AAF40</c>) asks <c>0xEF</c> and <c>0xF2</c>,
/// <c>Shot_Update</c> <c>0x1BC</c> when the world stops it (<c>0x004AB49B</c>) and <c>0xF4</c> at its lifetime's death
/// (<c>0x004AB509</c>), <c>Shot_Teardown</c> <c>0xF4</c> again with the second handle (<c>0x004AAF9F</c>),
/// <c>Shot_ProcessSightOfEvil</c> <c>0xE8</c> (<c>0x004AF1BF</c>) and <c>Shot_Collide</c> <c>0xDB</c> in the struck creature's category
/// (<c>0x004AC099</c>).</summary>
public sealed class ShotSoundTests
{
    private const byte Owner = 3;
    private const string Category = "SPELL_TEST";

    private static (GameSimulation Sim, ShotState Shot, ListEventSink Events) Setup(
        uint flags = 4, string category = Category, byte processType = 0, uint processFlags = 0, ushort health = 4, int collideType = 0)
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        ShotDef definition = basis.Shots[Dk2TerrainFixture.FireballShotId] with
        {
            Flags = flags, Health = health, Speed = 4096, CollideType = (byte)collideType, DamageType = 0, ProcessType = processType,
            ShotProcessFlags = processFlags, AirFriction = 0, GeneralEffectId = 0, CreationEffectId = 0, TimedEffectId = 0,
            DeathEffectId = 0, HitSolidEffectId = 0, HitThingEffectId = 0, SoundCategory = category,
        };
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values, basis.Creatures.Values,
            basis.Variables, shots: [definition]);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 12), [(byte)0, (byte)1, (byte)2, (byte)3, (byte)4]);
        for (int y = 1; y < 11; y++)
            for (int x = 1; x < 19; x++) world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
        ShotState shot = ShotRules.Spawn(world, definition, Owner, CreatureState.CentreOf(4), CreatureState.CentreOf(4), 0, 0,
            0, FightTargetKind.None, -1, 0, -1, CreatureState.CentreOf(15), CreatureState.CentreOf(4));
        ShotRules.Launch(shot, definition, shot.TargetX, shot.TargetY);
        var sim = new GameSimulation(db, world, 1, [new Construct(shot), new ShotSystem()]);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        return (sim, shot, events);
    }

    /// <summary>What every caller of <c>Shot_Create</c> does once, as the spell casts and traps do through <see cref="ShotRules.RequestTrail"/>.</summary>
    private sealed class Construct(ShotState shot) : ISimulationSystem
    {
        public string Name => "test_construct";
        public void Execute(SimulationContext context)
        {
            context.Navigation.Refresh();
            if (shot.Age == 0 && context.Tick.Value == 0) ShotRules.RequestTrail(context, shot, new Dk2Angle(256), new Fp32(3000));
        }
    }

    private static ThingSoundRequest[] Heard(ListEventSink events, ThingSoundEvent sound) =>
        Sounds(events).Where(s => s.Event == sound).ToArray();

    private static FpPosition3 At(int x, int y, int z) => new(new Fp32(x), new Fp32(y), new Fp32(z));

    [Fact]
    public void AShotsConstructionAsksEfWithItsFirstHandleAndF2WithItsSecondInItsCategoryAtItsPosition()
    {
        (GameSimulation sim, ShotState shot, ListEventSink events) = Setup();

        sim.Tick();

        ThingSoundRequest made = Assert.Single(Heard(events, ThingSoundEvent.SpellCast));
        Assert.Equal((ushort)0xEF, (ushort)made.Event);
        Assert.Equal(Category, made.Category);
        Assert.Equal(new SoundSourceId(SoundSourceKind.Shot, shot.Id), made.Source);
        Assert.Equal(At(CreatureState.CentreOf(4), CreatureState.CentreOf(4), 3000), made.Position);
        Assert.True(made.Voiced);
        ThingSoundRequest second = Assert.Single(Heard(events, ThingSoundEvent.ShotMadeSecond));
        Assert.Equal((ushort)0xF2, (ushort)second.Event);
        Assert.Equal(new SoundSourceId(SoundSourceKind.ShotSecond, shot.Id), second.Source);
        Assert.Equal(made.Position, second.Position);
    }

    [Fact]
    public void AShotWhoseCategoryIsEmptyAsksNothingAtAllFromBirthToTeardown()
    {
        (GameSimulation sim, _, ListEventSink events) = Setup(category: "");

        sim.Run(8);

        Assert.Empty(Sounds(events));
    }

    [Fact]
    public void AShotThatLivesOutItsLifetimeAsksF4WithItsFirstHandleAtItsLastPositionAndF4AgainWithItsSecondWhenRemoved()
    {
        (GameSimulation sim, ShotState shot, ListEventSink events) = Setup(health: 2);
        sim.Run(2);

        Assert.Empty(sim.World.Shots);
        ThingSoundRequest[] ended = Heard(events, ThingSoundEvent.ShotEnded);
        Assert.Equal(2, ended.Length);
        Assert.Equal(new SoundSourceId(SoundSourceKind.Shot, shot.Id), ended[0].Source);
        Assert.Equal(new SoundSourceId(SoundSourceKind.ShotSecond, shot.Id), ended[1].Source);
        Assert.All(ended, s =>
        {
            Assert.Equal(Category, s.Category);
            Assert.True(s.Voiced);
            Assert.Equal(shot.PosX, s.Position.X.Raw);
            Assert.Equal(shot.PosY, s.Position.Y.Raw);
        });
    }

    [Fact]
    public void AShotTheWorldStopsAndKillsAsksTheSolidHitAndTheTeardownF4ButNoLifetimeF4()
    {
        (GameSimulation sim, ShotState shot, ListEventSink events) = Setup(flags: 1);
        sim.World.Tile(5, 4).TerrainId = Dk2TerrainFixture.Rock;

        sim.Tick();

        Assert.Empty(sim.World.Shots);
        ThingSoundRequest solid = Assert.Single(Heard(events, ThingSoundEvent.ShotHitSolid));
        Assert.Equal((ushort)0x1BC, (ushort)solid.Event);
        Assert.Equal(new SoundSourceId(SoundSourceKind.Shot, shot.Id), solid.Source);
        Assert.Equal(Category, solid.Category);
        Assert.Equal(shot.PosX, solid.Position.X.Raw);
        ThingSoundRequest ended = Assert.Single(Heard(events, ThingSoundEvent.ShotEnded));
        Assert.Equal(SoundSourceKind.ShotSecond, ended.Source.Kind);
    }

    [Fact]
    public void AShotTheWorldStopsWithoutKillingItAsksTheSolidHitOnceAndLivesOn()
    {
        (GameSimulation sim, ShotState shot, ListEventSink events) = Setup(flags: 4, health: 6);
        sim.World.Tile(5, 4).TerrainId = Dk2TerrainFixture.Rock;

        sim.Tick();

        Assert.Single(sim.World.Shots);
        Assert.Single(Heard(events, ThingSoundEvent.ShotHitSolid));
        Assert.Empty(Heard(events, ThingSoundEvent.ShotEnded));
    }

    [Fact]
    public void AStandingShotAsksTheSolidHitOnEveryUpdateAsItsHitSolidEffectIs()
    {
        (GameSimulation sim, ShotState shot, ListEventSink events) = Setup(flags: 4, health: 6);
        shot.VelX = shot.VelY = 0;

        sim.Run(3);

        Assert.Equal(3, Heard(events, ThingSoundEvent.ShotHitSolid).Length);
    }

    [Fact]
    public void ASightOfEvilWithTwentyUpdatesLeftAsksE8OnceForItsOwnersScreenAtItsPosition()
    {
        (GameSimulation sim, ShotState shot, ListEventSink events) = Setup(flags: 4, category: "KEEPER_SPELL_SIGHT_OF_EVIL",
            processType: 25, processFlags: 0x42, health: 64);
        shot.VelX = shot.VelY = 0;
        shot.Data1 = 0;   // no region to post: only the sound is looked at

        sim.Run(60);

        ThingSoundRequest ending = Assert.Single(Heard(events, ThingSoundEvent.SightOfEvilEnding));
        Assert.Equal((ushort)0xE8, (ushort)ending.Event);
        Assert.Equal("KEEPER_SPELL_SIGHT_OF_EVIL", ending.Category);
        Assert.Equal(new PlayerId(Owner), ending.ViewerOnlyFor);
        Assert.Equal(new SoundSourceId(SoundSourceKind.Shot, shot.Id), ending.Source);
        Assert.Equal(shot.PosX, ending.Position.X.Raw);
        Assert.True(ending.Voiced);
    }

    [Fact]
    public void ASightOfEvilAsksE8InTheUpdateWhoseHealthIsTwenty_NotTheOneBeforeOrAfter()
    {
        (GameSimulation sim, ShotState shot, ListEventSink events) = Setup(flags: 4, category: "KEEPER_SPELL_SIGHT_OF_EVIL",
            processType: 25, processFlags: 0x42, health: 64);
        shot.VelX = shot.VelY = 0;
        int healthAtTheAsk = -1;
        for (int i = 0; i < 60 && healthAtTheAsk < 0; i++)
        {
            int before = shot.Health;
            sim.Tick();
            if (Heard(events, ThingSoundEvent.SightOfEvilEnding).Length > 0) healthAtTheAsk = before;
        }

        Assert.Equal(20, healthAtTheAsk);
    }

    [Fact]
    public void AShotThatTurnsWhatItStrikesAsksDbInTheStruckCreaturesCategoryAtItsPositionWithTheShotsHandle()
    {
        (GameSimulation sim, ShotState shot, ListEventSink events) = Setup(flags: 0x102);
        CreatureState target = sim.World.SpawnCreature(sim.Database, Dk2TerrainFixture.GoblinCreatureId, 4, new TilePos(4, 4));
        shot.TargetKind = FightTargetKind.Creature;
        shot.TargetId = target.Id;
        shot.ForcedCollision = true;

        sim.Tick();

        ThingSoundRequest struck = Assert.Single(Heard(events, ThingSoundEvent.Struck));
        Assert.Equal((ushort)0xDB, (ushort)struck.Event);
        Assert.Equal("CREATURE_GOBLIN", struck.Category);
        Assert.Equal(At(target.PosX, target.PosY, target.PosZ), struck.Position);
        Assert.Equal(new SoundSourceId(SoundSourceKind.Shot, shot.Id), struck.Source);
        Assert.True(struck.Voiced);
    }

    [Fact]
    public void AShotWithoutTheTurnFlagThatStrikesACreatureAsksNoDb()
    {
        (GameSimulation sim, ShotState shot, ListEventSink events) = Setup(flags: 2);
        CreatureState target = sim.World.SpawnCreature(sim.Database, Dk2TerrainFixture.GoblinCreatureId, 4, new TilePos(4, 4));
        shot.TargetKind = FightTargetKind.Creature;
        shot.TargetId = target.Id;
        shot.ForcedCollision = true;

        sim.Tick();

        Assert.Empty(Heard(events, ThingSoundEvent.Struck));
    }
}
