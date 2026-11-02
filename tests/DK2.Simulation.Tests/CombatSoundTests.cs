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

    private static (GameDatabase Db, WorldState World) Open(Func<CreatureDef, CreatureDef>? goblin = null)
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values,
            basis.Creatures.Values.Select(c => c.CreatureId == Dk2TerrainFixture.GoblinCreatureId && goblin != null ? goblin(c) : c),
            basis.Variables, basis.KeeperSpells.Values, basis.Objects.Values, basis.Doors.Values, basis.Traps.Values,
            basis.CreatureSpells.Values, basis.Shots.Values, basis.Effects.Values, basis.EffectElements.Values, basis.Level, basis.CameraPathFrames);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(new TilePos(x, y)).TerrainId = Dk2TerrainFixture.DirtPath;
        return (db, world);
    }

    private static ThingSoundRequest[] Heard(ListEventSink events, ThingSoundEvent sound) =>
        Sounds(events).Where(s => s.Event == sound).ToArray();

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
