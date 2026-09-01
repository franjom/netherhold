using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>
/// The fourteen terrain types, four rooms, eighteen variables and one creature
/// that the M3 mechanics touch, transcribed from the reference install so they
/// can be tested where no Dungeon Keeper 2 installation exists — CI included.
///
/// Unlike <see cref="TestWorld"/>, nothing here is invented: every number is
/// DK2's own, extracted by <c>dk2tools</c> and recorded in
/// docs/research/terrain-room-dump.md, game-database-dump.md and imp-tasks.md.
/// That makes the fixture a liability as well as a convenience — a
/// transcription can drift from the game it claims to copy — so
/// <c>DK2.Behavior.Tests.TileWorkOnRealDataTests</c> and
/// <c>ImpsOnRealDataTests</c> re-run the same assertions against the real
/// install whenever one is present. That guard has already earned its keep: the
/// Imp's hunger rate was first transcribed from a lossy decimal and was wrong by
/// a factor of 4096.
/// </summary>
internal static class Dk2TerrainFixture
{
    public const byte ImpenetrableRock = 1;
    public const byte Rock = 2;
    public const byte DirtPath = 3;
    public const byte Water = 4;
    public const byte Gold = 6;
    public const byte Gems = 7;
    public const byte ClaimedPath = 8;
    public const byte ReinforcedWall = 9;
    public const byte TreasuryTerrain = 10;
    public const byte LairTerrain = 11;
    public const byte DungeonHeartTerrain = 14;
    public const byte WoodenBridgeTerrain = 17;
    public const byte ManaVaultTerrain = 31;
    public const byte ClaimedVaultTerrain = 32;

    public static GameDatabase Database() => new(
        [
            T(ImpenetrableRock, "Impenetrable Rock",
              TerrainFlags.Solid | TerrainFlags.Impenetrable, 1, 1, 1, 1),
            T(Rock, "Rock",
              TerrainFlags.Solid | TerrainFlags.Taggable, 165, 660, ReinforcedWall, DirtPath),
            T(DirtPath, "Dirt Path",
              TerrainFlags.FillInable, 1, 660, ClaimedPath, DirtPath),
            T(Water, "Water",
              TerrainFlags.Impenetrable | TerrainFlags.Water, 1, 1, Water, Water, damage: 100),
            T(Gold, "Gold",
              TerrainFlags.Solid | TerrainFlags.Taggable, 660, 660, Gold, DirtPath, goldValue: 3000),
            T(Gems, "Gems",
              TerrainFlags.Solid | TerrainFlags.Impenetrable | TerrainFlags.Taggable,
              100, 100, Gems, Gems, goldValue: 3000),
            T(ClaimedPath, "Claimed Path",
              TerrainFlags.Ownable | TerrainFlags.Attackable | TerrainFlags.FillInable,
              660, 660, Rock, DirtPath, manaGain: 1),
            T(ReinforcedWall, "Reinforced Wall",
              TerrainFlags.Solid | TerrainFlags.Ownable | TerrainFlags.Taggable | TerrainFlags.Attackable,
              900, 900, ReinforcedWall, Rock, manaGain: 1),
            T(TreasuryTerrain, "Treasury", RoomTerrain, 10000, 10000, TreasuryTerrain, ClaimedPath),
            T(LairTerrain, "Lair", RoomTerrain, 10000, 10000, LairTerrain, ClaimedPath),
            T(DungeonHeartTerrain, "Dungeon Heart",
              RoomTerrain, 10000, 10000, DungeonHeartTerrain, ClaimedPath),
            T(WoodenBridgeTerrain, "Wooden Bridge",
              RoomTerrain, 2000, 2000, WoodenBridgeTerrain, ClaimedPath),

            // The vault pair is here for the mana rules: the claimed vault is the
            // only terrain in the game worth more than one mana a second, and the
            // only one with a non-zero MaxManaGain (Q37). Its unclaimed form is
            // worth nothing, which is the whole point of claiming it.
            T(ManaVaultTerrain, "Mana Vault",
              TerrainFlags.FillInable, 1, 2000, ClaimedVaultTerrain, ManaVaultTerrain),
            T(ClaimedVaultTerrain, "Claimed Vault",
              TerrainFlags.Ownable | TerrainFlags.Attackable | TerrainFlags.FillInable,
              2000, 2000, ClaimedVaultTerrain, ManaVaultTerrain,
              manaGain: 100, maxManaGain: 100),
        ],
        rooms:
        [
            // Cost, terrain and placement flags exactly as the install has them
            // (dk2tools kwd rooms / kwd roomflags). The bridge is here because it
            // is the only shipped room that goes anywhere but land, which is what
            // makes the placeable-on flags worth reading rather than assuming.
            R(RoomTypeId.Treasury, "Treasury", 200, TreasuryTerrain, 0x128Cu),
            R(RoomTypeId.Lair, "Lair", 300, LairTerrain, 0x128Cu),
            R(RoomTypeId.DungeonHeart, "Dungeon Heart", 0, DungeonHeartTerrain, 0x204u, 5, 5),
            R(RoomTypeId.WoodenBridge, "Wooden Bridge", 200, WoodenBridgeTerrain, 0x1083u),
        ],
        creatures: [Imp()],
        variables: Variables(),
        keeperSpells: KeeperSpells());

