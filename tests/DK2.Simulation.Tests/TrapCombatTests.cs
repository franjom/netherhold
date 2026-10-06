// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Navigation;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>A creature fighting an enemy's trap (traps.md §11, combat-acquisition.md §6): scan D
/// (<c>0x0045D7A5</c>) chooses a revealed, completed trap that is not a Jack In The Box when no
/// creature is left; the commit's trap reach is <c>MeleeRange + 0x400</c>; the swing's trap arm
/// (<c>0x0049916D</c>) lands the blow flat. All from the binary; no original-game run has been
/// compared (Q362).</summary>
public class TrapCombatTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;
    private static readonly TilePos TrapTile = new(8, 5);

    private static (GameDatabase db, WorldState world) Open()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 12), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
        return (db, world);
    }

    /// <summary>A completed trap on a tile its owner holds, revealed unless asked otherwise.</summary>
    private static TrapState Place(GameDatabase db, WorldState world, TilePos at, byte owner, byte trapId, bool hidden = false)
    {
        ref TileState tile = ref world.Tile(at);
        tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
        tile.OwnerPlayerId = owner;
        ThingProjection.Place(world, db, [new TrapThing(new byte[16]) { X = at.X, Y = at.Y, TrapId = trapId, PlayerId = owner, NumberOfShots = 1 }]);
        TrapState trap = world.Traps[^1];
        trap.Hidden = hidden;
        return trap;
    }

    private static WorldSight Sight(GameDatabase db, WorldState world)
    {
        var navigation = new WorldSteering(db, world);
        navigation.Refresh();
        return new WorldSight(navigation, world, db);
    }

    private static TrapState? Scan(GameDatabase db, WorldState world, CreatureState me)
    {
        CreatureDef def = db.CreatureById(me.CreatureTypeId);
        return CombatTargetRules.ScanTraps(world, Sight(db, world), me, def, CombatRules.RangeSquared(def.DistanceCanSee), out _);
    }

    private static GameSimulation Fight(GameDatabase db, WorldState world, ListEventSink sink)
    {
        var sim = new GameSimulation(db, world, 1,
        [
            new CreatureStatusSystem(), new CombatSystem(), new CreatureMovementSystem(), new TrapSystem(), new ShotSystem(),
            new TrapRemovalSystem(),
        ]);
        sim.ObserveWith(sink);
        return sim;
    }

    [Fact]
    public void AGoblinChoosesARevealedRivalTrap_StepsOntoIt_AndBreaksItWithFlatBlows_ThenLeavesTheFight()
    {
        (GameDatabase db, WorldState world) = Open();
        TrapState alarm = Place(db, world, TrapTile, Rival, Dk2TerrainFixture.AlarmTrapId);
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 5));
        CreatureDef def = db.CreatureById(goblin.CreatureTypeId);
        int blow = MeleeDamageRules.Swing(db, def, 1, MeleeDamageRules.Stance.None);
        Assert.True(blow > 0);
        var sink = new ListEventSink();
        GameSimulation sim = Fight(db, world, sink);

        sim.Run(1);
        Assert.Equal(FightTargetKind.Trap, goblin.FightTargetKind);
        Assert.Equal(alarm.Id, goblin.FightTargetId);
        Assert.Equal(Rival, goblin.FightTargetPlayerId);
        Assert.Equal(CombatRules.AttackMode.MeleeAtThing, goblin.AttackType);
        // 0x0045E065: a trap adds a quarter tile to MeleeRange, half what a door or an object adds.
        Assert.Equal(((def.MeleeRange.Raw + 0x400) * (def.MeleeRange.Raw + 0x400)) >> 12, goblin.AttackRangeSquared);

        for (int i = 0; i < 2000 && world.Traps.Count > 0; i++) sim.Run(1);

        Assert.Empty(world.Traps);
        // Every blow is the same, from wherever it lands: no bearing bonus on a thing. The trap's own
        // recovery (+0xA4 a second) is why the blows outnumber its health over the blow.
        List<SimEvent> hits = sink.Events.Where(e => e.Kind == "trap_hit" && e.Detail.Contains($"trap={alarm.Id}")).ToList();
        Assert.True(hits.Count >= (2000 + blow - 1) / blow, $"{hits.Count} blows");
        Assert.All(hits, e => Assert.Contains($"blow={blow} ", e.Detail));
        Assert.Contains(sink.Events, e => e.Kind == "trap_removed" && e.Detail.Contains($"trap={alarm.Id}"));
        Assert.True(CombatRules.DistanceSquared(goblin.PosX, goblin.PosY, alarm.PosX, alarm.PosY) < goblin.AttackRangeSquared
            || goblin.FightTargetKind != FightTargetKind.Trap, "it struck from within its reach");

        // Gone, the trap fails spFighting's validity test; nothing else is in sight, so the fight ends.
        sim.Run(3);
        Assert.NotEqual(FightTargetKind.Trap, goblin.FightTargetKind);
        Assert.NotEqual(CreatureActivity.Fighting, goblin.Activity);
    }

    [Fact]
    public void TheBlowLandsTheWindUpAfterTheSwingBegins()
    {
        (GameDatabase db, WorldState world) = Open();
        TrapState alarm = Place(db, world, TrapTile, Rival, Dk2TerrainFixture.AlarmTrapId);
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, TrapTile);
        int delay = CombatRules.TicksOf(db.CreatureById(goblin.CreatureTypeId).MeleeHitDelay);
        Assert.True(delay > 1);
        var sink = new ListEventSink();
        GameSimulation sim = Fight(db, world, sink);

        for (int i = 0; i < 40 && !goblin.SwingInProgress; i++) sim.Run(1);
        Assert.True(goblin.SwingInProgress);
        int full = alarm.Health;
        sim.Run(delay - 1);
        Assert.Equal(full, alarm.Health);
        sim.Run(1);
        Assert.True(alarm.Health < full);
    }

    [Fact]
    public void ScanD_PassesOverAHiddenTrap_AnUnbuiltTrap_AJackInTheBox_AndItsOwnSidesTrap()
    {
        (GameDatabase db, WorldState world) = Open();
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(6, 5));
        TrapState hidden = Place(db, world, new TilePos(7, 5), Rival, Dk2TerrainFixture.AlarmTrapId, hidden: true);
        TrapState unbuilt = Place(db, world, new TilePos(7, 4), Rival, Dk2TerrainFixture.AlarmTrapId);
        unbuilt.UnderConstruction = true;
        Place(db, world, new TilePos(7, 6), Rival, Dk2TerrainFixture.JackInTheBoxTrapId);
        Place(db, world, new TilePos(5, 5), Keeper, Dk2TerrainFixture.AlarmTrapId);
        Assert.Null(Scan(db, world, goblin));

        hidden.Hidden = false;
        Assert.Same(hidden, Scan(db, world, goblin));
    }

    [Fact]
    public void ScanD_TakesTheNearest_AndOfTwoAsNearTheNewerTrap()
    {
        (GameDatabase db, WorldState world) = Open();
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(8, 5));
        TrapState far = Place(db, world, new TilePos(11, 5), Rival, Dk2TerrainFixture.AlarmTrapId);
        TrapState older = Place(db, world, new TilePos(6, 5), Rival, Dk2TerrainFixture.AlarmTrapId);
        Assert.Same(older, Scan(db, world, goblin));
        // The owner list is prepend (0x004B4C61), so the trap linked last is walked first and keeps a tie.
        TrapState newer = Place(db, world, new TilePos(10, 5), Rival, Dk2TerrainFixture.AlarmTrapId);
        Assert.Same(newer, Scan(db, world, goblin));
        Assert.NotSame(far, Scan(db, world, goblin));
    }

    [Fact]
    public void ScanD_SeesExactlyToTheSightRangeInclusive_AndNotThroughRock()
    {
        (GameDatabase db, WorldState world) = Open();
        TrapState trap = Place(db, world, new TilePos(2, 5), Rival, Dk2TerrainFixture.AlarmTrapId);
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(8, 5));
        int sightSq = CombatRules.RangeSquared(db.CreatureById(goblin.CreatureTypeId).DistanceCanSee);
        int tiles = (int)Math.Sqrt(sightSq >> 12);
        Assert.Equal(sightSq, (tiles * tiles) << 12);

        // 0x0045D877 skips only a distance above sight², so a trap exactly at it is taken.
        goblin.PosX = trap.PosX + (tiles << 12);
        Assert.Equal(sightSq, CombatRules.DistanceSquared(goblin.PosX, goblin.PosY, trap.PosX, trap.PosY));
        Assert.Same(trap, Scan(db, world, goblin));
        goblin.PosX++;
        Assert.Null(Scan(db, world, goblin));

        goblin.PosX = trap.PosX + (2 << 12);
        Assert.Same(trap, Scan(db, world, goblin));
        world.Tile(trap.X + 1, trap.Y).TerrainId = Dk2TerrainFixture.Rock;
        Assert.Null(Scan(db, world, goblin));
    }

    [Fact]
    public void AnImpTakesATrapOnlyWithinASquaredDistanceUnder0x800()
    {
        (GameDatabase db, WorldState world) = Open();
        TrapState trap = Place(db, world, TrapTile, Rival, Dk2TerrainFixture.AlarmTrapId);
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, TrapTile);
        // 2896² >> 12 is 2047 and 2897² >> 12 is 2048 (0x0045D86A: JGE 0x800 skips).
        imp.PosX = trap.PosX + 2896;
        Assert.Same(trap, Scan(db, world, imp));
        imp.PosX = trap.PosX + 2897;
        Assert.Null(Scan(db, world, imp));
    }

    [Fact]
    public void AnImpWithNoCreatureToFaceFallsThroughToScanD_AndAttacksTheRevealedTrapUnderIt()
    {
        (GameDatabase db, WorldState world) = Open();
        TrapState trap = Place(db, world, TrapTile, Rival, Dk2TerrainFixture.AlarmTrapId);
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, TrapTile);
        var sink = new ListEventSink();
        Fight(db, world, sink).Run(1);
        Assert.Equal(FightTargetKind.Trap, imp.FightTargetKind);
        Assert.Equal(trap.Id, imp.FightTargetId);
    }

    [Fact]
    public void AHostileCreatureInSightOutranksTheTrap_ForScanDRunsOnlyWithNoneLeft()
    {
        (GameDatabase db, WorldState world) = Open();
        Place(db, world, TrapTile, Rival, Dk2TerrainFixture.AlarmTrapId);
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(7, 5));
        CreatureState enemy = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(4, 5));
        Fight(db, world, new ListEventSink()).Run(1);
        Assert.Equal(FightTargetKind.Creature, goblin.FightTargetKind);
        Assert.Equal(enemy.Id, goblin.FightTargetId);
    }

    /// <summary>A Support caster takes its spell at any distance (the melee rule skips fight type 4),
    /// and its shot strikes the trap through the enemies predicate's overlap.</summary>
    [Fact]
    public void AWarlockCastsAtARevealedTrap_AndItsShotsStrikeIt()
    {
        (GameDatabase db, WorldState world) = Open();
        TrapState trap = Place(db, world, TrapTile, Rival, Dk2TerrainFixture.AlarmTrapId);
        CreatureState warlock = world.SpawnCreature(db, Dk2TerrainFixture.WarlockCreatureId, Keeper, new TilePos(5, 5));
        var sink = new ListEventSink();
        GameSimulation sim = Fight(db, world, sink);

        sim.Run(1);
        Assert.Equal(FightTargetKind.Trap, warlock.FightTargetKind);
        Assert.Equal(CombatRules.AttackMode.SpellAtThing, warlock.AttackType);
        for (int i = 0; i < 200 && !sink.Events.Any(e => e.Kind == "shot_trap_hit"); i++) sim.Run(1);
        Assert.Contains(sink.Events, e => e.Kind == "shot_trap_hit" && e.Detail.Contains($"trap={trap.Id}"));
    }

    [Fact]
    public void ATrapThatDiesUnderAnotherHandIsDropped_AndTheFightEnds()
    {
        (GameDatabase db, WorldState world) = Open();
        TrapState trap = Place(db, world, TrapTile, Rival, Dk2TerrainFixture.AlarmTrapId);
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 5));
        GameSimulation sim = Fight(db, world, new ListEventSink());
        sim.Run(1);
        Assert.Equal(FightTargetKind.Trap, goblin.FightTargetKind);

        TrapRules.Damage(trap, -trap.Health);
        sim.Run(3);
        Assert.Empty(world.Traps);
        Assert.NotEqual(FightTargetKind.Trap, goblin.FightTargetKind);
        Assert.NotEqual(CreatureActivity.Fighting, goblin.Activity);
    }
}
