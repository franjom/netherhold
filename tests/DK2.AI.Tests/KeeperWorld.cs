// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Combat;

namespace DK2.AI.Tests;

/// <summary>
/// A small synthetic world for the keeper: a rock map with an impenetrable
/// border, one keeper (player 3) with a heart and a patch of claimed floor, and
/// whatever seams and walls a test carves. Terrain and room ids are local to
/// this fixture and mean nothing outside it.
/// </summary>
internal static class KeeperWorld
{
    public const byte Impenetrable = 1;
    public const byte Rock = 2;
    public const byte DirtPath = 3;
    public const byte Gold = 6;
    public const byte ClaimedPath = 8;
    public const byte TreasuryFloor = 10;
    public const byte LairFloor = 11;
    public const byte HatcheryFloor = 12;
    public const byte PortalFloor = 13;
    public const byte HeroPortalFloor = 14;
    public const byte HeartFloor = 20;
    public const byte PrisonFloor = 21;
    public const byte LibraryFloor = 22;
    public const byte Lava = 7;
    public const byte Water = 5;
    public const byte WoodenBridgeFloor = 17;
    public const byte StoneBridgeFloor = 26;
    public const byte TrainingFloor = 23;
    public const byte CombatPitFloor = 24;
    public const byte TempleFloor = 27;

    /// <summary>Shot 5, Create Object, and a Lightning shot for Thunderbolt; the flag is object 10.</summary>
    public const byte CreateObjectShotId = 5;
    public const byte LightningShotId = 30;

    /// <summary>A worker and a fighter, so the keeper has something to move; a hero owns the
    /// same fighter record when it attacks.</summary>
    public const byte WorkerCreature = 1;
    public const byte FighterCreature = 2;
    public const ushort FighterThreat = 50;

    /// <summary>A creature that sleeps in a bed of <see cref="BedObjectId"/>, so a missing or full
    /// Lair is something its sleep need finds (messages 41 to 43).</summary>
    public const byte SleeperCreature = 3;
    public const byte BedObjectId = 22;

    /// <summary>A creature that grows hungry and eats one chicken, so a missing or empty Hatchery is
    /// something its need finds (messages 41 to 43); the coop, egg and chicken are the shipped records.</summary>
    public const byte EaterCreature = 4;

    /// <summary>A creature with a wage and a happy table of Research then Train, for the mood remedies.</summary>
    public const byte MoodyCreature = 5;

    public const byte Keeper = 3;
    public static readonly TilePos HeartAt = new(15, 15);

