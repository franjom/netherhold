// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;
using static DK2.Simulation.Tests.SoundFixture;

namespace DK2.Simulation.Tests;

/// <summary>A creature's spell cast of docs/research/sound-effects-combat.md section 4: after the shot is made,
/// <c>Creature_ActionTargetedSpell</c> (<c>0x00498A8D</c>) and <c>Creature_ActionFreeAimSpell</c> (<c>0x0049868C</c>) ask the creature
/// spell record's sound word (<c>+0x3F</c>, the file's <c>SoundEvent</c>) in the shot's category with the caster's own handle, at the
/// caster's position.</summary>
public sealed class CreatureSpellSoundTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;
    private const ushort Word = 0x115;

    private static (GameDatabase Db, WorldState World) Open()
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values, basis.Creatures.Values, basis.Variables,
            basis.KeeperSpells.Values, basis.Objects.Values, basis.Doors.Values, basis.Traps.Values,
            basis.CreatureSpells.Values.Select(s => s with { SoundEvent = Word }),
            basis.Shots.Values.Select(s => s with { SoundCategory = "SPELL_FIREBALL" }),
            basis.Effects.Values, basis.EffectElements.Values, basis.Level, basis.CameraPathFrames);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(new TilePos(x, y)).TerrainId = Dk2TerrainFixture.DirtPath;
        return (db, world);
    }

    private static ThingSoundRequest[] Heard(ListEventSink events) =>
        Sounds(events).Where(s => (ushort)s.Event == Word).ToArray();

    [Fact]
    public void ACastersShotLeavingAsksTheSpellsSoundWordInTheShotsCategoryWithTheCastersHandleAtItsPosition()
    {
        (GameDatabase db, WorldState world) = Open();
        CreatureState warlock = world.SpawnCreature(db, Dk2TerrainFixture.WarlockCreatureId, Keeper, new TilePos(5, 10));
        world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(8, 10));
        var spawned = new FpPosition3(new Fp32(warlock.PosX), new Fp32(warlock.PosY), new Fp32(warlock.PosZ));   // it stands where it was made when it fires
        var sim = new GameSimulation(db, world, 1, [new CombatSystem(), new CreatureMovementSystem(), new ShotSystem()]);
        var events = new ListEventSink();
        sim.ObserveWith(events);

        sim.Run(40);

        SimEvent fired = events.Events.First(e => e.Kind == "shot_fired" && e.Detail.Contains("creature=" + warlock.Id));
        SimEvent asked = events.Events.First(e => e.ThingSound is { } s && (ushort)s.Event == Word);
        Assert.Equal(fired.Tick, asked.Tick);
        ThingSoundRequest cast = asked.ThingSound!.Value;
        Assert.Equal("SPELL_FIREBALL", cast.Category);
        Assert.Equal(SoundSourceId.Of(new CreatureId(warlock.Id)), cast.Source);
        Assert.Equal(spawned, cast.Position);
        Assert.True(cast.Voiced);
    }

    [Fact]
    public void AFreeAimedShotAsksTheSameFromTheCastersPosition()
    {
        (GameDatabase db, WorldState world) = Open();
        CreatureState warlock = world.SpawnCreature(db, Dk2TerrainFixture.WarlockCreatureId, Keeper, new TilePos(5, 10));
        var sim = new GameSimulation(db, world, 1, [new Once(context =>
        {
            FightRules.BeginFreeAim(context, warlock, slot: 0, x: CreatureState.CentreOf(12), y: CreatureState.CentreOf(10), z: 0, pitch: 0, range: 16384);
            FightRules.FireFreeAim(context, warlock, context.Database.CreatureById(warlock.CreatureTypeId));
        })]);
        var events = new ListEventSink();
        sim.ObserveWith(events);

        sim.Tick();

        ThingSoundRequest cast = Assert.Single(Heard(events));
        Assert.Equal("SPELL_FIREBALL", cast.Category);
        Assert.Equal(SoundSourceId.Of(new CreatureId(warlock.Id)), cast.Source);
        Assert.Equal(new FpPosition3(new Fp32(warlock.PosX), new Fp32(warlock.PosY), new Fp32(warlock.PosZ)), cast.Position);
    }

    [Fact]
    public void ACastWhoseShotCannotBeMadeAsksNothing()
    {
        (GameDatabase db, WorldState world) = Open();
        CreatureState warlock = world.SpawnCreature(db, Dk2TerrainFixture.WarlockCreatureId, Keeper, new TilePos(5, 10));
        var sim = new GameSimulation(db, world, 1, [new Once(context =>
        {
            warlock.SwingSpellSlot = 9;   // no such slot: the spell record is missing, so no shot is made
            FightRules.FireFreeAim(context, warlock, context.Database.CreatureById(warlock.CreatureTypeId));
        })]);
        var events = new ListEventSink();
        sim.ObserveWith(events);

        sim.Tick();

        Assert.Empty(Heard(events));
    }

    private sealed class Once(Action<SimulationContext> rule) : ISimulationSystem
    {
        public string Name => nameof(Once);
        public void Execute(SimulationContext context) => rule(context);
    }
}
