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

    /// <summary>Shot 5, Create Object, and a Lightning shot for Thunderbolt; the flag is object 10.</summary>
    public const byte CreateObjectShotId = 5;
    public const byte LightningShotId = 30;

    /// <summary>A worker and a fighter, so the keeper has something to move; a hero owns the
    /// same fighter record when it attacks.</summary>
    public const byte WorkerCreature = 1;
    public const byte FighterCreature = 2;
    public const ushort FighterThreat = 50;

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
            Terrain(TreasuryFloor, "Treasury", TerrainFlags.Ownable | TerrainFlags.Room, 100),
            Terrain(LairFloor, "Lair", TerrainFlags.Ownable | TerrainFlags.Room, 100),
            Terrain(HatcheryFloor, "Hatchery", TerrainFlags.Ownable | TerrainFlags.Room, 100),
            Terrain(HeroPortalFloor, "Hero Portal", TerrainFlags.Ownable | TerrainFlags.Room, 100),
            Terrain(PortalFloor, "Portal", TerrainFlags.Ownable | TerrainFlags.Room, 100),
            Terrain(HeartFloor, "Dungeon Heart", TerrainFlags.Ownable | TerrainFlags.Room, 100),
            Terrain(PrisonFloor, "Prison", TerrainFlags.Ownable | TerrainFlags.Room, 100),
            Terrain(LibraryFloor, "Library", TerrainFlags.Ownable | TerrainFlags.Room, 100),
            Terrain(Lava, "Lava", TerrainFlags.Lava, 0),
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
        ],
        creatures:
        [
            Creature("Worker", WorkerCreature, CreatureFlags.IsWorker | CreatureFlags.IsEvil | CreatureFlags.CanBePickedUp
                | CreatureFlags.CanBeSlapped, hp: 500, threat: 2, JobClass.Scout, FightStyle.NonFighter),
            Creature("Fighter", FighterCreature, CreatureFlags.IsEvil | CreatureFlags.CanBePickedUp | CreatureFlags.CanBeSlapped
                | CreatureFlags.CanWalkOnWater, hp: 1500, threat: FighterThreat, JobClass.Fighter, FightStyle.Blocker),
        ],
        variables: Variables(handCapacity),
        keeperSpells: KeeperSpells(),
        objects: [Flag()],
        shots:
        [
            Shot("Create Object", CreateObjectShotId, 2048, 1, 4, 0x80, ShotRules.CreateObjectProcess),
            Shot("Lightning Shot", LightningShotId, 0, 8, 0x104, 0x40, ShotRules.LightningProcess),
            Shot("Modify Health", ModifyHealthShotId, 3686, 128, 0x23, 0x4, ShotRules.ModifyHealthProcess, collide: 3),
        ]);
    }

    /// <summary>Spells 1 to 10 with the shipped ids, costs, flags and selectors, so the battle
    /// step's ascending walk over the descriptor count meets what DK2's meets: Thunderbolt (6),
    /// Inferno (9) and a Chicken-shaped 7 are offensive, Heal (10) supportive, the rest excluded.</summary>
    private static KeeperSpellDef[] KeeperSpells() =>
    [
        Spell("Sight Of Evil", 1, 5000, KeeperSpellFlags.ExcludedFromBattle, 5, 4),
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
        Spell("Chicken", 7, 10000, KeeperSpellFlags.Offensive, 1, 3),
        Spell("Tremor", 8, 30000, KeeperSpellFlags.Offensive | KeeperSpellFlags.ExcludedFromBattle, 5, 4),
        Spell("Inferno", 9, 50000, KeeperSpellFlags.Offensive, 1, 5),
        new()
        {
            Name = "Heal", KeeperSpellId = KeeperSpellRules.Heal, ManaCost = 5000, ManaUpkeepPerSecond = 0,
            Flags = KeeperSpellFlags.Supportive, LocationSelector = 4, TargetSelector = 1,
            ShotTypeId = ModifyHealthShotId, ShotData1 = 16000, BonusShotTypeId = ModifyHealthShotId, BonusShotData1 = 25000,
        },
    ];

    /// <summary>Shot 4 as shipped: process 3 on the thing struck, dying on it, collide class 3.</summary>
    public const byte ModifyHealthShotId = 4;

    private static KeeperSpellDef Spell(string name, byte id, int cost, KeeperSpellFlags flags, byte location, byte target, byte shot = 0) =>
        new()
        {
            Name = name, KeeperSpellId = id, ManaCost = cost, ManaUpkeepPerSecond = 0, Flags = flags,
            LocationSelector = location, TargetSelector = target, ShotTypeId = shot, BonusShotTypeId = shot,
        };

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
        tile.TaggedBy = 0;
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
            tile.TaggedBy = 0;
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

        vars.Availabilities.Add(new AvailabilityEntry(5, Keeper, 4, 3));
        // Rows for the battle spells, unavailable until a test says otherwise.
        foreach (int spell in new[] { 3, 6, 7, 9, 10 })
            vars.Availabilities.Add(new AvailabilityEntry(5, Keeper, spell, 1));
        vars.Misc[VariableTypeId.MaximumManaThreshold] = new MiscVariable(VariableTypeId.MaximumManaThreshold, 200000, 0, 0);
        vars.Misc[VariableTypeId.MinimumImpThreshold] = new MiscVariable(VariableTypeId.MinimumImpThreshold, 4, 0, 0);
        // The heart's health and regeneration, as the shipped global VARIABLES table
        // carries them (rows 174 and 131); a keeper's world needs both (ADR-0162).
        vars.Misc[VariableTypeId.DungeonHeartObjectHealth] = new MiscVariable(VariableTypeId.DungeonHeartObjectHealth, 100000, 0, 0);
        vars.Misc[VariableTypeId.DungeonHeartHealthRegenerationPerSecond] = new MiscVariable(VariableTypeId.DungeonHeartHealthRegenerationPerSecond, 25, 0, 0);
        // What the hand and a fight read once creatures exist, at the shipped values.
        Set(vars, VariableTypeId.MaxNumberOfThingsInHand, handCapacity);
        Set(vars, VariableTypeId.CreatureDyingStateDurationSeconds, 60);
        Set(vars, VariableTypeId.BackOffMinimumDurationSeconds, 0);
        Set(vars, VariableTypeId.BackOffMaximumDurationSeconds, 30);
        Set(vars, VariableTypeId.GuardRoomReportingDistanceTiles, 7);
        Set(vars, VariableTypeId.PitPercentageDamageTakenOfNormalCombat, 20);
        Set(vars, VariableTypeId.GoodCreaturesChaseEnemyDurationSeconds, 3);
        Set(vars, VariableTypeId.EvilCreaturesChaseEnemyDurationSeconds, 7);
        Set(vars, VariableTypeId.CallToArmsFightDistanceTiles, 5);
        Set(vars, VariableTypeId.UpgradedCallToArmsCostPercentage, 70);
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

    private static RoomDef Room(RoomTypeId id, string name, ushort cost, byte terrainId, bool buildable)
    {
        return new()
        {
            Name = name,
            RoomId = (byte)id,
            Flags = RoomFlags.PlaceableOnLand | RoomFlags.NormalTiles | (buildable ? RoomFlags.Buildable : 0),
            Cost = cost,
            ReturnPercentage = 50,
            TerrainId = terrainId,
            Construction = id == RoomTypeId.Portal ? TileConstruction.ThreeByThree : TileConstruction.Normal,
            CreatedCreatureId = 0,
            ObjectIds = [],
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