    public static GameDatabase Database(int handCapacity = 64)
    {
        return new(
        [
            Terrain(Impenetrable, "Impenetrable Rock", TerrainFlags.Solid | TerrainFlags.Impenetrable, 0),
            Terrain(Rock, "Rock", TerrainFlags.Solid | TerrainFlags.Taggable, 165, destroyedType: DirtPath),
            Terrain(DirtPath, "Dirt Path", TerrainFlags.Ownable, 100, maxHealthType: ClaimedPath),
            Terrain(Gold, "Gold", TerrainFlags.Solid | TerrainFlags.Taggable, 300, destroyedType: DirtPath, gold: 1024),
            Terrain(ClaimedPath, "Claimed Path", TerrainFlags.Ownable, 100),
            Terrain(TreasuryFloor, "Treasury", TerrainFlags.Ownable | TerrainFlags.Room, 100, destroyedType: ClaimedPath),
            Terrain(LairFloor, "Lair", TerrainFlags.Ownable | TerrainFlags.Room, 100, destroyedType: ClaimedPath),
            Terrain(HatcheryFloor, "Hatchery", TerrainFlags.Ownable | TerrainFlags.Room, 100),
            Terrain(HeroPortalFloor, "Hero Portal", TerrainFlags.Ownable | TerrainFlags.Room, 100),
            Terrain(PortalFloor, "Portal", TerrainFlags.Ownable | TerrainFlags.Room, 100),
            Terrain(HeartFloor, "Dungeon Heart", TerrainFlags.Ownable | TerrainFlags.Room, 100),
            Terrain(PrisonFloor, "Prison", TerrainFlags.Ownable | TerrainFlags.Room, 100),
            Terrain(LibraryFloor, "Library", TerrainFlags.Ownable | TerrainFlags.Room, 100),
            Terrain(TrainingFloor, "Training Room", TerrainFlags.Ownable | TerrainFlags.Room, 100),
            Terrain(CombatPitFloor, "Combat Pit", TerrainFlags.Ownable | TerrainFlags.Room, 100),
            Terrain(TempleFloor, "Temple", TerrainFlags.Ownable | TerrainFlags.Room, 100),
            Terrain(Lava, "Lava", TerrainFlags.Lava, 0),
            // Water, Lava and both bridges carry their shipped placement-relevant flags: the hazards
            // are Impenetrable and not Solid, the bridges Ownable rooms (kwd terrainflags).
            Terrain(Water, "Water", TerrainFlags.Impenetrable | TerrainFlags.Water, 0),
            Terrain(WoodenBridgeFloor, "Wooden Bridge", TerrainFlags.Ownable | TerrainFlags.Room, 100),
            Terrain(StoneBridgeFloor, "Stone Bridge", TerrainFlags.Ownable | TerrainFlags.Room, 100),
        ],
        rooms:
        [
            Room(RoomTypeId.Treasury, "Treasury", 50, TreasuryFloor, buildable: true),
            Room(RoomTypeId.Lair, "Lair", 100, LairFloor, buildable: true),
            Room(RoomTypeId.Hatchery, "Hatchery", 125, HatcheryFloor, buildable: true),
            Room(RoomTypeId.HeroPortal, "Hero Portal", 0, HeroPortalFloor, buildable: false),
            Room(RoomTypeId.Portal, "Portal", 0, PortalFloor, buildable: false),
            Room(RoomTypeId.DungeonHeart, "Dungeon Heart", 0, HeartFloor, buildable: false),
            Room(RoomTypeId.Prison, "Prison", 0, PrisonFloor, buildable: false),
            Room(RoomTypeId.Library, "Library", 0, LibraryFloor, buildable: false),
            // The shipped flags, 0x13AC and 0x12AC: special tiles, so the dummies stand on the floor slots.
            Room(RoomTypeId.TrainingRoom, "Training Room", 0, TrainingFloor, buildable: false, (RoomFlags)0x13ACu, [TrainingDummyObjectId, 0, 0, 0]),
            Room(RoomTypeId.CombatPit, "Combat Pit", 0, CombatPitFloor, buildable: false, (RoomFlags)0x12ACu),
            Room(RoomTypeId.Temple, "Temple", 0, TempleFloor, buildable: false),
            // Both bridges as shipped: 0x1083 on each, so either could span water or lava, 200 and 500 a tile.
            Room(RoomTypeId.WoodenBridge, "Wooden Bridge", 200, WoodenBridgeFloor, buildable: true, BridgeFlags),
            Room(RoomTypeId.StoneBridge, "Stone Bridge", 500, StoneBridgeFloor, buildable: true, BridgeFlags),
        ],
        creatures:
        [
            Creature("Worker", WorkerCreature, CreatureFlags.IsWorker | CreatureFlags.IsEvil | CreatureFlags.CanBePickedUp
                | CreatureFlags.CanBeSlapped, hp: 500, threat: 2, JobClass.Scout, FightStyle.NonFighter),
            Creature("Fighter", FighterCreature, CreatureFlags.IsEvil | CreatureFlags.CanBePickedUp | CreatureFlags.CanBeSlapped
                | CreatureFlags.CanWalkOnWater, hp: 1500, threat: FighterThreat, JobClass.Fighter, FightStyle.Blocker),
            Creature("Sleeper", SleeperCreature, CreatureFlags.IsEvil | CreatureFlags.CanBePickedUp, hp: 500, threat: 2,
                JobClass.Fighter, FightStyle.Blocker) with { LairObjectId = BedObjectId, TimeSleep = 9 },
            Creature("Eater", EaterCreature, CreatureFlags.IsEvil | CreatureFlags.CanBePickedUp, hp: 500, threat: 2,
                JobClass.Fighter, FightStyle.Blocker) with { HungerFill = 1, HpFromChicken = 100 },
            Creature("Moody", MoodyCreature, CreatureFlags.IsEvil | CreatureFlags.CanBePickedUp, hp: 500, threat: 2,
                JobClass.Fighter, FightStyle.Blocker) with
            {
                Pay = 100, HungerFill = 1,
                HappyJobs = [new(JobType.Research, 0, 0, 50), new(JobType.Train, 0, 0, 50)],
            },
        ],
        variables: Variables(handCapacity),
        keeperSpells: KeeperSpells(),
        objects: [Flag(), TrainingDummy(), Bed(), FloorGoldPile(), Hatchery(HatcheryRules.CoopObjectId, 0x00004000u, 0, 3252),
            Hatchery(HatcheryRules.EggObjectId, 0x00018602u, 0, 128), Hatchery(HatcheryRules.ChickenObjectId, 0x00230782u, 1, 128)],
        shots:
        [
            Shot("Create Object", CreateObjectShotId, 2048, 1, 4, 0x80, ShotRules.CreateObjectProcess),
            Shot("Lightning Shot", LightningShotId, 0, 8, 0x104, 0x40, ShotRules.LightningProcess),
            Shot("Modify Health", ModifyHealthShotId, 3686, 128, 0x23, 0x4, ShotRules.ModifyHealthProcess, collide: 3),
            Shot("Chicken", ChickenShotId, 2048, 16, 0x6, 0x4, ShotRules.ChickenProcess, collide: 2),
            Shot("Inferno", InfernoShotId, 2048, 30, 0x104, 0x2, ShotRules.InfernoProcess),
            Shot("Sight Of Evil", SightShotId, 2048, 64, 0x4, 0x42, SightProcess),
        ]);
    }

