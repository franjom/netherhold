// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>Worlds for the press tests of migration plan step 2c: a synthetic floor without creatures (what CI runs), and
/// the install's Goblin on a claimed 5 x 5 floor for a pickup the local rules accept.</summary>
internal static class PressFixture
{
    public static readonly PlayerId Keeper = PlayerIds.FirstKeeper;

    private static readonly TerrainDef Floor = new()
    {
        Name = "Floor", TerrainId = 0, Flags = 0,
        StartingHealth = 1, MaxHealth = 1, GoldValue = 0, ManaGain = 0, MaxManaGain = 0,
        Damage = 0, MaxHealthTypeTerrainId = 0, DestroyedTypeTerrainId = 0,
        Depth = new Fp32(0), NameStringId = 0, TooltipStringId = 0,
    };

    public static KeeperSpellDef Spell(byte id, byte targetSelector) => new()
    {
        Name = "Test spell " + id, KeeperSpellId = id, ManaCost = 0, ManaUpkeepPerSecond = 0, ShotTypeId = 0,
        BonusShotTypeId = 0, CooldownRaw = 0, LocationSelector = 0, TargetSelector = targetSelector,
    };

    /// <summary>A synthetic worker, the one creature <see cref="CreatureOverview"/> needs to open; not a claim about DK2's.</summary>
    private static readonly CreatureDef Worker = new()
    {
        Name = "fixture", CreatureId = 1, Flags = CreatureFlags.IsEvil | CreatureFlags.IsWorker,
        Speed = default, RunSpeed = default, ShuffleSpeed = default,
        Height = default, EyeHeight = default, DistanceCanSee = default,
        DistanceCanHear = default, PerceptionRange = default, FootprintRadius = default,
        SizeClass = default, TurnRate = default, Hp = 100,
        HpFromChicken = default, HungerRate = default, HungerFill = default,
        TimeAwake = default, TimeSleep = default, OwnLandHealthIncreasePerSecond = default,
        DecomposeValue = default, Fear = default, Threat = default,
        MeleeDamage = default, SlapDamage = default, MeleeRange = default,
        MeleeRecharge = default, MeleeHitDelay = default, MeleeAttackType = default,
        StunDuration = default, FightStyle = default, Resistances = [],
        Spells = [], Pay = default, MaxGoldHeld = default,
        InitialGoldHeld = default, ExpForNextLevel = default, ExpPerSecond = default,
        ExpPerSecondTraining = default, ResearchPerSecond = default, ManufacturePerSecond = default,
        ManaGeneratedByPrayerPerSecond = default, JobClass = default, UnhappyThreshold = default,
        AngerNoLair = default, AngerNoFood = default, AngerNoPay = default,
        AngerNoWork = default, AngerSlap = default, AngerInHand = default,
        TortureHpChange = default, TortureMoodChange = default, TortureTimeToConvert = default,
        HappyJobs = [], UnhappyJobs = [], AngryJobs = [],
        HateJobs = [], AlternativeJobs = [], Attractions = [],
        GuardDuration = default, IdleDuration = default, SlapFearlessDuration = default,
        PossessionManaCost = default, LairObjectId = default, CloneCreatureId = default,
        NameStringId = default, TooltipStringId = default, IntroductionStringId = default,
    };

    /// <summary>The floor, the worker, <paramref name="spells"/> and the one variable a keeper's player needs.</summary>
    public static GameDatabase Database(params KeeperSpellDef[] spells) => Database(spells, [], []);

    /// <summary>The same, with door and trap definitions.</summary>
    public static GameDatabase Database(KeeperSpellDef[] spells, DoorDef[] doors, TrapDef[] traps)
    {
        var variables = new GlobalVariables();
        variables.Misc[VariableTypeId.DungeonHeartObjectHealth] = new(VariableTypeId.DungeonHeartObjectHealth, 100, 0, 0);
        return new GameDatabase([Floor], [], [Worker], variables, spells, doors: doors, traps: traps);
    }

    /// <summary>A synthetic door; not a claim about DK2's.</summary>
    public static DoorDef Door(byte id) => new()
    {
        Name = "Door", DoorId = id, Health = 100, GoldCost = 0, ManufactureRequired = 0, Flags = 0,
    };

    /// <summary>A synthetic trap firing <paramref name="shot"/> (13 is the alarm's); not a claim about DK2's.</summary>
    public static TrapDef Trap(byte id, byte shot) => new()
    {
        Name = "Trap", TrapId = id, Health = 100, GoldCost = 0, ManufactureRequired = 0, Flags = 0, ShotTypeId = shot,
        ManaUpkeepPerSecond = 0,
    };

    /// <summary>An 8 x 8 floor with the heroes, the neutral player and <see cref="Keeper"/>, who owns no creature.</summary>
    public static WorldState World(GameDatabase database) => WorldState.FromMap(database,
        new LevelMap(8, 8, Enumerable.Repeat(new MapTile(0, 0, 0, 0), 64).ToArray()),
        [PlayerIds.Good, PlayerIds.Neutral, Keeper.Value]);

    public static BottomPanelView Panel(GameDatabase database) => new(database, Keeper, (_, name) => name);

    public static CreatureQueryView Query(GameDatabase database) => new(database, _ => "", Keeper, new NoNames());

    /// <summary>The install's database and a Goblin of the keeper's on a claimed floor (LocalPickupFeedbackTests' fixture).</summary>
    public static (GameDatabase Database, WorldState World, CreatureState Goblin) Goblin()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        byte floor = db.Terrain.Values.Single(t => t.Name == "Claimed Path").TerrainId;
        var world = WorldState.FromMap(db, new LevelMap(5, 5,
            Enumerable.Repeat(new MapTile(floor, Keeper.Value, 0, 0), 25).ToArray()), [PlayerIds.Neutral, Keeper.Value]);
        return (db, world, world.SpawnCreature(db, db.CreatureByName("Goblin")!.CreatureId, Keeper, new TilePos(2, 2)));
    }

    private sealed class NoNames : IClassicCreatureNames
    {
        public string Name(CreatureNameIndex index) => "";
        public string BloodGroup(CreatureBloodGroupIndex index) => "";
    }
}
