// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>DK2's per-player <c>HeroesDestroyed</c> (<c>player+0x14A</c>) and the health source it
/// is credited from (creature <c>+0x36</c>): the death bookkeeping's credit path
/// <c>0x0048822F</c>–<c>0x004882DC</c>, read in the disassembly (00487C70.md, message-tabs.md §9).</summary>
public class HeroesDestroyedTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;

    [Fact]
    public void AHeroKilledByPlayerThree_CreditsPlayerThree()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState knight = Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, PlayerIds.Good);
        CreatureDef def = db.CreatureById(knight.CreatureTypeId);

        StatusEffectRules.Hurt(world, db, def, knight, 1_000_000, Keeper);

        Assert.Equal(0, knight.Health);
        Assert.Equal(Keeper, knight.HealthSourcePlayerId);
        Assert.Equal(Keeper, CreatureHealthSystem.CreditHeroKill(world, def, knight));
        Assert.Equal(1, world.Player(Keeper).HeroesDestroyed);
        Assert.Equal(0, world.Player(Rival).HeroesDestroyed);
        Assert.Equal(0, world.Player(PlayerIds.Good).HeroesDestroyed);
    }

    [Fact]
    public void TheHealthRoutineStampsItsSourceBeforeAnyRefusal()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState knight = Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, PlayerIds.Good);
        CreatureDef def = db.CreatureById(knight.CreatureTypeId);
        int before = knight.Health;

        // A zero delta lands nothing but still writes +0x36 (0x004B2CE0 stamps first).
        Assert.Equal(0, StatusEffectRules.Hurt(world, db, def, knight, 0, Rival));
        Assert.Equal(before, knight.Health);
        Assert.Equal(Rival, knight.HealthSourcePlayerId);

        CreatureHealthRules.Apply(db, def, knight, 0, Keeper);
        Assert.Equal(Keeper, knight.HealthSourcePlayerId);
    }

    [Fact]
    public void ASourceOfZeroFallsBackToTheFirstMeleeAttackersOwner()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState knight = Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, PlayerIds.Good);
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival);
        CreatureState warlock = Spawn(db, world, Dk2TerrainFixture.WarlockCreatureId, Keeper);
        CreatureDef def = db.CreatureById(knight.CreatureTypeId);
        knight.Health = 0;
        knight.HealthSourcePlayerId = 0;
        // Same slot: melee (+0x504[i]) is read before ranged (+0x514[i]).
        knight.EngagedMelee[2] = goblin.Id;
        knight.EngagedRanged[2] = warlock.Id;

        Assert.Equal(Rival, CreatureHealthSystem.CreditHeroKill(world, def, knight));
        Assert.Equal(1, world.Player(Rival).HeroesDestroyed);
        Assert.Equal(0, world.Player(Keeper).HeroesDestroyed);
    }

    [Fact]
    public void ASourceOfZeroScansSlotBySlot_SoAnEarlierRangedSlotBeatsALaterMeleeOne()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState knight = Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, PlayerIds.Good);
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival);
        CreatureState warlock = Spawn(db, world, Dk2TerrainFixture.WarlockCreatureId, Keeper);
        CreatureDef def = db.CreatureById(knight.CreatureTypeId);
        knight.Health = 0;
        knight.EngagedRanged[1] = warlock.Id;
        knight.EngagedMelee[3] = goblin.Id;

        Assert.Equal(Keeper, CreatureHealthSystem.CreditHeroKill(world, def, knight));
        Assert.Equal(1, world.Player(Keeper).HeroesDestroyed);
    }

    [Fact]
    public void NoSourceAndNoAttacker_CreditsNobody()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState knight = Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, PlayerIds.Good);
        knight.Health = 0;

        Assert.Equal(0, CreatureHealthSystem.CreditHeroKill(world, db.CreatureById(knight.CreatureTypeId), knight));
        Assert.Equal(0, world.Player(Keeper).HeroesDestroyed);
    }

    [Fact]
    public void ANonGoodVictimCountsNoHero()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Rival);
        CreatureDef def = db.CreatureById(goblin.CreatureTypeId);
        StatusEffectRules.Hurt(world, db, def, goblin, 1_000_000, Keeper);

        Assert.Equal(Keeper, CreatureHealthSystem.CreditHeroKill(world, def, goblin));
        Assert.Equal(0, world.Player(Keeper).HeroesDestroyed);
    }

    [Fact]
    public void AnUncountedDeathWithACreditedPlayerCountsNothing()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState knight = Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, PlayerIds.Good);
        CreatureDef def = db.CreatureById(knight.CreatureTypeId);
        StatusEffectRules.Hurt(world, db, def, knight, 1_000_000, Keeper);
        knight.DeathUncounted = true;   // +0x3E6 bit 0x40

        Assert.Equal(Keeper, CreatureHealthSystem.CreditHeroKill(world, def, knight));
        Assert.Equal(0, world.Player(Keeper).HeroesDestroyed);
    }

    [Fact]
    public void ThereIsNoOwnerTest_SoAKeepersOwnHeroCountsForItsKiller()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState knight = Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, Keeper);
        CreatureDef def = db.CreatureById(knight.CreatureTypeId);
        StatusEffectRules.Hurt(world, db, def, knight, 1_000_000, Keeper);

        CreatureHealthSystem.CreditHeroKill(world, def, knight);
        Assert.Equal(1, world.Player(Keeper).HeroesDestroyed);
    }

    [Fact]
    public void ACreatureStillAboveZeroHealthCountsNothing()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState knight = Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, PlayerIds.Good);
        knight.HealthSourcePlayerId = Keeper;

        Assert.Equal(0, CreatureHealthSystem.CreditHeroKill(world, db.CreatureById(knight.CreatureTypeId), knight));
        Assert.Equal(0, world.Player(Keeper).HeroesDestroyed);
    }

    [Fact]
    public void TheCountAndTheSourceSurviveASnapshot()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState knight = Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, PlayerIds.Good);
        knight.HealthSourcePlayerId = Rival;
        knight.DeathUncounted = true;
        world.Player(Keeper).HeroesDestroyed = 7;

        GameSimulation original = new(db, world, 1, []);
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(original), db, []);

        CreatureState again = restored.World.CreatureById(knight.Id)!;
        Assert.Equal(Rival, again.HealthSourcePlayerId);
        Assert.True(again.DeathUncounted);
        Assert.Equal(7, restored.World.Player(Keeper).HeroesDestroyed);
        Assert.Equal(original.StateHash(), restored.StateHash());
    }

    private static (GameDatabase, WorldState) OpenWorld()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        return (db, world);
    }

    private static CreatureState Spawn(GameDatabase db, WorldState world, byte type, byte owner) =>
        world.SpawnCreature(db, type, owner, new TilePos(5, 5));
}