    /// <summary>Spells 1 to 11 and 13 with the shipped ids, costs, flags and selectors, so the battle
    /// step's ascending walk over the descriptor count meets what DK2's meets: Thunderbolt (6),
    /// Inferno (9) and Chicken (11) are offensive, Heal (10) supportive, the rest excluded; 7 stands
    /// in for an offensive spell with no effect built, which shipped Turncoat is not.</summary>
    private static KeeperSpellDef[] KeeperSpells() =>
    [
        Spell("Sight Of Evil", KeeperSpellRules.SightOfEvil, 5000, KeeperSpellFlags.ExcludedFromBattle, 5, 4, SightShotId, data1: 5),
        Spell("Possession", 2, 500, KeeperSpellFlags.ExcludedFromBattle, 5, 6),
        new()
        {
            Name = KeeperSpellName.CallToArms, KeeperSpellId = CallToArmsRules.SpellId, ManaCost = 10000, ManaUpkeepPerSecond = 90,
            Flags = KeeperSpellFlags.ExcludedFromBattle, LocationSelector = 4, TargetSelector = 4,
            ShotTypeId = CreateObjectShotId, ShotData1 = CallToArmsRules.ObjectTypeId,
            BonusShotTypeId = CreateObjectShotId, BonusShotData1 = CallToArmsRules.ObjectTypeId,
        },
        new() { Name = KeeperSpellName.CreateImp, KeeperSpellId = 4, ManaCost = 1500, ManaUpkeepPerSecond = 7, Flags = KeeperSpellFlags.ExcludedFromBattle },
        Spell("Summon Horny", 5, 100000, KeeperSpellFlags.ExcludedFromBattle, 1, 4),
        Spell("Thunderbolt", KeeperSpellRules.Thunderbolt, 6000, KeeperSpellFlags.Offensive, 1, 2, LightningShotId),
        Spell("Unbuilt", 7, 10000, KeeperSpellFlags.Offensive, 1, 3),
        Spell("Tremor", 8, 30000, KeeperSpellFlags.Offensive | KeeperSpellFlags.ExcludedFromBattle, 5, 4),
        Spell("Inferno", KeeperSpellRules.Inferno, 50000, KeeperSpellFlags.Offensive, 1, 5, InfernoShotId, data1: 100, data2: 1),
        new()
        {
            Name = "Heal", KeeperSpellId = KeeperSpellRules.Heal, ManaCost = 5000, ManaUpkeepPerSecond = 0,
            Flags = KeeperSpellFlags.Supportive, LocationSelector = 4, TargetSelector = 1,
            ShotTypeId = ModifyHealthShotId, ShotData1 = 16000, BonusShotTypeId = ModifyHealthShotId, BonusShotData1 = 25000,
        },
        Spell("Chicken", KeeperSpellRules.Chicken, 10000, KeeperSpellFlags.Offensive, 1, 3, ChickenShotId, data1: 60),
        Spell("Create Gold", KeeperSpellRules.CreateGold, 15000, KeeperSpellFlags.ExcludedFromBattle, 1, 4, CreateObjectShotId,
            data1: FloorGold.FloorGoldObjectId, data2: 1000),
    ];

