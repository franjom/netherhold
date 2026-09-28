// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

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
    public const byte HeartFloor = 20;

    public const byte Keeper = 3;
    public static readonly TilePos HeartAt = new(15, 15);

    public static GameDatabase Database() => new(
        [
            Terrain(Impenetrable, "Impenetrable Rock", TerrainFlags.Solid | TerrainFlags.Impenetrable, 0),
            Terrain(Rock, "Rock", TerrainFlags.Solid | TerrainFlags.Taggable, 165, destroyedType: DirtPath),
            Terrain(DirtPath, "Dirt Path", TerrainFlags.Ownable, 100, maxHealthType: ClaimedPath),
            Terrain(Gold, "Gold", TerrainFlags.Solid | TerrainFlags.Taggable, 300, destroyedType: DirtPath, gold: 1024),
            Terrain(ClaimedPath, "Claimed Path", TerrainFlags.Ownable, 100),
            Terrain(TreasuryFloor, "Treasury", TerrainFlags.Ownable | TerrainFlags.Room, 100),
            Terrain(LairFloor, "Lair", TerrainFlags.Ownable | TerrainFlags.Room, 100),
            Terrain(HatcheryFloor, "Hatchery", TerrainFlags.Ownable | TerrainFlags.Room, 100),
            Terrain(PortalFloor, "Portal", TerrainFlags.Ownable | TerrainFlags.Room, 100),
            Terrain(HeartFloor, "Dungeon Heart", TerrainFlags.Ownable | TerrainFlags.Room, 100),
        ],
        rooms:
        [
            Room(RoomTypeId.Treasury, "Treasury", 50, TreasuryFloor, buildable: true),
            Room(RoomTypeId.Lair, "Lair", 100, LairFloor, buildable: true),
            Room(RoomTypeId.Hatchery, "Hatchery", 125, HatcheryFloor, buildable: true),
            Room(RoomTypeId.Portal, "Portal", 0, PortalFloor, buildable: false),
            Room(RoomTypeId.DungeonHeart, "Dungeon Heart", 0, HeartFloor, buildable: false),
        ],
        creatures: [],
        variables: Variables(),
        keeperSpells: [new KeeperSpellDef { Name = KeeperSpellName.CreateImp, ManaCost = 1500, ManaUpkeepPerSecond = 7 }]);

    /// <summary>Gormagon-shaped parameters, trimmed to what step 1 reads.</summary>
    public static KeeperAIParameters Parameters(byte speed = 1, byte roomSize = 9, ushort money = 1000, byte maxImps = 0, byte gap = 0) => new()
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
        BARemoveIfCantReinforce = false,
        BAThreatSuperiority = 100,
        AttackingVsDefensiveSpellPreference = 2,
        ImprisonPercentage = 0,
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

    /// <summary>A 30×30 rock map: impenetrable border, the keeper's heart and a 5×5 claimed patch around it.</summary>
    public static WorldState World(GameDatabase database, int width = 30, int height = 30)
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

        return WorldState.FromMap(database, new LevelMap(width, height, tiles), [0, 1, 2, Keeper]);
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

    private static GlobalVariables Variables()
    {
        var vars = new GlobalVariables();
        foreach (RoomTypeId room in new[] { RoomTypeId.Treasury, RoomTypeId.Lair, RoomTypeId.Hatchery })
            vars.Availabilities.Add(new AvailabilityEntry(1, Keeper, (int)room, 3));
        vars.Availabilities.Add(new AvailabilityEntry(5, Keeper, 4, 3));
        vars.Misc[VariableTypeId.MaximumManaThreshold] = new MiscVariable(VariableTypeId.MaximumManaThreshold, 200000, 0, 0);
        vars.Misc[VariableTypeId.MinimumImpThreshold] = new MiscVariable(VariableTypeId.MinimumImpThreshold, 4, 0, 0);
        // The heart's health and regeneration, as the shipped global VARIABLES table
        // carries them (rows 174 and 131); a keeper's world needs both (ADR-0162).
        vars.Misc[VariableTypeId.DungeonHeartObjectHealth] = new MiscVariable(VariableTypeId.DungeonHeartObjectHealth, 100000, 0, 0);
        vars.Misc[VariableTypeId.DungeonHeartHealthRegenerationPerSecond] = new MiscVariable(VariableTypeId.DungeonHeartHealthRegenerationPerSecond, 25, 0, 0);
        return vars;
    }

    private static TerrainDef Terrain(
        byte id, string name, TerrainFlags flags, ushort health,
        byte maxHealthType = 0, byte destroyedType = 0, ushort gold = 0) => new()
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

    private static RoomDef Room(RoomTypeId id, string name, ushort cost, byte terrainId, bool buildable) => new()
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
