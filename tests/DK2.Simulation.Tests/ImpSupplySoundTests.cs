// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;
using static DK2.Simulation.Tests.SoundFixture;

namespace DK2.Simulation.Tests;

/// <summary>Create Imp's cast (<c>ImpSupplySystem.Cast</c>, the mirror of <c>Spell_CastAtPosition</c> <c>0x004BBD30</c>), bought and free:
/// shot 2 has an empty category, so DK2 asks <c>0xEF</c> in <c>KEEPER_SPELL_CREATE_IMP</c> at <c>0x004BBEBD</c> and the shot's own
/// constructor asks nothing; a shot with a category asks the constructor's <c>0xEF</c> and <c>0xF2</c> in it and the cast asks nothing.</summary>
public sealed class ImpSupplySoundTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    private static (GameSimulation Sim, WorldState World) Setup(string shotCategory, bool bought)
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        GameDatabase db = WithSpellAndShotCategories(basis,
            s => s.Name == KeeperSpellName.CreateImp ? s with { SoundCategory = "KEEPER_SPELL_CREATE_IMP" } : s,
            s => s.ShotId is 2 or 60 ? s with { SoundCategory = shotCategory } : s);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        Dk2TerrainFixture.HeartRoom(world, db, Keeper);
        world.Player(Keeper).Mana = 10_000;
        world.Player(Keeper).NextFreeImpTick = bought ? new GameTick(uint.MaxValue) : GameTick.Zero;
        var sim = new GameSimulation(db, world, 5, Dk2TerrainFixture.SystemsWithout<DungeonHeartSystem>());
        return (sim, world);
    }

    private static ThingSoundRequest[] Heard(ListEventSink events, ThingSoundEvent sound) =>
        Sounds(events).Where(s => s.Event == sound
            && s.Source.Kind is SoundSourceKind.Player or SoundSourceKind.Shot or SoundSourceKind.ShotSecond).ToArray();

    private static ListEventSink Observe(GameSimulation sim)
    {
        var events = new ListEventSink();
        sim.ObserveWith(events);
        return events;
    }

    [Fact]
    public void ABoughtCastOfAShotWithNoCategoryAsksEfInKeeperSpellCreateImpAtTheCastPointWithHandleZero()
    {
        (GameSimulation sim, _) = Setup("", bought: true);
        sim.Run(40);
        ListEventSink events = Observe(sim);
        var point = new FpPosition2(new Fp32((2 * Fp32.One) + 0x234), new Fp32((3 * Fp32.One) + 0x345));

        sim.Tick(InputFrame.Of(new CreateImpCommand(Keeper, point)));

        Assert.Contains(events.Events, e => e.Kind == "imp_created");
        ThingSoundRequest cast = Assert.Single(Heard(events, ThingSoundEvent.SpellCast));
        Assert.Equal("KEEPER_SPELL_CREATE_IMP", cast.Category);
        Assert.False(cast.Voiced);
        Assert.Equal((point.X.Raw, point.Y.Raw), (cast.Position.X.Raw, cast.Position.Y.Raw));
        Assert.Empty(Heard(events, ThingSoundEvent.ShotMadeSecond));
    }

    [Fact]
    public void EveryFreeCastAsksTheSameFromThePointItDrew()
    {
        (GameSimulation sim, _) = Setup("", bought: false);
        ListEventSink events = Observe(sim);

        sim.Run(80);

        int generated = events.Events.Count(e => e.Kind == "imp_generated");
        Assert.True(generated >= 1);
        Assert.Equal(generated, Heard(events, ThingSoundEvent.SpellCast).Count(s => s.Category == "KEEPER_SPELL_CREATE_IMP" && !s.Voiced));
    }

    [Fact]
    public void AShotWithACategoryAsksItsConstructorsEfAndF2InItAndTheCastAsksNothing()
    {
        (GameSimulation sim, _) = Setup("SPELL_TEST", bought: true);
        sim.Run(40);
        ListEventSink events = Observe(sim);

        sim.Tick(InputFrame.Of(new CreateImpCommand(Keeper, new FpPosition2(new Fp32(2 * Fp32.One), new Fp32(3 * Fp32.One)))));

        ThingSoundRequest made = Assert.Single(Heard(events, ThingSoundEvent.SpellCast));
        Assert.Equal("SPELL_TEST", made.Category);
        Assert.Equal(SoundSourceKind.Shot, made.Source.Kind);
        ThingSoundRequest second = Assert.Single(Heard(events, ThingSoundEvent.ShotMadeSecond));
        Assert.Equal(SoundSourceKind.ShotSecond, second.Source.Kind);
        Assert.Equal(made.Source.Value, second.Source.Value);
    }
}