    /// <summary>Shots 10 and 15 as shipped: Chicken's process 15 on the thing struck, Inferno's process
    /// 30 before each move of its 30 ticks.</summary>
    public const byte ChickenShotId = 10, InfernoShotId = 15;

    /// <summary>Shot 33 as shipped: process 25, whose only effect is the local presentation region.</summary>
    public const byte SightShotId = 33, SightProcess = 25;

    /// <summary>Shot 4 as shipped: process 3 on the thing struck, dying on it, collide class 3.</summary>
    public const byte ModifyHealthShotId = 4;

    private static KeeperSpellDef Spell(string name, byte id, int cost, KeeperSpellFlags flags, byte location, byte target, byte shot = 0,
        int data1 = 0, int data2 = 0) =>
        new()
        {
            Name = name, KeeperSpellId = id, ManaCost = cost, ManaUpkeepPerSecond = 0, Flags = flags,
            LocationSelector = location, TargetSelector = target, ShotTypeId = shot, BonusShotTypeId = shot,
            ShotData1 = data1, ShotData2 = data2, BonusShotData1 = data1, BonusShotData2 = data2,
        };

    /// <summary>Object 14, the Training Room's floor furniture: a room's places are its furniture (ADR-0205).</summary>
    public const byte TrainingDummyObjectId = 14;

    private static ObjectDef TrainingDummy() => new()
    {
        Name = "Training Dummy", ObjectId = TrainingDummyObjectId, Flags = (ObjectFlags)0x02004000u, Hp = 1, ManaValue = 0,
        RoomCapacity = 0, StartState = 0, PickUpPriority = 176, Width = new Fp32(2297), Height = new Fp32(1228),
        Mass = new Fp32(8192), Speed = new Fp32(0), NameStringId = 0, TooltipStringId = 0,
    };

    /// <summary>A creature bed: <c>TypeLair</c> is the bit the Lair rules read (shipped flags 0x04034020).</summary>
    private static ObjectDef Bed() => new()
    {
        Name = "Creature Bed", ObjectId = BedObjectId, Flags = (ObjectFlags)0x04034020u, Hp = 128, ManaValue = 0,
        RoomCapacity = 1, StartState = 0, PickUpPriority = 176, Width = new Fp32(2048), Height = new Fp32(256),
        Mass = new Fp32(8192), Speed = new Fp32(0), NameStringId = 0, TooltipStringId = 0,
    };

    /// <summary>Object 1, "Gold": the floor pile a sale's refund lands in when no store takes it.</summary>
    private static ObjectDef FloorGoldPile() => new()
    {
        Name = "Gold", ObjectId = FloorGold.FloorGoldObjectId, Flags = (ObjectFlags)0x00810140u, Hp = 128, ManaValue = 0,
        RoomCapacity = 0, StartState = 31, PickUpPriority = 176, Width = new Fp32(256), Height = new Fp32(1228),
        Mass = new Fp32(16384), Speed = new Fp32(0), NameStringId = 292, TooltipStringId = 292,
    };

    /// <summary>The coop, egg and chicken with their shipped flags, capacity and width.</summary>
    private static ObjectDef Hatchery(byte id, uint flags, byte capacity, int width) => new()
    {
        Name = $"Hatchery object {id}", ObjectId = id, Flags = (ObjectFlags)flags, Hp = 128, ManaValue = 0,
        RoomCapacity = capacity, StartState = 0, PickUpPriority = 176, Width = new Fp32(width), Height = new Fp32(128),
        Mass = new Fp32(8192), Speed = new Fp32(0), NameStringId = 0, TooltipStringId = 0,
    };

