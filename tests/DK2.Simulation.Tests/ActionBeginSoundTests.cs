// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;
using static DK2.Simulation.Tests.SoundFixture;

namespace DK2.Simulation.Tests;

/// <summary>The sound <c>CreatureAction_Begin</c> (<c>0x00496700</c>, <c>0x004968BC</c>) asks when an action begins: the action record's
/// <c>+25</c> word in the creature's category, with its handle, at its position. Ours asks it where our code begins the action it
/// mirrors (docs/research/sound-effects-continuous.md section 2). Counting tests filter by the event and the creature kind, as
/// events alias (<c>0xD3</c> is also a trainee's state sound).</summary>
public sealed class ActionBeginSoundTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;
    private static readonly TilePos Face = new(4, 2);

    private static ThingSoundRequest[] Heard(ListEventSink events, int word) =>
        Sounds(events).Where(s => (ushort)s.Event == word && s.Source.Kind == SoundSourceKind.Creature).ToArray();

    // ---- The imp's dig and mine ----

    private static (GameSimulation Sim, ListEventSink Events, CreatureState Imp) Worker(TileWorkKind kind, long entered)
    {
        GameDatabase fixture = Dk2TerrainFixture.Database();
        var art = new CreatureAnimationTiming[CreatureDef.ArtSlotCount];
        art[5] = new CreatureAnimationTiming
        {
            ResourceType = 6, Flags = CreatureAnimationFlags.Present | CreatureAnimationFlags.EndSegment,
            FrameCount = 15, FramesPerSecond = 20, EndSegmentFrames = 8,
        };
        var database = new GameDatabase(fixture.Terrain.Values, fixture.Rooms.Values,
            fixture.Creatures.Values.Select(c => c.CreatureId == Dk2TerrainFixture.ImpCreatureId ? c with { ArtSlots = art } : c),
            fixture.Variables, objects: fixture.Objects.Values, keeperSpells: fixture.KeeperSpells.Values);
        WorldState world = WorldState.FromMap(database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        CreatureState imp = world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(3, 2));
        ref TileState tile = ref world.Tile(Face);
        tile.TerrainId = kind == TileWorkKind.MineGold ? Dk2TerrainFixture.Gold : Dk2TerrainFixture.Rock;
        tile.OwnerPlayerId = PlayerIds.Neutral;
        tile.Health = 30000;
        tile.SetTaggedBy(Keeper, true);
        imp.TargetWork = kind;
        imp.TargetTileIndex = (Face.Y * world.Width) + Face.X;
        imp.Dk2StateId = ImpStates.For(kind).Work;
        imp.StateEnteredTick = entered;
        imp.TimerTicks = 2;   // the entry update has run: three ticks to the first application, less its own turn
        typeof(WorldState).GetProperty(nameof(WorldState.CurrentTick))!.SetValue(world, new GameTick(entered));
        var sim = new GameSimulation(database, world, 99, [new ImpTaskSystem()]);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        return (sim, events, imp);
    }

    [Fact]
    public void ADiggingImpAsks334WhenItsDigActionBeginsAndAgainEachTimeItsClipWraps()
    {
        var (sim, events, imp) = Worker(TileWorkKind.Dig, entered: 100);
        sim.Run(40);

        ThingSoundRequest[] heard = Heard(events, 0x334);
        long[] ticks = events.Events.Where(e => e.ThingSound is { } s && (ushort)s.Event == 0x334).Select(e => e.Tick.Value).ToArray();
        Assert.NotEmpty(ticks);
        // The action begins on the turn after the work state is entered, and again as each cycle of the clip wraps: the first cycle
        // takes four turns and the rest three (mining-cadence.md section 5).
        Assert.Equal([101L, 105, 108, 111, 114, 117], ticks.Take(6));
        Assert.All(heard, s =>
        {
            Assert.Equal("CREATURE_IMP", s.Category);
            Assert.Equal(SoundSourceId.Of(new CreatureId(imp.Id)), s.Source);
            Assert.True(s.Voiced);
        });
        Assert.Empty(Heard(events, 0x109));
    }

    [Fact]
    public void AMiningImpAsks109ForActionTwoAndNot334()
    {
        var (sim, events, _) = Worker(TileWorkKind.MineGold, entered: 100);
        sim.Run(40);

        Assert.NotEmpty(Heard(events, 0x109));
        Assert.Empty(Heard(events, 0x334));
        Assert.Equal(101, events.Events.First(e => e.ThingSound is { } s && (ushort)s.Event == 0x109).Tick.Value);
    }

    [Fact]
    public void AWallGoneBeforeTheFirstTurnEndsTheWorkStateAndAsksNothing_NotGuardedByTheBeginBranchItself()
    {
        var (sim, events, _) = Worker(TileWorkKind.Dig, entered: 100);
        sim.World.Tile(Face).TerrainId = Dk2TerrainFixture.DirtPath;
        sim.World.Tile(Face).SetTaggedBy(Keeper, false);
        sim.Run(10);

        Assert.Empty(Heard(events, 0x334));
    }


    // ---- The imp's wall repair ----

    /// <summary>Action 8 is the repair state's (<c>0x00466710</c>, state 24): it begins on the handler's first call with no action
    /// running (<c>0x004667C0</c>) and its record's word is <c>0x10C</c>, so the imp's own <c>0x10C</c> is the repair's, not
    /// reinforcing (STRONGLY INFERRED: the begin's gate and the repair clip's length are unread, so ours asks once per work state).</summary>
    [Fact]
    public void AnImpRepairingAWallAsks10COnceAtTheFirstCallOfItsWorkState_ModelsOnlyTheFirstBegin()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        var at = new TilePos(8, 5);
        ref TileState floor = ref world.Tile(at);
        floor.TerrainId = Dk2TerrainFixture.ClaimedPath;
        floor.Health = database.TerrainById(Dk2TerrainFixture.ClaimedPath).StartingHealth;
        floor.OwnerPlayerId = Keeper;
        ushort full = database.TerrainById(Dk2TerrainFixture.ReinforcedWall).MaxHealth;
        foreach (TilePos side in new TilePos[] { new(9, 5), new(7, 5), new(8, 6), new(8, 4) })
        {
            ref TileState wall = ref world.Tile(side);
            wall.TerrainId = Dk2TerrainFixture.ReinforcedWall;
            wall.OwnerPlayerId = Keeper;
            wall.Health = full;
        }
        world.Tile(new TilePos(9, 5)).Health = (ushort)(full - 100);
        CreatureState imp = world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, Keeper, at);
        var sim = new GameSimulation(database, world, 99, Dk2TerrainFixture.DefaultSystems());
        var events = new ListEventSink();
        sim.ObserveWith(events);

        long entered = -1;
        for (int i = 0; i < SimulationTiming.TicksFromSeconds(6); i++)
        {
            sim.Tick();
            if (entered < 0 && imp.Dk2StateId == ImpStates.For(TileWorkKind.RepairWall).Work) entered = imp.StateEnteredTick;
        }

        Assert.True(entered >= 0);
        SimEvent begun = Assert.Single(events.Events, e => e.ThingSound is { Source.Kind: SoundSourceKind.Creature } s && (ushort)s.Event == 0x10C);
        Assert.Equal(entered + 1, begun.Tick.Value);
        Assert.Equal("CREATURE_IMP", begun.ThingSound!.Value.Category);
        Assert.Equal(SoundSourceId.Of(new CreatureId(imp.Id)), begun.ThingSound!.Value.Source);
    }

    // ---- The swing, the cast and the free-aimed cast ----

    private static (GameDatabase Db, WorldState World) Open()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(new TilePos(x, y)).TerrainId = Dk2TerrainFixture.DirtPath;
        return (db, world);
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

    private static ListEventSink Run(GameDatabase db, WorldState world, Action<SimulationContext> rule)
    {
        var sim = new GameSimulation(db, world, 1, [new Once(rule)]);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        sim.Tick();
        return events;
    }

    private static FpPosition3 Where(CreatureState c) => new(new Fp32(c.PosX), new Fp32(c.PosY), new Fp32(c.PosZ));

    [Fact]
    public void AMeleeSwingBeginningAsksD7InTheAttackersCategoryWithItsHandleAtItsPosition()
    {
        (GameDatabase db, WorldState world) = Open();
        CreatureState me = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 5));
        CreatureState target = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(6, 5));
        me.FightTargetKind = FightTargetKind.Creature;
        me.FightTargetId = target.Id;
        ListEventSink events = Run(db, world, context =>
        {
            context.Navigation.Refresh();
            FightRules.StartSwing(context, new WorldSight(context.Navigation, world, db), me, db.CreatureById(me.CreatureTypeId), []);
        });

        ThingSoundRequest begun = Assert.Single(Heard(events, 0xD7));
        Assert.Equal("CREATURE_GOBLIN", begun.Category);
        Assert.Equal(SoundSourceId.Of(new CreatureId(me.Id)), begun.Source);
        Assert.Equal(Where(me), begun.Position);
        Assert.True(begun.Voiced);
    }

    [Fact]
    public void ASpellCastBeginningAsksD8()
    {
        (GameDatabase db, WorldState world) = Open();
        CreatureState warlock = world.SpawnCreature(db, Dk2TerrainFixture.WarlockCreatureId, Keeper, new TilePos(5, 10));
        world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(8, 10));
        warlock.FightTargetKind = FightTargetKind.Creature;
        ListEventSink events = Run(db, world, context =>
            FightRules.StartCast(context, warlock, db.CreatureById(warlock.CreatureTypeId), 0));

        Assert.Single(Heard(events, 0xD8));
        Assert.Empty(Heard(events, 0xD7));
    }

    [Theory]
    [InlineData(SpellRules.MeleeSwing, 0xD7)]
    [InlineData(SpellRules.SpellCast, 0xD8)]
    public void APossessedCreatureBeginningAnActionAsksItsRecordsWord(byte kind, int word)
    {
        (GameDatabase db, WorldState world) = Open();
        CreatureState me = world.SpawnCreature(db, Dk2TerrainFixture.WarlockCreatureId, Keeper, new TilePos(5, 10));
        ListEventSink events = Run(db, world, context =>
            FightRules.BeginPossessedAction(context, me, kind, slot: 0, FightTargetKind.None, CombatRules.NoTarget));

        Assert.Single(Heard(events, word));
    }

    [Fact]
    public void AFreeAimedCastBeginningAsksD8()
    {
        (GameDatabase db, WorldState world) = Open();
        CreatureState me = world.SpawnCreature(db, Dk2TerrainFixture.WarlockCreatureId, Keeper, new TilePos(5, 10));
        ListEventSink events = Run(db, world, context =>
            FightRules.BeginFreeAim(context, me, slot: 0, x: CreatureState.CentreOf(12), y: CreatureState.CentreOf(10), z: 0, pitch: 0, range: 16384));

        Assert.Single(Heard(events, 0xD8));
    }
}