    /// <summary>
    /// The keeper spells a registered system reads. Only Create Imp, because
    /// only Create Imp is read: <see cref="ImpSupplyRules"/> takes its cast cost
    /// and <see cref="ManaRules"/> takes the per-second upkeep every imp then
    /// charges its keeper.
    ///
    /// <para>Both numbers are DK2's own — 1500 to cast, 7 a second to keep — for
    /// the same reason the variables below are: a made-up figure here would be a
    /// made-up figure inside a determinism hash. `KeeperSpellsOnRealDataTests`
    /// reads both back out of the shipped file so these cannot drift from it.</para>
    /// </summary>
    private static KeeperSpellDef[] KeeperSpells() =>
    [
        new() { Name = KeeperSpellName.CreateImp, ManaCost = 1500, ManaUpkeepPerSecond = 7 },
    ];

    /// <summary>The flags every room terrain in the install carries: Ownable, Room, Attackable.</summary>
    private const TerrainFlags RoomTerrain =
        TerrainFlags.Ownable | TerrainFlags.Room | TerrainFlags.Attackable;

    public const byte ImpCreatureId = 1;

    /// <summary>
    /// The Imp, as the reference install has it.
    ///
    /// Only the fields listed here are transcribed; everything else is left at
    /// zero and is <b>not</b> a claim about the creature. The drift guard in
    /// <c>DK2.Behavior.Tests.ImpsOnRealDataTests</c> compares exactly these
    /// against the install, so a field added here without being checked there
    /// would be an invention nobody notices.
    /// </summary>
    public static CreatureDef Imp() => new()
    {
        Name = "Imp",
        CreatureId = ImpCreatureId,
        Flags = (CreatureFlags)0xB000089Fu,

        Speed = new Fp32(7536),
        RunSpeed = new Fp32(7536),
        ShuffleSpeed = new Fp32(4096),
        Height = new Fp32(2048),
        DistanceCanSee = new Fp32(16384),
        DistanceCanHear = new Fp32(16384),

        Hp = 500,
        HungerRate = new Fp32(134213632), // 32767 in DK2's fixed point: the "never" sentinel (Q12)
        TimeAwake = 32767,
        TimeSleep = 0,

        MeleeDamage = 30,
        MeleeRange = new Fp32(1433),
        MeleeRecharge = new Fp32(4915),
        MeleeAttackType = AttackType.MeleeBlade,

        MaxGoldHeld = 1400,
        InitialGoldHeld = 500,

        Pay = 0,
        Fear = 5,
        Threat = 2,
        ExpForNextLevel = 3000,
        ExpPerSecond = 1200,
        ExpPerSecondTraining = 30,
        UnhappyThreshold = 50,
        JobClass = JobClass.Scout,
        FightStyle = FightStyle.NonFighter,

        // Not transcribed. Present because the record requires them.
        EyeHeight = new Fp32(0),
        PerceptionRange = new Fp32(0),
        HpFromChicken = 0,
        HungerFill = 0,
        OwnLandHealthIncreasePerSecond = 0,
        DecomposeValue = 0,
        SlapDamage = 0,
        StunDuration = new Fp32(0),
        Resistances = [],
        Spells = [],
        ResearchPerSecond = 0,
        ManufacturePerSecond = 0,
        ManaGeneratedByPrayerPerSecond = 0,
        AngerNoLair = 0,
        AngerNoFood = 0,
        AngerNoPay = 0,
        AngerNoWork = 0,
        AngerSlap = 0,
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

    /// <summary>The ten tile-work health deltas, the gems yield, the imp idle delay,
    /// the three gold-per-tile limits and the three mana rates, as shipped.</summary>
    public static GlobalVariables Variables()
    {
        var vars = new GlobalVariables();
        Set(vars, VariableTypeId.ClaimTileHealth, 825);
        Set(vars, VariableTypeId.AttackTileHealth, -140);
        Set(vars, VariableTypeId.RepairTileHealth, 125);
        Set(vars, VariableTypeId.MineGoldHealth, -300);
        Set(vars, VariableTypeId.DigRockHealth, -5000);
        Set(vars, VariableTypeId.DigOwnWallHealth, -130);
        Set(vars, VariableTypeId.DigEnemyWallHealth, -8);
        Set(vars, VariableTypeId.FillInHealth, 5);
        Set(vars, VariableTypeId.ReinforceWallHealth, 50);
        Set(vars, VariableTypeId.RepairWallHealth, 22);
        Set(vars, VariableTypeId.GoldMinedFromGems, 25);
        Set(vars, VariableTypeId.ImpIdleDelayBeforeReevaluationSeconds, 5);
        Set(vars, VariableTypeId.MinimumImpThreshold, 4);
        Set(vars, VariableTypeId.TimeBeforeFreeImpGeneratedSeconds, 5);
        Set(vars, VariableTypeId.MaxGoldPerTreasuryTile, 3000);
        Set(vars, VariableTypeId.MaxGoldPerDungeonHeartTile, 1000);
        Set(vars, VariableTypeId.MaxGoldPileOutsideTreasury, 2000);
        Set(vars, VariableTypeId.MaximumManaThreshold, 200000);
        Set(vars, VariableTypeId.MaximumManaGainPerSecond, 500);
        Set(vars, VariableTypeId.ExcessManaDecreaseRatePerSecond, 5);
        Set(vars, VariableTypeId.DungeonHeartManaGenerationIncreasePerSecond, 30);
        return vars;
    }

    private static void Set(GlobalVariables vars, VariableTypeId id, int value) =>
        vars.Misc[id] = new MiscVariable(id, value, 0, 0);

    private static TerrainDef T(
        byte id, string name, TerrainFlags flags,
        ushort startingHealth, ushort maxHealth,
        byte maxHealthType, byte destroyedType,
        ushort goldValue = 0, ushort manaGain = 0, ushort maxManaGain = 0,
        short damage = 0) => new()
    {
        Name = name,
        TerrainId = id,
        Flags = flags,
        StartingHealth = startingHealth,
        MaxHealth = maxHealth,
        GoldValue = goldValue,
        ManaGain = manaGain,
        MaxManaGain = maxManaGain,
        Damage = damage,
        MaxHealthTypeTerrainId = maxHealthType,
        DestroyedTypeTerrainId = destroyedType,
        Depth = new Fp32(0),
        NameStringId = 0,
        TooltipStringId = 0,
    };

    /// <summary>
    /// One room record. Only the fields the room rules read are transcribed;
    /// the drift guard in <c>RoomsOnRealDataTests</c> checks exactly these.
    /// </summary>
    private static RoomDef R(
        RoomTypeId id, string name, ushort cost, byte terrainId, uint flags,
        byte sizeX = 1, byte sizeY = 1) => new()
    {
        Name = name,
        RoomId = (byte)id,
        Flags = (RoomFlags)flags,
        Cost = cost,
        TerrainId = terrainId,
        ReturnPercentage = 100,

        // Transcribed, because RoomInstance.IsFunctional reads them: the size
        // below which a room does nothing (Q45).
        RecommendedSizeX = sizeX,
        RecommendedSizeY = sizeY,

        // Not transcribed. Present because the record requires them.
        Construction = TileConstruction.Normal,
        CreatedCreatureId = 0,
        ObjectIds = [],
        HealthGain = 0,
        CeilingHeight = new Fp32(0),
        OrderInEditor = 0,
        NameStringId = 0,
        TooltipStringId = 0,
    };

    /// <summary>
    /// A small dungeon: impenetrable border, a rock body, a claimed-path room
    /// owned by player 1 at the top-left, one gold seam and one gem face.
    /// </summary>
    public static LevelMap Map(int width = 16, int height = 12)
    {
        var tiles = new MapTile[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool border = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                bool room = x is >= 1 and <= 3 && y is >= 1 and <= 3;

                byte terrain = border ? ImpenetrableRock : room ? ClaimedPath : Rock;
                byte owner = (byte)(room ? 1 : 0);
                tiles[(y * width) + x] = new MapTile(terrain, owner, 0, 0);
            }
        }

        tiles[(5 * width) + 5] = new MapTile(Gold, 0, 0, 0);
        tiles[(5 * width) + 6] = new MapTile(Gems, 0, 0, 0);
        return new LevelMap(width, height, tiles);
    }

    public static WorldState World() =>
        WorldState.FromMap(Database(), Map(), [(byte)0, (byte)1, (byte)2]);

    /// <summary>A simulation with the real M3 systems, in registration order.</summary>
    public static GameSimulation Simulation(ulong seed = 4242)
    {
        GameDatabase database = Database();
        return new GameSimulation(
            database,
            WorldState.FromMap(database, Map(), [(byte)0, (byte)1, (byte)2]),
            seed,
            DefaultSystems());
    }

    /// <summary>The one declared system order — see <see cref="Systems.SimulationSystems"/>.</summary>
    public static ISimulationSystem[] DefaultSystems() => Systems.SimulationSystems.Default();
}