    /// <summary>Stands each room's auto-placed furniture, as a level's rooms come furnished.</summary>
    public static void FurnishRooms(GameDatabase database, WorldState world)
    {
        var pieces = new List<(int TileIndex, byte ObjectTypeId)>();
        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            AutoFurnitureRules.DesiredFurniture(world, database, room, pieces);
            foreach ((int tile, byte type) in pieces)
                if (database.ObjectById(type) is not null)
                    world.SpawnObject(database, type, room.OwnerPlayerId, tile).AutoPlaced = true;
        }
    }

    /// <summary>The Call to Arms flag, OBJECTS record 10 as shipped: start state 12, 128 health.</summary>
    private static ObjectDef Flag() => new()
    {
        Name = "Call To Arms", ObjectId = CallToArmsRules.ObjectTypeId,
        Flags = ObjectFlags.CanBePickedUp | ObjectFlags.CanBeSlapped | ObjectFlags.CanBeDroppedOnAnyLand,
        Hp = 128, ManaValue = 0, RoomCapacity = 1, StartState = 12, PickUpPriority = 176,
        Width = new Fp32(1704), Height = new Fp32(127), Mass = new Fp32(4), Speed = new Fp32(0),
        NameStringId = 2725, TooltipStringId = 2726,
    };

    private static ShotDef Shot(string name, byte id, int speed, ushort health, uint flags, uint processFlags, byte process, byte collide = 0) =>
        new(new byte[239])
        {
            Name = name, MeshResource = new KwdArtResource("", 0, default, 0, 0, 0, 0), Light = new KwdLight(0, 0, 0, 0, 0, 0, 0, 0, 0),
            SoundCategory = "", ShotId = id, Speed = speed, Radius = 819, Health = health, Flags = flags,
            ShotProcessFlags = processFlags, CollideType = collide, ProcessType = process, DamageType = 0, AttackCategory = 12, DeathShotId = 0,
        };

    /// <summary>Gormagon-shaped parameters, trimmed to what step 1 reads.</summary>
    public static KeeperAIParameters Parameters(byte speed = 1, byte roomSize = 9, ushort money = 1000, byte maxImps = 0, byte gap = 0,
        bool withdraw = false, byte imprison = 0)
    {
        return new()
        {
            EditorAiType = 0,
            TendancySpeed = speed,
            Openness = 20,
            RemoveCallToArmsOwnCreatures = 0,
            BuildLostRoomAfterMinimumTime = 0,
            CreateEmptyRoomShapeAreasWhenIdle = false,
            AutomaticallyBuildBiggerLairWhenPortalTakenOver = true,
            SellRecentlyTakenOverRoom = false,
            MinimumRandomTimeBeforePlacingReseachedRoom = 0,
            DefaultRoomSize = roomSize,
            TilesLeftBetweenRooms = gap,
            PlaceRoomsNextToIdeal = false,
            CorridorStyle = 0,
            BuildNewRoomWhenMoreSpaceRequired = 1,
            NeutralRoomRangeFromHeart = 10,
            RoomList = [1, 2, 4, 3, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16],
            Flexibility = 0,
            NeutralRoomRangeFromClaimedArea = 2,
            RemoveCallToArmsAfter = 250,
            BoulderTrapsOnLongCorridors = false,
            BoulderTrapOnBreachRoute = false,
            AggressiveVsDefensiveTrapPreference = 2,
            TrapVsDoorPreference = 2,
            DoorPreference = 0,
            TrapAndDoorUsage = 0,
            IAAllRoomsPlaced = false,
            IAAllSpellsResearched = 0,
            IAOnlyAttackAttackers = false,
            NeverAttack = true,
            IACreatureSkillLevel = 1,
            IAThreatSuperiority = 100,
            IAFirstEnd = 0,
            IAFirstStart = 0,
            BreachAtXPoints = 1,
            IAUseCreatures = 0,
            ManaValue = 100,
            BACallToArmsValue = 250,
            RemoveCallToArmsEnemyCreatures = 2,
            RemoveCallToArmsWithinRange = 5,
            BARemoveIfCantReinforce = withdraw,
            BAThreatSuperiority = 100,
            AttackingVsDefensiveSpellPreference = 2,
            ImprisonPercentage = imprison,
            BARetreatHealth = 20,
            GoldValue = 100,
            UnhappyCreaturesHappy = true,
            AngryCreaturesHappy = true,
            AngryCreaturesDisposeOf = false,
            UnwantedCreatureDisposeOf = false,
            AngryDisposalMethod = 0,
            MaximumImps = maxImps,
            Flags = 0xFF,
            IAMinimumCreatures = 3,
            LightningInWater = false,
            UseSightOfEvil = 0,
            UseKeeperSpells = 0,
            SpellPreference = 2,
            CallToArmsUsage = 0,
            TendancyMoney = money,
            WaitBeforeAttackingAgain = 30,
            StartingMana = 100,
            SpecialRange = 5,
            TilesPerImpRatio = 40,
            MaxBuildAreaMinX = 0,
            MaxBuildAreaMinY = 0,
            MaxBuildAreaMaxX = 29,
            MaxBuildAreaMaxY = 29,
            ProbabilityOfMovingCreatureForResearch = 0,
            SpecialSearchChance = 0,
            SpecialFindChance = 0,
            ImprisonedCreatureAction = 1,
            RawBlock = new byte[158],
        };
    }

    /// <summary>A 30×30 rock map: impenetrable border, the keeper's heart and a 5×5 claimed patch around it.</summary>
    public static WorldState World(GameDatabase database, int width = 30, int height = 30, IEnumerable<byte>? morePlayers = null)
    {
        var tiles = new MapTile[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                bool border = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                tiles[(y * width) + x] = new MapTile(border ? Impenetrable : Rock, 0, 0, 0);
            }

        for (int y = HeartAt.Y - 2; y <= HeartAt.Y + 2; y++)
            for (int x = HeartAt.X - 2; x <= HeartAt.X + 2; x++)
                tiles[(y * width) + x] = new MapTile(ClaimedPath, Keeper, 0, 0);
        tiles[(HeartAt.Y * width) + HeartAt.X] = new MapTile(HeartFloor, Keeper, 0, 0);

        WorldState world = WorldState.FromMap(database, new LevelMap(width, height, tiles), [0, 1, 2, Keeper, .. morePlayers ?? []]);
        // Mana enough for the imps the tests ask for: DK2's imp cast tests its cost first.
        world.Player(Keeper).Mana = 100000;
        return world;
    }

    public static void Set(WorldState world, GameDatabase database, TilePos p, byte terrainId, byte owner = 0)
    {
        ref TileState tile = ref world.Tile(p);
        tile.TerrainId = terrainId;
        tile.OwnerPlayerId = owner;
        tile.Health = database.TerrainById(terrainId).StartingHealth;
        tile.TaggedBy = PlayerMask.None;
    }

    /// <summary>
    /// Stands in for the imps: every tile the keeper has tagged becomes the
    /// keeper's claimed floor at once. Lets the keeper's own loop be tested
    /// without depending on creature pathing.
    /// </summary>
    public static int DigWhatIsTagged(WorldState world, GameDatabase database, byte playerId)
    {
        int dug = 0;
        for (int i = 0; i < world.TileCount; i++)
        {
            ref TileState tile = ref world.TileAtIndex(i);
            if (!tile.IsTaggedBy(playerId)) continue;
            if (!database.TerrainById(tile.TerrainId).IsSolid) continue;
            tile.TerrainId = ClaimedPath;
            tile.OwnerPlayerId = playerId;
            tile.Health = database.TerrainById(ClaimedPath).StartingHealth;
            tile.TaggedBy = PlayerMask.None;
            dug++;
        }
        return dug;
    }

    private static GlobalVariables Variables(int handCapacity)
    {
        var vars = new GlobalVariables();
        foreach (RoomTypeId room in new[] { RoomTypeId.Treasury, RoomTypeId.Lair, RoomTypeId.Hatchery })
        {
            vars.Availabilities.Add(new AvailabilityEntry(1, Keeper, (int)room, 3));
        }

        // Both bridges present but unavailable until a test opens them.
        vars.Availabilities.Add(new AvailabilityEntry(1, Keeper, (int)RoomTypeId.WoodenBridge, 1));
        vars.Availabilities.Add(new AvailabilityEntry(1, Keeper, (int)RoomTypeId.StoneBridge, 1));
        vars.Availabilities.Add(new AvailabilityEntry(5, Keeper, 4, 3));
        // Rows for the battle spells, unavailable until a test says otherwise.
        foreach (int spell in new[] { KeeperSpellRules.SightOfEvil, 3, 6, 7, 9, 10, 11, KeeperSpellRules.CreateGold })
            vars.Availabilities.Add(new AvailabilityEntry(5, Keeper, spell, 1));
        vars.Misc[VariableTypeId.MaximumManaThreshold] = new MiscVariable(VariableTypeId.MaximumManaThreshold, 200000, 0, 0);
        vars.Misc[VariableTypeId.MinimumImpThreshold] = new MiscVariable(VariableTypeId.MinimumImpThreshold, 4, 0, 0);
        // The heart's health and regeneration, as the shipped global VARIABLES table
        // carries them (rows 174 and 131); a keeper's world needs both (ADR-0162).
        vars.Misc[VariableTypeId.DungeonHeartObjectHealth] = new MiscVariable(VariableTypeId.DungeonHeartObjectHealth, 100000, 0, 0);
        vars.Misc[VariableTypeId.DungeonHeartHealthRegenerationPerSecond] = new MiscVariable(VariableTypeId.DungeonHeartHealthRegenerationPerSecond, 25, 0, 0);
        // What the hand and a fight read once creatures exist, at the shipped values.
        Set(vars, VariableTypeId.MaxNumberOfThingsInHand, handCapacity);
        Set(vars, VariableTypeId.ChickenGenerationTimePerHatchery, 16);
        Set(vars, VariableTypeId.MaxFreeRangeChickensPerPlayer, 12);
        Set(vars, VariableTypeId.CreatureDyingStateDurationSeconds, 60);
        Set(vars, VariableTypeId.BackOffMinimumDurationSeconds, 0);
        Set(vars, VariableTypeId.BackOffMaximumDurationSeconds, 30);
        Set(vars, VariableTypeId.GuardRoomReportingDistanceTiles, 7);
        Set(vars, VariableTypeId.PitPercentageDamageTakenOfNormalCombat, 20);
        Set(vars, VariableTypeId.GoodCreaturesChaseEnemyDurationSeconds, 3);
        Set(vars, VariableTypeId.EvilCreaturesChaseEnemyDurationSeconds, 7);
        Set(vars, VariableTypeId.CallToArmsFightDistanceTiles, 5);
        Set(vars, VariableTypeId.UpgradedCallToArmsCostPercentage, 70);
        // The training moves' cap and what the Training Room and Combat Pit read, as shipped.
        Set(vars, VariableTypeId.TrainingRoomMaxExperienceLevel, 4);
        Set(vars, VariableTypeId.CombatPitMaxExperienceLevel, 8);
        Set(vars, VariableTypeId.EPLossSecondWhileNotTraining, 0);
        Set(vars, VariableTypeId.ModifyAngerOfPitSpectatorPerSecond, -275);
        // What a store holds and a sale refunds, as shipped: the room messages' producers read them.
        Set(vars, VariableTypeId.MaxGoldPerTreasuryTile, 3000);
        Set(vars, VariableTypeId.MaxGoldPerDungeonHeartTile, 1000);
        Set(vars, VariableTypeId.MaxGoldPileOutsideTreasury, 2000);
        Set(vars, VariableTypeId.RoomSellValuePercentageOfCost, 50);
        Set(vars, VariableTypeId.CreatureSleepsWhenBelowPercentHealth, 50);
        Set(vars, VariableTypeId.ModifyPlayerColdWhileTrainingPerSecond, -5);
        // The shipped level rows for health and the experience threshold, levels 2 to 10
        // (creature-levels.md §2), so a level-scaled rule differs from its record value.
        int[] health = [125, 150, 175, 200, 225, 250, 300, 400, 600];
        int[] experience = [130, 180, 250, 350, 470, 600, 720, 850, 1000];
        for (int level = 2; level <= 10; level++)
        {
            vars.CreatureStats.Add(new CreatureStatEntry((int)CreatureStatId.Health, health[level - 2], level, false));
            vars.CreatureStats.Add(new CreatureStatEntry((int)CreatureStatId.ExperienceForNextLevel, experience[level - 2], level, false));
        }
        return vars;
    }

    private static void Set(GlobalVariables vars, VariableTypeId id, int value)
    {
        vars.Misc[id] = new MiscVariable(id, value, 0, 0);
    }

    /// <summary>A creature record with the fixture's own combat numbers and the Imp fixture's
    /// values for every field no test here reads.</summary>
    private static CreatureDef Creature(string name, byte id, CreatureFlags flags, ushort hp, ushort threat,
        JobClass job, FightStyle style)
    {
        return new()
        {
            Name = name,
            CreatureId = id,
            Flags = flags,
            Speed = new Fp32(2867),
            RunSpeed = new Fp32(2867),
            ShuffleSpeed = new Fp32(4096),
            Height = new Fp32(2048),
            EyeHeight = new Fp32(0),
            DistanceCanSee = new Fp32(16384),
            DistanceCanHear = new Fp32(16384),
            PerceptionRange = new Fp32(0x6000),
            FootprintRadius = new Fp32(651),
            SizeClass = 2,
            TurnRate = 2048,
            Hp = hp,
            HpFromChicken = 0,
            HungerRate = new Fp32(134213632),
            HungerFill = 0,
            TimeAwake = 32767,
            TimeSleep = 0,
            OwnLandHealthIncreasePerSecond = 0,
            DecomposeValue = 0,
            Fear = 50,
            Threat = threat,
            MeleeDamage = 170,
            SlapDamage = 115,
            MeleeRange = new Fp32(1433),
            MeleeRecharge = Fp32.FromDouble(1.3),
            MeleeHitDelay = Fp32.FromDouble(0.75),
            MeleeAttackType = AttackType.MeleeBlade,
            StunDuration = Fp32.FromDouble(5),
            FightStyle = style,
            Resistances = [],
            Spells = [],
            Pay = 0,
            MaxGoldHeld = 0,
            InitialGoldHeld = 0,
            ExpForNextLevel = 3000,
            ExpPerSecond = 200,
            ExpPerSecondTraining = 40,
            UnhappyThreshold = 50,
            JobClass = job,
            ResearchPerSecond = 0,
            ManufacturePerSecond = 0,
            ManaGeneratedByPrayerPerSecond = 0,
            AngerNoLair = 1,
            AngerNoFood = 1,
            AngerNoPay = 1,
            AngerNoWork = 1,
            AngerSlap = 1,
            AngerInHand = 0,
            TortureHpChange = 0,
            TortureMoodChange = 0,
            TortureTimeToConvert = new Fp32(0),
            HappyJobs = [],
            UnhappyJobs = [],
            AngryJobs = [],
            HateJobs = [],
            AlternativeJobs = [],
            Attractions = [],
            GuardDuration = new Fp32(0),
            IdleDuration = new Fp32(0),
            SlapFearlessDuration = new Fp32(0),
            PossessionManaCost = 0,
            LairObjectId = 0,
            CloneCreatureId = 0,
            NameStringId = 0,
            TooltipStringId = 0,
            IntroductionStringId = 0,
        };
    }

    private static TerrainDef Terrain(
        byte id, string name, TerrainFlags flags, ushort health,
        byte maxHealthType = 0, byte destroyedType = 0, ushort gold = 0)
    {
        return new()
        {
            Name = name,
            TerrainId = id,
            Flags = flags,
            StartingHealth = health,
            MaxHealth = health,
            GoldValue = gold,
            ManaGain = 0,
            MaxManaGain = 0,
            Damage = 0,
            MaxHealthTypeTerrainId = maxHealthType,
            DestroyedTypeTerrainId = destroyedType,
            Depth = new Fp32(0),
            NameStringId = 0,
            TooltipStringId = 0,
        };
    }

    private const RoomFlags BridgeFlags = RoomFlags.PlaceableOnWater | RoomFlags.PlaceableOnLava;

    private static RoomDef Room(RoomTypeId id, string name, ushort cost, byte terrainId, bool buildable,
        RoomFlags ground = RoomFlags.PlaceableOnLand | RoomFlags.NormalTiles, byte[]? objectIds = null)
    {
        return new()
        {
            Name = name,
            RoomId = (byte)id,
            Flags = ground | (buildable ? RoomFlags.Buildable : 0),
            Cost = cost,
            ReturnPercentage = 50,
            TerrainId = terrainId,
            Construction = id == RoomTypeId.Portal ? TileConstruction.ThreeByThree : TileConstruction.Normal,
            CreatedCreatureId = 0,
            ObjectIds = objectIds ?? [],
            HealthGain = 0,
            CeilingHeight = new Fp32(0),
            RecommendedSizeX = 1,
            RecommendedSizeY = 1,
            OrderInEditor = 0,
            NameStringId = 0,
            TooltipStringId = 0,
        };
    }
}
