// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;
using static DK2.Simulation.Tests.SoundFixture;

namespace DK2.Simulation.Tests;

/// <summary>The melee swing's, the death's and the corpse's sounds of docs/research/sound-effects-combat.md section 5:
/// <c>Combat_MeleeSwing</c> (<c>0x004992EB</c>) asks <c>0xDB</c> as the last thing of a swing that reached its tail,
/// <c>Creature_DeathBookkeeping</c> (<c>0x00487E4A</c>) <c>0xDA</c> unless the removal counts nothing, and <c>Corpse_Destroy</c>
/// (<c>0x004B10A0</c>) <c>0xF4</c> in <c>OBJECT_FLIES</c>; the creature's own category, position and handle for the first two.</summary>
public sealed class CombatSoundTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;

    private static (GameDatabase Db, WorldState World) Open(Func<CreatureDef, CreatureDef>? goblin = null, byte structureImpact = 0)
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values,
            basis.Creatures.Values.Select(c => c.CreatureId == Dk2TerrainFixture.GoblinCreatureId && goblin != null ? goblin(c) : c),
            basis.Variables, basis.KeeperSpells.Values, basis.Objects.Values.Select(o => o with { ImpactKind = structureImpact }),
            basis.Doors.Values.Select(d => d with { ImpactKind = structureImpact }), basis.Traps.Values.Select(t => t with { ImpactKind = structureImpact }),
            basis.CreatureSpells.Values, basis.Shots.Values, basis.Effects.Values, basis.EffectElements.Values, basis.Level, basis.CameraPathFrames);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(new TilePos(x, y)).TerrainId = Dk2TerrainFixture.DirtPath;
        return (db, world);
    }

    /// <summary>By event and by the kind of thing: 0xDA and 0xF4 are also the heart's and the terrain's, and 0xDB the shot's.</summary>
    private static ThingSoundRequest[] Heard(ListEventSink events, ThingSoundEvent sound) =>
        Sounds(events).Where(s => s.Event == sound
            && s.Source.Kind == (sound == ThingSoundEvent.CorpseDestroyed ? SoundSourceKind.Corpse : SoundSourceKind.Creature)).ToArray();

    private static FpPosition3 Where(CreatureState c) => new(new Fp32(c.PosX), new Fp32(c.PosY), new Fp32(c.PosZ));

    private static GameSimulation Run(GameDatabase db, WorldState world, Action<SimulationContext> rule, out ListEventSink events)
    {
        var sim = new GameSimulation(db, world, 1, [new Once(rule), new CreatureHealthSystem()]);
        events = new ListEventSink();
        sim.ObserveWith(events);
        return sim;
    }

    private sealed class Once(Action<SimulationContext> rule) : ISimulationSystem
    {
        private bool _done;
        public string Name => nameof(Once);
        public void Execute(SimulationContext context)
        {
            if (_done) return;
            _done = true;
            rule(context);
        }
    }

    private static void Swing(SimulationContext context, CreatureState me, FightTargetKind kind, int id)
    {
        me.SwingInProgress = true;
        me.SwingKind = SpellRules.MeleeSwing;
        me.SwingTargetKind = kind;
        me.SwingTargetId = id;
        FightRules.LandBlow(context, me, context.Database.CreatureById(me.CreatureTypeId));
    }

    [Fact]
    public void ASwingThatLandsAsksDbInTheAttackersCategoryAtItsPositionWithItsHandle()
    {
        (GameDatabase db, WorldState world) = Open();
        CreatureState me = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 5));
        CreatureState target = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(6, 5));
        int health = target.Health;
        GameSimulation sim = Run(db, world, context => Swing(context, me, FightTargetKind.Creature, target.Id), out ListEventSink events);

        sim.Tick();

        ThingSoundRequest swung = Assert.Single(Heard(events, ThingSoundEvent.Struck));
        Assert.Equal((ushort)0xDB, (ushort)swung.Event);
        Assert.Equal("CREATURE_GOBLIN", swung.Category);
        Assert.Equal(SoundSourceId.Of(new CreatureId(me.Id)), swung.Source);
        Assert.Equal(Where(me), swung.Position);
        Assert.True(swung.Voiced);
        Assert.True(target.Health < health);   // it did land
    }

    [Fact]
    public void ASwingWhoseBlowIsZeroStillAsksDbBecauseTheHandlersTailIsReached()
    {
        (GameDatabase db, WorldState world) = Open(goblin: c => c with { MeleeDamage = 0 });
        CreatureState me = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 5));
        CreatureState target = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(6, 5));
        int health = target.Health;
        GameSimulation sim = Run(db, world, context => Swing(context, me, FightTargetKind.Creature, target.Id), out ListEventSink events);

        sim.Tick();

        Assert.Equal(health, target.Health);
        Assert.Single(Heard(events, ThingSoundEvent.Struck));
    }

    [Fact]
    public void ASwingAtATargetThatIsGoneAsksNothing()
    {
        (GameDatabase db, WorldState world) = Open();
        CreatureState me = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 5));
        GameSimulation sim = Run(db, world, context => Swing(context, me, FightTargetKind.Creature, 999), out ListEventSink events);

        sim.Tick();

        Assert.Empty(Sounds(events));
    }

    [Fact]
    public void ASwingAtAnInvulnerableCreatureAsksNothingBecauseTheHandlerReturnsBeforeItsTail()
    {
        (GameDatabase db, WorldState world) = Open();
        CreatureState me = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 5));
        CreatureState target = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(6, 5));
        target.InvulnerableTicks = 100;
        GameSimulation sim = Run(db, world, context => Swing(context, me, FightTargetKind.Creature, target.Id), out ListEventSink events);

        sim.Tick();

        Assert.Empty(Sounds(events));
    }


    private static ThingSoundRequest[] Weapon(ListEventSink events) =>
        Sounds(events).Where(s => s.Category.StartsWith("WEAPON_", StringComparison.Ordinal)).ToArray();

    [Theory]
    [InlineData(AttackType.MeleeBlade, 1, "WEAPON_METAL", 0x114)]
    [InlineData(AttackType.MeleeBlunt, 2, "WEAPON_WOOD", 0x1BC)]
    [InlineData(AttackType.MeleeBody, 3, "WEAPON_FLESH", 0x113)]
    [InlineData(AttackType.MeleeScythe, 4, "WEAPON_SCYTHE", 0x1BB)]
    [InlineData(AttackType.MeleeBlade, 5, "WEAPON_METAL", 0x112)]
    [InlineData(AttackType.MeleeBlade, 6, "WEAPON_METAL", 0x112)]
    [InlineData(AttackType.MeleeBlade, 7, "WEAPON_METAL", 0x1BD)]
    public void ASwingAtACreatureAsksTheWeaponSoundOfTheAttackersAttackTypeBySurfaceAtTheAttackerWithItsHandleBeforeItsDb(
        AttackType attack, byte surface, string category, int word)
    {
        (GameDatabase db, WorldState world) = Open(c => c with { MeleeAttackType = attack, ImpactKind = surface });
        CreatureState me = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 5));
        CreatureState target = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(6, 5));
        GameSimulation sim = Run(db, world, context => Swing(context, me, FightTargetKind.Creature, target.Id), out ListEventSink events);

        sim.Tick();

        ThingSoundRequest impact = Assert.Single(Weapon(events));
        Assert.Equal(word, (int)impact.Event);
        Assert.Equal(category, impact.Category);
        Assert.Equal(SoundSourceId.Of(new CreatureId(me.Id)), impact.Source);
        Assert.Equal(Where(me), impact.Position);
        Assert.True(impact.Voiced);
        ThingSoundRequest[] all = Sounds(events);   // the impact, then the swing's 0xDB
        Assert.Same(impact.Category, all[0].Category);
        Assert.Equal(ThingSoundEvent.Struck, all[1].Event);
    }

    [Theory]
    [InlineData(AttackType.MeleeBlade, 0)]
    [InlineData(AttackType.MeleeBlade, 8)]
    [InlineData(AttackType.WizardFireball, 1)]
    public void ASwingWhoseSurfaceIsOutsideOneToSevenOrWhoseAttackTypeIsNotInTheTableAsksNoWeaponSound(AttackType attack, byte surface)
    {
        (GameDatabase db, WorldState world) = Open(c => c with { MeleeAttackType = attack, ImpactKind = surface });
        CreatureState me = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 5));
        CreatureState target = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(6, 5));
        GameSimulation sim = Run(db, world, context => Swing(context, me, FightTargetKind.Creature, target.Id), out ListEventSink events);

        sim.Tick();

        Assert.Empty(Weapon(events));
        Assert.Single(Heard(events, ThingSoundEvent.Struck));
    }

    [Fact]
    public void ASwingWhoseBlowIsZeroStillAsksTheWeaponSoundBecauseTheLookupPrecedesTheBlowTest()
    {
        (GameDatabase db, WorldState world) = Open(c => c with { MeleeDamage = 0, ImpactKind = 1 });
        CreatureState me = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 5));
        CreatureState target = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(6, 5));
        GameSimulation sim = Run(db, world, context => Swing(context, me, FightTargetKind.Creature, target.Id), out ListEventSink events);

        sim.Tick();

        Assert.Single(Weapon(events));
    }

    [Fact]
    public void ASwingAtAnInvulnerableCreatureAsksNoWeaponSoundBecauseTheHandlerReturnsBeforeTheLookup()
    {
        (GameDatabase db, WorldState world) = Open(c => c with { ImpactKind = 1 });
        CreatureState me = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 5));
        CreatureState target = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(6, 5));
        target.InvulnerableTicks = 100;
        GameSimulation sim = Run(db, world, context => Swing(context, me, FightTargetKind.Creature, target.Id), out ListEventSink events);

        sim.Tick();

        Assert.Empty(Weapon(events));
    }

    [Fact]
    public void ASwingAtADoorUsesTheDoorsSurfaceByteAndTheAttackersAttackType()
    {
        (GameDatabase db, WorldState world) = Open(structureImpact: 3);
        world.Tile(10, 10).TerrainId = Dk2TerrainFixture.ClaimedPath;
        world.Tile(10, 10).OwnerPlayerId = Rival;
        ThingProjection.Place(world, db, [new DoorThing(new byte[20])
            { X = 10, Y = 10, PlayerId = Rival, Flags = 0, DoorId = 1, Unknown_0011 = new byte[3] }]);
        CreatureState me = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(8, 10));
        GameSimulation sim = Run(db, world, context => Swing(context, me, FightTargetKind.Door, world.Doors[0].Id), out ListEventSink events);

        sim.Tick();

        ThingSoundRequest impact = Assert.Single(Weapon(events));
        Assert.Equal((0x113, "WEAPON_METAL"), ((int)impact.Event, impact.Category));
        Assert.Equal(Where(me), impact.Position);
    }

    [Fact]
    public void ASwingAtATrapUsesTheTrapsSurfaceByte()
    {
        (GameDatabase db, WorldState world) = Open(structureImpact: 4);
        world.Tile(10, 10).TerrainId = Dk2TerrainFixture.ClaimedPath;
        world.Tile(10, 10).OwnerPlayerId = Rival;
        ThingProjection.Place(world, db, [new TrapThing(new byte[16])
            { X = 10, Y = 10, TrapId = Dk2TerrainFixture.AlarmTrapId, PlayerId = Rival, NumberOfShots = 1 }]);
        CreatureState me = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(8, 10));
        GameSimulation sim = Run(db, world, context => Swing(context, me, FightTargetKind.Trap, world.Traps[^1].Id), out ListEventSink events);

        sim.Tick();

        Assert.Equal(0x1BB, (int)Assert.Single(Weapon(events)).Event);
    }

    [Fact]
    public void ASwingAtAnObjectUsesTheObjectsSurfaceByte()
    {
        (GameDatabase db, WorldState world) = Open(structureImpact: 2);
        ObjectDef def = db.Objects.Values.First();
        ObjectState thing = world.SpawnObject(db, def.ObjectId, Rival, (10 * world.Width) + 10);
        CreatureState me = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(8, 10));
        GameSimulation sim = Run(db, world, context => Swing(context, me, FightTargetKind.Object, thing.Id), out ListEventSink events);

        sim.Tick();

        Assert.Equal(0x1BC, (int)Assert.Single(Weapon(events)).Event);
    }
    [Fact]
    public void ACreatureThatDiesAndLeavesABodyAsksDaInItsCategoryAtItsPositionWithItsHandle()
    {
        (GameDatabase db, WorldState world) = Open();
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(6, 6));
        goblin.DieInstantly = true;
        goblin.Health = 0;
        GameSimulation sim = Run(db, world, _ => { }, out ListEventSink events);

        sim.Run(SimulationTiming.TicksPerSecond);

        Assert.Contains(events.Events, e => e.Kind == "creature_died");
        ThingSoundRequest freed = Assert.Single(Heard(events, ThingSoundEvent.CreatureFreed));
        Assert.Equal((ushort)0xDA, (ushort)freed.Event);
        Assert.Equal("CREATURE_GOBLIN", freed.Category);
        Assert.Equal(SoundSourceId.Of(new CreatureId(goblin.Id)), freed.Source);
        Assert.Equal(Where(goblin), freed.Position);
        Assert.True(freed.Voiced);
    }

    [Fact]
    public void ADeathThatCountsNothingAsksNoDa_TheBit0x40OfThePlanRowsDeathBookkeeping()
    {
        (GameDatabase db, WorldState world) = Open();
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(6, 6));
        goblin.DieInstantly = true;
        goblin.DeathUncounted = true;
        goblin.Health = 0;
        GameSimulation sim = Run(db, world, _ => { }, out ListEventSink events);

        sim.Run(SimulationTiming.TicksPerSecond);

        Assert.Contains(events.Events, e => e.Kind == "creature_died");
        Assert.Empty(Heard(events, ThingSoundEvent.CreatureFreed));
    }

    [Fact]
    public void ACorpseThatRotsAwayAsksF4InObjectFliesAtItsPositionWithItsOwnVoiceAndNothingForTheCreatureAgain()
    {
        (GameDatabase db, WorldState world) = Open();
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(3, 3));
        CreatureDeathRules.BecomeCorpse(world, db, goblin);
        GameSimulation sim = Run(db, world, _ => { }, out ListEventSink events);
        sim.Run(SimulationTiming.TicksPerSecond * 59);
        Assert.Empty(Heard(events, ThingSoundEvent.CorpseDestroyed));

        sim.Run(SimulationTiming.TicksPerSecond * 2);

        Assert.DoesNotContain(world.Creatures, c => c.Id == goblin.Id);
        ThingSoundRequest gone = Assert.Single(Heard(events, ThingSoundEvent.CorpseDestroyed));
        Assert.Equal((ushort)0xF4, (ushort)gone.Event);
        Assert.Equal("OBJECT_FLIES", gone.Category);
        Assert.Equal(new SoundSourceId(SoundSourceKind.Corpse, goblin.Id), gone.Source);
        Assert.Equal(Where(goblin), gone.Position);
        Assert.True(gone.Voiced);
        Assert.Empty(Heard(events, ThingSoundEvent.CreatureFreed));
    }
}
