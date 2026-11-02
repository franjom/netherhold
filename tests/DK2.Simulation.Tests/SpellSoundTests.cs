// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;
using static DK2.Simulation.Tests.SoundFixture;

namespace DK2.Simulation.Tests;

/// <summary>The sounds of docs/research/sound-effects-combat.md sections 1 and 2: the keeper spell's <c>0xEF</c> from
/// <c>Spell_CastAtPosition</c> (<c>0x004BBEBD</c>, only when the shot's own category is empty) and
/// <c>Spell_CastOnCreature</c> (<c>0x004BC26F</c>, always), both in the spell's category at the cast position with handle 0,
/// and the Tremor's <c>0xFB</c> (<c>World_StartTremor</c>), <c>0x2EC</c> and <c>0x31F</c> (<c>World_UpdateTremors</c>).</summary>
public sealed class SpellSoundTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;
    private static readonly TilePos At = new(10, 10);

    private static ShotDef Shot(byte id, byte process, string category, uint processFlags, int health = 8) => new(new byte[239])
    {
        Name = "Shot", ShotId = id, MeshResource = new("", 0, default, 0, 0, 0, 0),
        Light = new(0, 0, 0, 0, 0, 0, 0, 0, 0), SoundCategory = category, Speed = 2048, Radius = 819, Health = (ushort)health,
        Flags = 4, ShotProcessFlags = processFlags, ProcessType = process, AttackCategory = 6,
    };

    private static (GameDatabase Db, WorldState World, CreatureState Target) Setup(string shotCategory, string spellCategory = "KEEPER_SPELL_TEST")
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        var thunderbolt = new KeeperSpellDef
        {
            Name = "Thunderbolt", KeeperSpellId = 6, ManaCost = 6000, ManaUpkeepPerSecond = 0, ShotTypeId = 21, ShotData1 = 150,
            BonusShotTypeId = 21, BonusShotData1 = 200, LocationSelector = 1, TargetSelector = 2, SoundCategory = spellCategory,
        };
        var tremor = new KeeperSpellDef
        {
            Name = "Tremor", KeeperSpellId = 8, ManaCost = 30000, ManaUpkeepPerSecond = 0, ShotTypeId = 24, ShotData1 = 5,
            ShotData2 = 3, BonusShotTypeId = 24, BonusShotData1 = 7, BonusShotData2 = 3, LocationSelector = 5, TargetSelector = 4,
            SoundCategory = "KEEPER_SPELL_TREMOR",
        };
        basis.Variables.Misc[VariableTypeId.TremorDamagePerTerrainTilePerSecond] =
            new MiscVariable(VariableTypeId.TremorDamagePerTerrainTilePerSecond, 436, 0, 0);
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values, basis.Creatures.Values, basis.Variables,
            [thunderbolt, tremor], objects: basis.Objects.Values,
            shots: [Shot(21, 9, shotCategory, 0x40), Shot(24, 27, shotCategory, 0x40, 10)]);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        world.Player(Keeper).Mana = 90000;
        world.Player(Keeper).Availabilities.Add(new AvailabilityState(AvailabilityKind.Spell, 6, 3));
        world.Player(Keeper).Availabilities.Add(new AvailabilityState(AvailabilityKind.Spell, 8, 3));
        world.Tile(At).TerrainId = Dk2TerrainFixture.ClaimedPath;
        world.Tile(At).OwnerPlayerId = Keeper;
        CreatureState target = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, At);
        return (db, world, target);
    }

    private static GameSimulation Sim(GameDatabase db, WorldState world, out ListEventSink events)
    {
        // Every draw is the middle of the Tremor's square sample (side 7), so its floor is the cast tile itself.
        var sim = new GameSimulation(db, world, new SimRandom(new MiddleDraw()), [new KeeperSpellSystem(), new ShotSystem(), new TremorSystem()]);
        events = new ListEventSink();
        sim.ObserveWith(events);
        return sim;
    }

    private static ThingSoundRequest[] Heard(ListEventSink events, ThingSoundEvent sound) =>
        Sounds(events).Where(s => s.Event == sound).ToArray();

    private static CastSpellAtPlaceCommand CastTremor() =>
        new(Keeper, 8, CreatureState.CentreOf(At.X), CreatureState.CentreOf(At.Y));

    [Fact]
    public void ACastAtAPlaceWhoseShotHasNoCategoryAsksEfInTheSpellsCategoryAtTheCastPointWithHandleZero()
    {
        (GameDatabase db, WorldState world, _) = Setup(shotCategory: "");
        GameSimulation sim = Sim(db, world, out ListEventSink events);

        sim.Tick(InputFrame.Of(CastTremor()));

        ThingSoundRequest cast = Assert.Single(Heard(events, ThingSoundEvent.SpellCast));
        Assert.Equal((ushort)0xEF, (ushort)cast.Event);
        Assert.Equal("KEEPER_SPELL_TREMOR", cast.Category);
        Assert.Equal(new FpPosition3(new Fp32(CreatureState.CentreOf(At.X)), new Fp32(CreatureState.CentreOf(At.Y)), new Fp32(0x1000)), cast.Position);
        Assert.False(cast.Voiced);
    }

    [Fact]
    public void ACastAtAPlaceWhoseShotHasACategoryAsksEfOnlyFromTheShotsOwnConstructionInThatCategory()
    {
        (GameDatabase db, WorldState world, _) = Setup(shotCategory: "SPELL_TEST");
        GameSimulation sim = Sim(db, world, out ListEventSink events);

        sim.Tick(InputFrame.Of(CastTremor()));

        ThingSoundRequest made = Assert.Single(Heard(events, ThingSoundEvent.SpellCast));
        Assert.Equal("SPELL_TEST", made.Category);
        Assert.True(made.Voiced);
        Assert.Equal(new SoundSourceId(SoundSourceKind.Shot, Assert.Single(world.Shots).Id), made.Source);
    }

    [Theory]
    [InlineData("")]
    [InlineData("SPELL_TEST")]
    public void ACastOnACreatureAsksEfInTheSpellsCategoryAtTheTargetsPositionWhateverTheShotsCategory(string shotCategory)
    {
        (GameDatabase db, WorldState world, CreatureState target) = Setup(shotCategory);
        GameSimulation sim = Sim(db, world, out ListEventSink events);

        sim.Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, 6, target.Id)));

        ThingSoundRequest cast = Assert.Single(Heard(events, ThingSoundEvent.SpellCast), s => s.Category == "KEEPER_SPELL_TEST");
        Assert.Equal(new FpPosition3(new Fp32(target.PosX), new Fp32(target.PosY), new Fp32(target.PosZ)), cast.Position);
        Assert.False(cast.Voiced);
        Assert.Equal(shotCategory.Length == 0 ? 1 : 2, Heard(events, ThingSoundEvent.SpellCast).Length);   // the shot's own, when it has one
    }

    [Fact]
    public void ARefusedCastAsksNothing()
    {
        (GameDatabase db, WorldState world, CreatureState target) = Setup(shotCategory: "");
        world.Player(Keeper).Mana = 5999;
        GameSimulation sim = Sim(db, world, out ListEventSink events);

        sim.Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, 6, target.Id)));

        Assert.Empty(Sounds(events));
    }

    [Fact]
    public void ATremorStartsWithFbInTheSpellsCategoryAtTheShotWithItsOwnVoiceAtHeightTwoThousand()
    {
        (GameDatabase db, WorldState world, _) = Setup(shotCategory: "");
        GameSimulation sim = Sim(db, world, out ListEventSink events);

        sim.Tick(InputFrame.Of(CastTremor()));

        ThingSoundRequest start = Assert.Single(Heard(events, ThingSoundEvent.TremorStarted));
        Assert.Equal((ushort)0xFB, (ushort)start.Event);
        Assert.Equal("KEEPER_SPELL_TREMOR", start.Category);
        Assert.Equal(new SoundSourceId(SoundSourceKind.Tremor, 0), start.Source);
        Assert.Equal(new FpPosition3(new Fp32(CreatureState.CentreOf(At.X)), new Fp32(CreatureState.CentreOf(At.Y)), new Fp32(0x2000)), start.Position);
        Assert.True(start.Voiced);
    }

    [Fact]
    public void ATremorShakesWith2ecUnvoicedAtTheChosenFloorTilesCentreAtHeightZeroOnEveryActiveUpdateThatFindsOne()
    {
        (GameDatabase db, WorldState world, _) = Setup(shotCategory: "");
        GameSimulation sim = Sim(db, world, out ListEventSink events);

        sim.Tick(InputFrame.Of(CastTremor()));
        sim.Tick();

        ThingSoundRequest[] shook = Heard(events, ThingSoundEvent.TremorShook);
        Assert.Equal(2, shook.Length);
        Assert.All(shook, s =>
        {
            Assert.Equal("KEEPER_SPELL_TREMOR", s.Category);
            Assert.False(s.Voiced);
            Assert.Equal(0, s.Position.Z.Raw);
            Assert.Equal(0x800, s.Position.X.Raw & 0xFFF);   // a tile's centre
            Assert.Equal(0x800, s.Position.Y.Raw & 0xFFF);
        });
    }

    [Fact]
    public void ATremorEndsWith31fAtItsPlaceWithItsVoiceAtHeightTwoThousandAndNothingMoreAfterwards()
    {
        (GameDatabase db, WorldState world, _) = Setup(shotCategory: "");
        GameSimulation sim = Sim(db, world, out ListEventSink events);
        sim.Tick(InputFrame.Of(CastTremor()));
        int duration = db.KeeperSpellById(8)!.ShotData1 * SimulationTiming.OriginalTicksPerGameSecond;
        sim.Run(duration - 1);
        Assert.Empty(Heard(events, ThingSoundEvent.TremorEnded));

        sim.Tick();
        sim.Run(5);

        ThingSoundRequest end = Assert.Single(Heard(events, ThingSoundEvent.TremorEnded));
        Assert.Equal((ushort)0x31F, (ushort)end.Event);
        Assert.Equal("KEEPER_SPELL_TREMOR", end.Category);
        Assert.Equal(new SoundSourceId(SoundSourceKind.Tremor, 0), end.Source);
        Assert.Equal(new FpPosition3(new Fp32(CreatureState.CentreOf(At.X)), new Fp32(CreatureState.CentreOf(At.Y)), new Fp32(0x2000)), end.Position);
        Assert.True(end.Voiced);
        int shaken = Heard(events, ThingSoundEvent.TremorShook).Length;
        sim.Run(3);
        Assert.Equal(shaken, Heard(events, ThingSoundEvent.TremorShook).Length);
    }

    [Fact]
    public void ASecondTremorTakesTheNextSlotAndItsVoice()
    {
        (GameDatabase db, WorldState world, _) = Setup(shotCategory: "");
        GameSimulation sim = Sim(db, world, out ListEventSink events);

        sim.Tick(InputFrame.Of(CastTremor()));
        sim.Tick(InputFrame.Of(CastTremor()));

        Assert.Equal([0, 1], Heard(events, ThingSoundEvent.TremorStarted).Select(s => s.Source.Value));
    }

    private sealed class MiddleDraw : IRandomAlgorithm
    {
        public int NextInt(int exclusiveMax) => exclusiveMax / 2;
        public void HashInto(ref Hash64 hash) { }
    }
}
