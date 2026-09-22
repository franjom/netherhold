// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>The terrain types, rooms, objects, variables and creatures the
/// built mechanics touch, transcribed from the reference install so they can be
/// tested where no Dungeon Keeper 2 installation exists — CI included.
/// </summary>
/// <remarks>
/// Unlike <see cref="TestWorld"/>, nothing here is invented: every number is
/// DK2's own, extracted by <c>dk2tools</c> and recorded in
/// <c>docs/research/terrain-room-dump.md</c>, <c>game-database-dump.md</c> and
/// <c>imp-tasks.md</c>. A transcription can drift from the game it claims to
/// copy, so the <c>*OnRealDataTests</c> in <c>DK2.Behavior.Tests</c> re-run the
/// same assertions against a real install whenever one is present.
/// </remarks>
internal static class Dk2TerrainFixture
{
    public const byte ImpenetrableRock = 1;
    public const byte Rock = 2;
    public const byte DirtPath = 3;
    public const byte Water = 4;
    public const byte Lava = 5;
    public const byte Gold = 6;
    public const byte Gems = 7;
    public const byte ClaimedPath = 8;
    public const byte ReinforcedWall = 9;
    public const byte TreasuryTerrain = 10;
    public const byte LairTerrain = 11;
    public const byte PortalTerrain = 12;
    public const byte HatcheryTerrain = 13;
    public const byte DungeonHeartTerrain = 14;
    public const byte WoodenBridgeTerrain = 17;
    public const byte GraveyardTerrain = 23;
    public const byte PrisonTerrain = 20;
    public const byte LibraryTerrain = 15;

    /// <summary>Terrain 16, and the room a content Goblin walks to (ADR-0082).</summary>
    public const byte TrainingRoomTerrain = 16;

    /// <summary>Terrain 22. The one room that takes anger back (ADR-0081).</summary>
    public const byte TempleTerrain = 22;

    /// <summary>Terrain 24, and where every unhappy creature in the game goes.</summary>
    public const byte CasinoTerrain = 24;
    public const byte ManaVaultTerrain = 31;
    public const byte ClaimedVaultTerrain = 32;

    /// <summary>Two shipped door records: the Wooden Door and the Barricade, whose flags byte
    /// (0x40) the navigation writer reads as a permanent lock.</summary>
    public static DoorDef[] Doors() =>
    [
        new() { Name = "Wooden Door", DoorId = 1, Health = 400, GoldCost = 500, ManufactureRequired = 1000 },
        new() { Name = "Barricade", DoorId = 6, Health = 800, GoldCost = 750, ManufactureRequired = 1000, Flags = DoorFlags.Barricade },
    ];

    public static GameDatabase Database(params CreatureDef[] extraCreatures) => new(
        [
            T(ImpenetrableRock, "Impenetrable Rock",
              TerrainFlags.Solid | TerrainFlags.Impenetrable, 1, 1, 1, 1),
            T(Rock, "Rock",
              TerrainFlags.Solid | TerrainFlags.Taggable, 165, 660, ReinforcedWall, DirtPath),
            T(DirtPath, "Dirt Path",
              TerrainFlags.FillInable, 1, 660, ClaimedPath, DirtPath),
            T(Water, "Water",
              TerrainFlags.Impenetrable | TerrainFlags.Water, 1, 1, Water, Water, damage: 100),

            // Present so the bridge rules can be tested both ways: a bridge over
            // lava must revert to lava and one over water to water, and a
            // fixture with only one hazard could not tell them apart (Q64).
            T(Lava, "Lava",
              TerrainFlags.Impenetrable | TerrainFlags.Lava, 1, 1, Lava, Lava, damage: 100),
            T(Gold, "Gold",
              TerrainFlags.Solid | TerrainFlags.Taggable, 660, 660, Gold, DirtPath, goldValue: 3000),
            T(Gems, "Gems",
              TerrainFlags.Solid | TerrainFlags.Impenetrable | TerrainFlags.Taggable,
              100, 100, Gems, Gems, goldValue: 3000),
            // The graveyard, for carrying (ADR-0063): terrain 23, 10000 health
            // both ways, destroyed-type 8 (Claimed Path), as the install has it.
            T(GraveyardTerrain, "Graveyard",
              TerrainFlags.Ownable, 10000, 10000, GraveyardTerrain, ClaimedPath),

            // The prison, terrain 20 in the shipped table, with the same
            // 10000/10000 health every room terrain carries.
            T(PrisonTerrain, "Prison",
              TerrainFlags.Ownable, 10000, 10000, PrisonTerrain, ClaimedPath),

            // The library, terrain 15. Present for one reason: it is the room
            // that owns an object, which is what kind 8 carries things to.
            T(LibraryTerrain, "Library",
              TerrainFlags.Ownable, 10000, 10000, LibraryTerrain, ClaimedPath),

            // The three rooms the job choosers walk to (Training Room, Temple,
            // Casino), from the shipped table (terrain-room-dump.md). With the
            // Library they cover every happy and unhappy job table the fixture's
            // creatures carry.
            T(TrainingRoomTerrain, "Training Room",
              TerrainFlags.Ownable, 10000, 10000, TrainingRoomTerrain, ClaimedPath),
            T(TempleTerrain, "Temple",
              TerrainFlags.Ownable, 10000, 10000, TempleTerrain, ClaimedPath),
            T(CasinoTerrain, "Casino",
              TerrainFlags.Ownable, 10000, 10000, CasinoTerrain, ClaimedPath),
            T(ClaimedPath, "Claimed Path",
              TerrainFlags.Ownable | TerrainFlags.Attackable | TerrainFlags.FillInable,
              660, 660, Rock, DirtPath, manaGain: 1),
            T(ReinforcedWall, "Reinforced Wall",
              TerrainFlags.Solid | TerrainFlags.Ownable | TerrainFlags.Taggable | TerrainFlags.Attackable,
              900, 900, ReinforcedWall, Rock, manaGain: 1),
            T(TreasuryTerrain, "Treasury", RoomTerrain, 10000, 10000, TreasuryTerrain, ClaimedPath),
            T(LairTerrain, "Lair", RoomTerrain, 10000, 10000, LairTerrain, ClaimedPath),
            T(PortalTerrain, "Portal", RoomTerrain, 10000, 10000, PortalTerrain, ClaimedPath),
            T(HatcheryTerrain, "Hatchery",
              RoomTerrain, 10000, 10000, HatcheryTerrain, ClaimedPath),
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
        doors: Doors(),
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

            // The portal, and its flags are the interesting part: 0x1004 is
            // PlaceableOnLand and HasFlame and nothing else — no Buildable, no
            // Attackable, no HasWalls. A keeper finds a portal and claims it
            // (manual §4.2.3), and the shipped record says so in three bits it
            // does not set. ThreeByThree because that construction is what
            // PortalRules.IsEntrance is really identifying.
            R(RoomTypeId.Portal, "Portal", 0, PortalTerrain, 0x1004u, 3, 3,
              TileConstruction.ThreeByThree),

            // The hatchery, for HatcheryRules. 300 to build and 3x3 recommended,
            // which is also the smallest room DK2 lets you make -- and the
            // number the laying curve is tuned around, since nine tiles is
            // exactly where its exponent gives 2 (hatchery.md section 4).
            R(RoomTypeId.Hatchery, "Hatchery", 300, HatcheryTerrain, 0x128Cu, 3, 3),

            // The graveyard: 2000 to build, 3x3 recommended, and the only room
            // besides the Portal whose CreatedCreatureId is set -- 8, the
            // Vampire. GraveyardRules reads this when the room's tally reaches
            // DecomposeValueNeededForVampire (Q165, ADR-0151).
            R(RoomTypeId.Graveyard, "Graveyard", 2000, GraveyardTerrain, 0x128Cu, 3, 3,
              createdCreatureId: VampireCreatureId),

            // The prison: 750 to build, 5x5 recommended, and the *other* room
            // in the shipped table whose CreatedCreatureId is set -- 9, the
            // Skeleton. Unlike the graveyard's vampire, this one is built: a
            // prisoner that withers away rises as one (ADR-0064).
            R(RoomTypeId.Prison, "Prison", 750, PrisonTerrain, 0x13ACu, 5, 5,
              createdCreatureId: SkeletonCreatureId),

            // The library: 600 to build, 3x3, and the shipped ObjectIds list
            // [64, 58] -- a book case and a library book. That list is the
            // whole reason this room is in the fixture: ObjectCarryRules reads
            // it backwards to find out where a loose object belongs (ADR-0065,
            // Q120).
            R(RoomTypeId.Library, "Library", 600, LibraryTerrain, 0x11ACu, 3, 3,
              objectIds: [BookCaseObjectId, LibraryBookObjectId]),

            // Costs, sizes and flags from the shipped table. The Temple's 5x5
            // DoubleQuad is transcribed rather than rounded off, because it is
            // the most expensive room in the game and the size is part of why.
            R(RoomTypeId.TrainingRoom, "Training Room", 500, TrainingRoomTerrain, 0x13ACu, 3, 3),
            R(RoomTypeId.Temple, "Temple", 3000, TempleTerrain, 0x108Cu, 5, 5,
              construction: TileConstruction.DoubleQuad),
            R(RoomTypeId.Casino, "Casino", 750, CasinoTerrain, 0x13ACu, 3, 3),
        ],
        creatures: [Imp(), Goblin(), Vampire(), Skeleton(), BileDemon(), .. extraCreatures],
        variables: Variables(),
        keeperSpells: KeeperSpells(),
        objects: Objects());

    /// <summary>Object id 22, the Goblin's bed.</summary>
    public const byte GoblinBedObjectId = 22;

    /// <summary>Object id 26, the Vampire's coffin.</summary>
    public const byte VampireBedObjectId = 26;

    /// <summary>Object id 64, the Library's book case — the fixture's loose object.</summary>
    public const byte BookCaseObjectId = 64;

    /// <summary>Object id 58, the Library's other object.</summary>
    public const byte LibraryBookObjectId = 58;

    /// <summary>Object id 66, placed at the centre of a sufficiently large Temple.</summary>
    public const byte TempleHandObjectId = AutoFurnitureRules.TempleHandObjectId;

    /// <summary>The object definitions a registered system reads.</summary>
    /// <remarks>
    /// The two beds are here because a bed is a thing standing on a tile rather
    /// than an index on a creature (Q130, <c>docs/research/objects.md</c> §7);
    /// without them <see cref="LairRules.TryPlaceBedInAnyLair"/> has nothing to
    /// place. They are byte-for-byte identical apart from the name: a bed is a
    /// bed by its <see cref="ObjectFlags.TypeLair"/> bit (§3), and which of the
    /// twelve it is only decides what gets drawn. Every record here is
    /// transcribed from the shipped <c>Objects.kwd</c> and read back by
    /// <c>ObjectsOnRealDataTests</c>, so none can drift from it.
    /// </remarks>
    private static ObjectDef[] Objects() =>
    [
        Bed(GoblinBedObjectId, "Goblin Creature Bed"),
        Bed(VampireBedObjectId, "Vampire Creature Bed"),

        // The heart's invisible obstacle, transcribed from the shipped record: width 3.0 tiles,
        // Obstacle and SolidObstacle (structure-blocks.md §6, Q355).
        new ObjectDef
        {
            Name = "Dungeon Heart Obstacle",
            ObjectId = Navigation.WorldSteering.DungeonHeartObstacleObjectId,
            Flags = ObjectFlags.Obstacle | ObjectFlags.BoulderCanRollThrough | ObjectFlags.FirstPersonObstacle | ObjectFlags.SolidObstacle,
            Hp = 1000,
            ManaValue = 0,
            RoomCapacity = 0,
            StartState = 0,
            PickUpPriority = 0,
            Width = new Fp32(12288),
            Height = new Fp32(256),
            Mass = new Fp32(8192),
            Speed = new Fp32(0),
            NameStringId = 0,
            TooltipStringId = 0,
        },

        // The two visible objects a running heart also holds on its centre tile (the Level 10
        // object dump): both plain Obstacle shoves, widths 1.0 and 0.6 tiles.
        new ObjectDef
        {
            Name = "Dungeon Heart",
            ObjectId = Navigation.WorldSteering.DungeonHeartObjectId,
            Flags = ObjectFlags.Obstacle | ObjectFlags.BoulderCanRollThrough,
            Hp = 34464, ManaValue = 0, RoomCapacity = 0, StartState = 0, PickUpPriority = 0,
            Width = new Fp32(4096), Height = new Fp32(4096), Mass = new Fp32(8192), Speed = new Fp32(0),
            NameStringId = 0, TooltipStringId = 0,
        },
        new ObjectDef
        {
            Name = "Arches",
            ObjectId = Navigation.WorldSteering.ArchesObjectId,
            Flags = ObjectFlags.Obstacle | ObjectFlags.BoulderCanRollThrough,
            Hp = 1, ManaValue = 0, RoomCapacity = 0, StartState = 0, PickUpPriority = 0,
            Width = new Fp32(2457), Height = new Fp32(4096), Mass = new Fp32(8192), Speed = new Fp32(0),
            NameStringId = 0, TooltipStringId = 0,
        },

        // The Library's two, which are what an imp puts away (ADR-0065). Their
        // ids are the shipped Rooms.kwd ObjectIds for room 6; the fields below
        // are NOT transcribed and nothing reads them, because kind 8 asks the
        // ROOM which objects are its, not the object which room is its - see
        // ObjectCarryRules.HomeRoomFor for what that reversal costs.
        new ObjectDef
        {
            Name = "Book Case",
            ObjectId = BookCaseObjectId,
            Flags = (ObjectFlags)0u,
            Hp = 1,
            ManaValue = 0,
            RoomCapacity = 0,
            StartState = 0,
            PickUpPriority = 0,
            Width = new Fp32(4096),
            Height = new Fp32(4096),
            Mass = new Fp32(4096),
            Speed = new Fp32(0),
            NameStringId = 0,
            TooltipStringId = 0,
        },
        new ObjectDef
        {
            Name = "Library Book",
            ObjectId = LibraryBookObjectId,
            Flags = (ObjectFlags)0u,
            Hp = 1,
            ManaValue = 0,
            RoomCapacity = 0,
            StartState = 0,
            PickUpPriority = 0,
            Width = new Fp32(4096),
            Height = new Fp32(4096),
            Mass = new Fp32(4096),
            Speed = new Fp32(0),
            NameStringId = 0,
            TooltipStringId = 0,
        },
        new ObjectDef
        {
            Name = "Temple Hand",
            ObjectId = TempleHandObjectId,
            Flags = ObjectFlags.Obstacle | ObjectFlags.BoulderCanRollThrough,
            Hp = 1,
            ManaValue = 0,
            RoomCapacity = 0,
            StartState = 0,
            PickUpPriority = 176,
            Width = new Fp32(4096),
            Height = new Fp32(4096),
            Mass = new Fp32(4096),
            Speed = new Fp32(0),
            NameStringId = 0,
            TooltipStringId = 0,
        },

        // The hatchery's two, transcribed the same way and guarded the same
        // way. A coop carries NO type flag at all -- only Obstacle -- which is
        // why HatcheryRules names its id instead of asking a flag, and why
        // getting these numbers from the file matters: a fabricated flag word
        // here would make a rule look right for the wrong reason.
        new ObjectDef
        {
            Name = "Chicken Coop",
            ObjectId = HatcheryRules.CoopObjectId,
            Flags = (ObjectFlags)0x00004000u,   // Obstacle
            Hp = 1,
            ManaValue = 0,
            RoomCapacity = 0,
            StartState = 0,
            PickUpPriority = 176,
            Width = new Fp32(3252),
            Height = new Fp32(1228),
            Mass = new Fp32(4096),
            Speed = new Fp32(0),
            NameStringId = 0,
            TooltipStringId = 0,
        },
        new ObjectDef
        {
            Name = "Egg",
            ObjectId = HatcheryRules.EggObjectId,

            // 0x00018602: DieOverTimeIfNotInRoom, CanBeSlapped, DieWhenSlapped,
            // Bounce, BoulderCanRollThrough. Note what is NOT there: TypeFood.
            // An egg is not edible, which is the whole of Q133.
            Flags = (ObjectFlags)0x00018602u,
            Hp = 128,
            ManaValue = 0,
            RoomCapacity = 0,
            StartState = 0,
            PickUpPriority = 176,
            Width = new Fp32(128),
            Height = new Fp32(128),
            Mass = new Fp32(8192),
            Speed = new Fp32(1024),
            NameStringId = 2719,
            TooltipStringId = 2720,
        },
        new ObjectDef
        {
            Name = "Chicken",
            ObjectId = HatcheryRules.ChickenObjectId,

            // 0x00230782: DieOverTimeIfNotInRoom, TypeFood, CanBePickedUp,
            // CanBeSlapped, DieWhenSlapped, BoulderDestroys, Damageable. So a
            // chicken can be picked up and dies if slapped, and the egg above
            // shares neither the food bit nor the carrying one.
            Flags = (ObjectFlags)0x00230782u,
            Hp = 1000,
            ManaValue = 0,
            RoomCapacity = 1,
            StartState = 10,
            PickUpPriority = 176,
            Width = new Fp32(128),
            Height = new Fp32(128),
            Mass = new Fp32(8192),
            Speed = new Fp32(1024),
            NameStringId = 2547,
            TooltipStringId = 2677,
        },

        // The three gold objects, all of them, because two of the three matter
        // by NOT being used: FloorGold creates object 1 and never 2 or 3, and a
        // test can only pin that if the other two are here to be created by
        // mistake. All three share the TypeGold bit, so IsPile answers yes to
        // all three -- which is the flag doing the work rather than the id, and
        // is why the rules that pick one name it outright.
        GoldObject(FloorGold.FloorGoldObjectId, "Gold", 0x00810140u, 0, 256, 1228),
        GoldObject(2, "Gold Bag", 0x00810140u, 1, 585, 1228),
        GoldObject(FloorGold.TreasuryPileObjectId, "Gold Pile 1", 0x00014140u, 1, 1085, 1068),
    ];

    /// <summary>
    /// One of the three gold records. They differ in four numbers and agree on
    /// everything else, including a <see cref="ObjectDef.StartState"/> of 31 and
    /// a name string shared by all three -- the game calls every one of them
    /// "Gold" in the interface.
    ///
    /// <para><c>Gold Pile 1</c>'s <see cref="ObjectDef.RoomCapacity"/> of 1 is
    /// worth noticing: it is the same field that turned out to cap a hatchery's
    /// flock at one chicken per tile, and here it says <b>one treasury pile per
    /// tile</b> -- which is exactly what the original's deposit does, found
    /// independently in the binary.</para>
    /// </summary>
    private static ObjectDef GoldObject(
        byte id, string name, uint flags, byte roomCapacity, int width, int height) => new()
    {
        Name = name,
        ObjectId = id,
        Flags = (ObjectFlags)flags,
        Hp = 128,
        ManaValue = 0,
        RoomCapacity = roomCapacity,
        StartState = 31,
        PickUpPriority = 176,
        Width = new Fp32(width),
        Height = new Fp32(height),
        Mass = new Fp32(16384),
        Speed = new Fp32(0),
        NameStringId = 292,
        TooltipStringId = 292,
    };

    private static ObjectDef Bed(byte id, string name) => new()
    {
        Name = name,
        ObjectId = id,

        // 0x04034020: TypeLair (0x20), Obstacle (0x4000), BoulderCanRollThrough
        // (0x10000), BoulderDestroys (0x20000), CastShadows (0x4000000). The
        // first bit is the only one any rule reads; the rest are transcribed
        // rather than trimmed, because a fixture that keeps only the bits it
        // uses stops being a copy of the record.
        Flags = (ObjectFlags)0x04034020u,

        Hp = 128,
        ManaValue = 0,
        RoomCapacity = 1,
        StartState = 0,
        PickUpPriority = 176,
        Width = new Fp32(2048),
        Height = new Fp32(256),
        Mass = new Fp32(8192),
        Speed = new Fp32(0),
        NameStringId = 0,
        TooltipStringId = 0,
    };

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
        PerceptionRange = new Fp32(0x6000),
        FootprintRadius = new Fp32(573),
        SizeClass = 1,
        TurnRate = 4096,

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
        // HpFromChicken and HungerFill really are 0 — the Imp never eats.
        // OwnLandHealthIncreasePerSecond is 67, one of only two non-zero values
        // in that whole column (the other is King Reginald at 400). Checked
        // against the install by CreatureNeedsOnRealDataTests.
        HpFromChicken = 0,
        HungerFill = 0,
        OwnLandHealthIncreasePerSecond = 67,
        DecomposeValue = 0,

        // 100 against the Imp's 500 health means five slaps kill an imp.
        // Checked against the install by CreatureNeedsOnRealDataTests.
        SlapDamage = 100,
        StunDuration = new Fp32(0),
        Resistances = [],
        Spells = [],
        ResearchPerSecond = 0,
        ManufacturePerSecond = 0,
        ManaGeneratedByPrayerPerSecond = 0,
        // The Imp's real anger values, and they are why it never gets angry
        // without needing a special case: 1 a second against a ceiling of 32767
        // is nine hours. The DK2 wiki says flatly "Imps never get angry"; the
        // shipped data says the same thing arithmetically.
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

    /// <summary>The Goblin — level 1's whole creature pool, and the first
    /// non-worker the simulation can produce.</summary>
    /// <remarks>A delta on <see cref="Imp"/>, so the hundred fields no mechanic
    /// reads stay in one place. Every field the portal reads is overridden here
    /// and is the Goblin's own: creature id 7, evil and not a worker, 1500 hp, 170
    /// melee, 500 pay, speed 2867 in DK2's fixed point (0.7 tiles a second),
    /// one attraction slot wanting a Lair of any size. Imp-specific fields are
    /// zeroed rather than inherited — 1400 gold capacity, 500 starting gold and
    /// the "never hungry" sentinel are facts about imps.
    /// <c>PortalOnRealDataTests</c> re-reads the overrides out of the shipped
    /// <c>Creatures.kwd</c>, so this cannot drift from it.
    /// </remarks>
    public const byte GoblinCreatureId = 7;

    public static CreatureDef Goblin() => Imp() with
    {
        Name = "Goblin",
        CreatureId = GoblinCreatureId,
        SlapDamage = 115,
        FootprintRadius = new Fp32(651),
        SizeClass = 2,
        TurnRate = 2048,

        // The real Goblin's flags, all load-bearing. AvailableViaPortal is what
        // PortalRules.CanComeFromPortal requires; GenerateDeadBody decides
        // whether death leaves a corpse (Q99); CanWalkOnWater decides both
        // whether water may be crossed and whether crossing costs health (Q118,
        // and 44 of 46 creatures carry it); CanBePickedUp and CanBeSlapped are
        // what let the hand handle it at all (ADR-0062).
        Flags = CreatureFlags.IsEvil | CreatureFlags.AvailableViaPortal
                | CreatureFlags.GenerateDeadBody | CreatureFlags.CanWalkOnWater
                | CreatureFlags.CanBePickedUp | CreatureFlags.CanBeSlapped,

        Hp = 1500,
        MeleeDamage = 170,
        Pay = 500,
        Fear = 50,
        Threat = 37,

        // The Goblin's own experience rates, both live now that training pays
        // (Q205). 40 divides by variable 19 exactly, so a Goblin loses nothing
        // to the loader's truncation where a Vampire's 30 loses two a second.
        // 200 is its COMBAT rate: across every shipped creature the combat rate
        // is exactly five times the training rate.
        ExpPerSecond = 200,
        ExpPerSecondTraining = 40,
        Speed = new Fp32(2867),
        RunSpeed = new Fp32(2867),
        JobClass = JobClass.Fighter,
        FightStyle = FightStyle.Blitzer,

        // The one field the portal's attraction gate reads: a Lair, any size.
        // The second slot is empty on the Goblin, as it is on all but four
        // creatures.
        Attractions =
        [
            new CreatureAttraction(1, (byte)RoomTypeId.Lair, 0),
            new CreatureAttraction(0, 0, 0),
        ],

        // The needs, as shipped: hungry every 180 seconds, one chicken fills
        // it, and that chicken is worth 375 — a quarter of its 1500 health.
        // These are what make the Goblin the fixture creature that has needs,
        // against the Imp which has none at all.
        HungerRate = Fp32.FromDouble(180),
        HungerFill = 1,
        HpFromChicken = 375,
        TimeAwake = 240,
        TimeSleep = 9,
        DecomposeValue = 200,
        OwnLandHealthIncreasePerSecond = 0,

        // Its bed. 22 is the Goblin's own object id, and it is what makes the
        // Goblin the fixture creature that sleeps — the Imp's is 0, which the
        // owner confirms means no lair at all (as for the Skeleton).
        LairObjectId = 22,

        // The Goblin's real anger record. AngerNoLair is the rate the owner
        // measured against a stopwatch to give the ceiling (Q106): 100 a second
        // reached unhappy at 2:45 and angry at 5:30, which is 32767/2 and
        // 32767. AngerSlap 3000 is the rate that gave the second, independent
        // confirmation — 6 slaps to unhappy, 11 to angry (Q114).
        AngerNoLair = 100,
        AngerNoFood = 100,
        AngerNoPay = 60,
        AngerNoWork = 50,
        AngerSlap = 3000,
        AngerInHand = -250,

        // Its three mood job tables, which are what the ladder actually drives.
        // A content Goblin trains, an unhappy one drinks, and an angry one is a
        // coin toss between walking out and rebelling — only the first of which
        // is implemented (ADR-0053).
        HappyJobs = [new(JobType.Train, 0, 0, 100)],
        UnhappyJobs = [new(JobType.Drink, 0, 0, 100)],
        AngryJobs = [new(JobType.Leave, 0, 0, 50), new(JobType.Rebel, 0, 0, 50)],

        // Imp facts, cleared rather than inherited.
        MaxGoldHeld = 0,
        InitialGoldHeld = 0,
    };

    public const byte DeserterCreatureId = 44;

    /// <summary>A Goblin whose angry table is <c>Leave(100%)</c>; nothing else
    /// differs.</summary>
    /// <remarks>
    /// <b>Not a DK2 creature.</b> An angry creature rolls afresh on every
    /// allowed selection (ADR-0061), so a test about <i>leaving</i> written on
    /// the real Goblin's coin toss would be a test about the seed. One choice
    /// takes the roll out of the question. The shipped roster is asserted in
    /// <c>CreatureNeedsOnRealDataTests</c>, so this cannot be mistaken for a
    /// claim about the original.
    /// </remarks>
    public static CreatureDef Deserter() => Goblin() with
    {
        Name = "Deserter",
        CreatureId = DeserterCreatureId,
        AngryJobs = [new(JobType.Leave, 0, 0, 100)],
    };

    public const byte VampireCreatureId = 8;

    /// <summary>The Vampire: the one creature in the roster that water can
    /// kill. CONFIRMED (owner, from play): "vampires can't cross through water,
    /// they need bridges".</summary>
    /// <remarks>Hazard terrain charges only a creature that cannot cross it
    /// (<c>CreatureHealthRules.TerrainHealthPerSecond</c>), and
    /// <see cref="CreatureFlags.CanWalkOnWater"/> is on 44 of the 46 creatures
    /// — the two Vampires are the exceptions, so without one here water's cost
    /// cannot be tested. Two absent flags matter as much: no
    /// <c>GenerateDeadBody</c> (it reincarnates rather than leaving a corpse,
    /// owner, Q113) and no <c>AvailableViaPortal</c> (it arrives from a
    /// Graveyard — attraction room 14, <c>LairObjectId</c> 26, its coffin).
    /// <c>CreatureNeedsOnRealDataTests</c> guards the overrides.</remarks>
    public static CreatureDef Vampire() => Goblin() with
    {
        Name = "Vampire",
        CreatureId = VampireCreatureId,
        FootprintRadius = new Fp32(999),
        SizeClass = 3,
        TurnRate = 1365,

        // 30 is the value that DOES lose to the loader's divide - 30 / 4 = 7,
        // so a Vampire trains at 28 a game second, not 30 - which makes it the
        // fixture's witness for that quirk. Eighteen of the 46 shipped
        // creatures carry 30, and 26 of the 46 lose something to truncation.
        ExpPerSecond = 150,
        ExpPerSecondTraining = 30,
        ResearchPerSecond = 5,
        SlapDamage = 125,

        Flags = CreatureFlags.IsEvil | CreatureFlags.IsFearless
                | CreatureFlags.CanBeResurrected | CreatureFlags.NeedBodyForFightIdle,

        Hp = 2500,
        MeleeDamage = 666,
        Pay = 750,
        Fear = 267,
        Threat = 107,
        Speed = new Fp32(2867),
        RunSpeed = new Fp32(5160),
        ShuffleSpeed = new Fp32(1638),
        JobClass = JobClass.Thinker,

        // Never eats — the same 32767 sentinel the Imp carries — but does
        // sleep, and for the same nine seconds as a Goblin after a much longer
        // 330 awake. Its bed is object 26 and its attraction is room 14, the
        // Graveyard.
        HungerRate = Imp().HungerRate,
        HungerFill = 0,
        HpFromChicken = 0,
        TimeAwake = 330,
        TimeSleep = 9,
        LairObjectId = 26,
        Attractions = [new CreatureAttraction(1, 14, 0), new CreatureAttraction(0, 0, 0)],

        // Its own anger record. AngerNoFood is 1 rather than 0 on a creature
        // that never eats, which is the shipped table's way of leaving a
        // needle in a need it does not have. AngerSlap is 7000, so a Vampire
        // takes five slaps to unhappy where a Goblin takes six.
        AngerNoFood = 1,
        AngerNoWork = 60,
        AngerSlap = 7000,

        // A vampire never walks out. Where a Goblin's angry table is a coin
        // toss between Leave and Rebel, this one is Rebel outright.
        HappyJobs = [new(JobType.Research, 0, 0, 80), new(JobType.Praying, 0, 0, 20)],
        UnhappyJobs = [new(JobType.Sulk, 0, 0, 10), new(JobType.Drink, 0, 0, 90)],
        AngryJobs = [new(JobType.Rebel, 0, 0, 100)],

        DecomposeValue = 0,
        MaxGoldHeld = 7500,
        InitialGoldHeld = 500,
    };

    public const byte SkeletonCreatureId = 9;

    /// <summary>The Skeleton — what a prisoner becomes, and the only creature
    /// in this fixture that a room makes rather than a portal.</summary>
    /// <remarks>The creature with no needs at all, and so the fixture's control
    /// against the Goblin: <c>hungerRate</c> 32767 and <c>awake</c> 32767
    /// (never eats, never sleeps), <c>sleep</c> 0, <c>LairObjectId</c> 0 (no
    /// bed, so nothing to want), <c>pay</c> 0 and every anger rate zero.
    /// <c>fear</c> is 30000, not the Goblin's 50 — the shipped table's way of
    /// writing "does not run away" on a creature also flagged
    /// <c>IsFearless</c>. It does <b>not</b> carry <c>GenerateDeadBody</c>, so
    /// a skeleton that runs out of health leaves nothing behind.</remarks>
    public static CreatureDef Skeleton() => Goblin() with
    {
        Name = "Skeleton",
        CreatureId = SkeletonCreatureId,
        FootprintRadius = new Fp32(634),
        TurnRate = 1365,

        Flags = CreatureFlags.IsEvil | CreatureFlags.IsFearless
                | CreatureFlags.CanBePickedUp | CreatureFlags.CanBeSlapped,

        Hp = 1500,
        MeleeDamage = 280,
        Pay = 0,
        Fear = 30000,
        Threat = 60,
        Speed = new Fp32(2867),
        RunSpeed = new Fp32(3686),
        JobClass = JobClass.Fighter,
        FightStyle = FightStyle.Flanker,

        // Never eats, never sleeps, never wants a bed. The three sentinels the
        // shipped record uses to say so.
        HungerRate = Imp().HungerRate,
        HungerFill = 0,
        HpFromChicken = 0,
        TimeAwake = 32767,
        TimeSleep = 0,
        LairObjectId = 0,
        DecomposeValue = 0,

        // Attracted by a Prison, which is the room that makes it.
        Attractions =
        [
            new CreatureAttraction(1, (byte)RoomTypeId.Prison, 0),
            new CreatureAttraction(0, 0, 0),
        ],

        // The shipped record says "never angry" TWICE: every anger rate is
        // zero, AND it carries the never-angered flag. The FLAG is the rule DK2
        // reads (tested in the one wrapper every anger site goes through); the
        // zero rates are a coincidence of this creature - Jack carries the same
        // flag with all four rates populated (Q210). Both are kept, so the
        // fixture reproduces the record rather than the inference.
        Flags2 = CreatureFlags2.NeverAngered,
        AngerNoLair = 0,
        AngerNoFood = 0,
        AngerNoPay = 0,
        AngerNoWork = 0,

        HappyJobs = [new(JobType.Train, 0, 0, 100)],
        UnhappyJobs = [new(JobType.Guard, 0, 0, 100)],
        AngryJobs = [new(JobType.Rebel, 0, 0, 100)],
    };

    public const byte BileDemonCreatureId = 3;

    /// <summary>The Bile Demon, here for one number: <c>hungerFill</c> is
    /// <b>5</b> where every other creature in the fixture is 1 or 0.</summary>
    /// <remarks>That number separates DK2's two ways of paying off hunger. A
    /// chicken eaten in a <b>hatchery</b> pays off one, so a Bile Demon's meal
    /// is five birds; a chicken eaten in a <b>prison</b> pays off the whole of
    /// <c>HungerFill</c>, so a caged one is filled by a single bird
    /// (<c>docs/research/eating.md</c>, ADR-0067). At <c>hungerFill</c> 1 the
    /// two rules are indistinguishable. Its hunger and sleep numbers are the
    /// shipped record's — 6000 health, 220 seconds between meals, five chickens
    /// to fill, 200 health a chicken, 270 awake, 9 asleep — and the rest is the
    /// Goblin's, because nothing reads the rest.</remarks>
    public static CreatureDef BileDemon() => Goblin() with
    {
        Name = "Bile Demon",
        CreatureId = BileDemonCreatureId,
        FootprintRadius = new Fp32(1740),
        SizeClass = 5,
        TurnRate = 682,

        Hp = 6000,
        MeleeDamage = 525,
        Pay = 600,
        Fear = 300,
        Threat = 120,

        HungerRate = Fp32.FromDouble(220),
        HungerFill = 5,
        HpFromChicken = 200,
        TimeAwake = 270,
        TimeSleep = 9,
        LairObjectId = 18,
    };

    /// <summary>Every global variable a built mechanic reads, at its shipped
    /// value, plus four rows of the per-level stat table.</summary>
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
        Set(vars, VariableTypeId.ConvertRoomHealth, -4000);
        Set(vars, VariableTypeId.GoldMinedFromGems, 25);

        // Creature needs. All four are the shipped values, and
        // CreatureNeedsOnRealDataTests reads them back out of the install so
        // they cannot drift from it.
        Set(vars, VariableTypeId.ModifyHealthOfCreatureInLairPerSecond, 100);
        Set(vars, VariableTypeId.CreatureCriticalHealthPercentageOfMax, 20);
        Set(vars, VariableTypeId.CreatureSleepsWhenBelowPercentHealth, 50);
        // The anger recovery rate: -550 a second while asleep in one own bed,
        // against AngerNoLair (+100), so a bed undoes in one second what five
        // and a half seconds without one did (ADR-0053).
        Set(vars, VariableTypeId.ModifyAngerOfCreatureInLairPerSecond, -550);

        // Praying, and both numbers are shipped and both are LIVE - checked,
        // because seven of their neighbours in the same block turned out to be
        // read by nothing at all (ADR-0082). The Temple is the only source in
        // DK2 that drains an anger bucket it did not fill (ADR-0081), and it
        // applies the -275 six times, once per bucket.
        Set(vars, VariableTypeId.ModifyCreatureAngerWhilePrayingPerSecond, -275);
        Set(vars, VariableTypeId.ModifyCreatureHealthWhilePrayingPerSecond, 33);

        // The training cap the job dispatcher tests a creature's level against
        // before it will look for a training room.
        Set(vars, VariableTypeId.TrainingRoomMaxExperienceLevel, 4);

        // Training's two other shipped numbers (Q205). "Cold" is the shipped
        // name and it means gold; the table's figure is negative and the
        // original negates it, so this is a charge of 5 a game second. The
        // second one is DK2's per-second experience DRAIN for creatures that are
        // not training - a real mechanism shipped with the rate set to zero.
        Set(vars, VariableTypeId.ModifyPlayerColdWhileTrainingPerSecond, -5);

        // The Library's two, at their shipped values. Both are read only by the
        // keeper's research adder, which is the correction that put them here:
        // the duration was written up across this project as a variable that
        // NOTHING reads, on the strength of the slap's work bonus using a
        // hardcoded 100 ticks instead (Q159). Two mechanisms, one name.
        Set(vars, VariableTypeId.IncreasedWorkRatePecentageFromSlapping, 50);
        Set(vars, VariableTypeId.IncreasedWorkRateDurationFromSlappingSeconds, 60);
        Set(vars, VariableTypeId.EPLossSecondWhileNotTraining, 0);

        Set(vars, VariableTypeId.CreatureDyingStateDurationSeconds, 60);

        // The corpse timer, which nothing reads: a body is an object (M6). Here
        // so CreatureDeathRules.BodyWindowTicks can be pinned rather than only
        // documented — two variables sharing a value is a fact worth a test.
        Set(vars, VariableTypeId.DeadBodyDiesAfterSeconds, 60);
        Set(vars, VariableTypeId.DecomposeValueNeededForVampire, 1000);
        Set(vars, VariableTypeId.ImpIdleDelayBeforeReevaluationSeconds, 5);

        // What an imp earns for working (Q124, CONFIRMED by the owner: "imps
        // get experience by working... they get exp and passively their level
        // raises"). The name lies: 20 is per ORIGINAL TICK, so 80 a game second
        // (Q79), and the 3000 to level 2 is 37.5 seconds of digging.
        Set(vars, VariableTypeId.ImpExperienceGainPerSecond, 20);

        // The hatchery's two shipped numbers. The generation time is here for
        // completeness -- nothing reads it, because the interval it belongs to
        // is multiplied by a variable no shipped file supplies (Q132).
        Set(vars, VariableTypeId.ChickenGenerationTimePerHatchery, 16);
        Set(vars, VariableTypeId.MaxFreeRangeChickensPerPlayer, 12);
        // How many things one keeper may hold at once (HandRules.Capacity).
        Set(vars, VariableTypeId.MaxNumberOfThingsInHand, 64);

        // The prison, as shipped: three health a second off every prisoner,
        // and fifty percent per extra creature that an overcrowded one breaks
        // out (ADR-0064).
        Set(vars, VariableTypeId.PrisonModifyCreatureHealthPerSecond, -3);
        Set(vars, VariableTypeId.OvercrowdingJailBreakPercentagePerExtraCreature, 50);
        Set(vars, VariableTypeId.MotionlessInPrisonSeconds, 8);
        // Payday, as shipped: ten minutes between them, and the first one ten
        // minutes in rather than at zero (Q9, ADR-0078).
        Set(vars, VariableTypeId.PayDayFrequencySeconds, 600);
        Set(vars, VariableTypeId.TimeBeforeFirstPayDaySeconds, 600);
        Set(vars, VariableTypeId.MinimumImpThreshold, 4);
        Set(vars, VariableTypeId.TimeBeforeFreeImpGeneratedSeconds, 5);
        Set(vars, VariableTypeId.MaxGoldPerTreasuryTile, 3000);
        Set(vars, VariableTypeId.RoomSellValuePercentageOfCost, 50);
        Set(vars, VariableTypeId.WoodBridgeLifeOnLavaSeconds, 30);
        Set(vars, VariableTypeId.MaxGoldPerDungeonHeartTile, 1000);
        Set(vars, VariableTypeId.MaxGoldPileOutsideTreasury, 2000);
        Set(vars, VariableTypeId.MaximumManaThreshold, 200000);
        Set(vars, VariableTypeId.MaximumManaGainPerSecond, 500);
        Set(vars, VariableTypeId.ExcessManaDecreaseRatePerSecond, 5);
        Set(vars, VariableTypeId.DungeonHeartManaGenerationIncreasePerSecond, 30);

        // The two the melee multiplier reads, as shipped. The Combat Pit's 100 is an
        // increment and not a scale, so a pit fighter hits twice as hard; the first-person
        // 12 is this project's hypothesis for a scalar nothing writes (Q337).
        Set(vars, VariableTypeId.CombatPitMeleeDamageModifierPercentage, 100);
        Set(vars, VariableTypeId.FirstPersonStatIncreasePercentage, 12);

        // The portal's three. The generation speed is the game-wide 25 rather
        // than level 1's 18, because this fixture is the global table; the
        // per-level override is exercised in PortalOnRealDataTests, which reads
        // the real Level1Variables.kld.
        Set(vars, VariableTypeId.EntranceGenerationSpeedSeconds, 25);
        Set(vars, VariableTypeId.CreaturesSupportedByFirstPortal, 15);
        Set(vars, VariableTypeId.CreaturesSupportedPerAdditionalPortal, 5);

        // Four rows of the per-level stat table, as shipped (Q119). The full
        // table is 32 stats x 9 levels; these are the stats anything here
        // reads, and a stat with no rows correctly scales by 100% at every
        // level. CreatureNeedsOnRealDataTests reads them back out of the
        // install, so they cannot drift from it.
        Stat(vars, CreatureStatId.Health, [125, 150, 175, 200, 225, 250, 300, 400, 600]);
        Stat(vars, CreatureStatId.Speed, [105, 110, 115, 120, 125, 150, 175, 200, 250]);
        // Wages, to 250% at level 10 - a veteran costs two and a half times
        // what it did out of the portal (ADR-0078). The drift guard in
        // DK2.Behavior.Tests re-reads this row from the real install.
        Stat(vars, CreatureStatId.Pay, [105, 110, 115, 120, 130, 140, 150, 180, 250]);
        Stat(vars, CreatureStatId.ExperienceForNextLevel,
             [130, 180, 250, 350, 470, 600, 720, 850, 1000]);
        // Melee damage, to 600% at level 10 - the steepest row the table has,
        // shared with Health (combat-damage.md section 3).
        Stat(vars, CreatureStatId.MeleeDamage, [125, 150, 175, 200, 225, 250, 300, 400, 600]);

        // The dungeon heart's health and its regeneration, as shipped in the global
        // VARIABLES table (balance-variables-in-the-binary.md rows 174 and 131). A
        // keeper's world cannot be built without them (ADR-0162).
        Set(vars, VariableTypeId.DungeonHeartObjectHealth, 100000);
        Set(vars, VariableTypeId.DungeonHeartHealthRegenerationPerSecond, 25);
        return vars;
    }

    /// <summary>
    /// Adds one stat's rows for levels 2 to 10, in that order. The table has no
    /// level-1 row: level 1 is the creature record's own value, which
    /// <c>GameDatabase.StatPercent</c> returns as an implicit 100%.
    /// </summary>
    private static void Stat(GlobalVariables vars, CreatureStatId stat, int[] percentByLevel)
    {
        for (int i = 0; i < percentByLevel.Length; i++)
        {
            vars.CreatureStats.Add(new CreatureStatEntry(
                StatId: (int)stat,
                Value: percentByLevel[i],
                Level: i + 2,
                FirstPerson: false));
        }
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
        byte sizeX = 1, byte sizeY = 1,
        TileConstruction construction = TileConstruction.Normal,
        byte createdCreatureId = 0,
        byte[]? objectIds = null) => new()
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

        // Transcribed for the portal, because PortalRules.IsEntrance asserts
        // that ThreeByThree picks out exactly the two portals.
        Construction = construction,

        // Transcribed for the two rooms that set it -- Graveyard 8 (Vampire)
        // and Prison 9 (Skeleton) -- because PrisonSystem reads it to decide
        // what a withered prisoner rises as.
        CreatedCreatureId = createdCreatureId,

        // Transcribed for the Library, because ObjectCarryRules reads the
        // room->objects mapping backwards to decide where a loose object
        // belongs. Ten of the shipped rooms have a list (Q120).
        ObjectIds = objectIds ?? [],

        // Not transcribed. Present because the record requires them.
        HealthGain = 0,
        CeilingHeight = new Fp32(0),
        OrderInEditor = 0,
        NameStringId = 0,
        TooltipStringId = 0,
    };

    /// <summary>A small dungeon: impenetrable border, a rock body, a
    /// claimed-path room at the top-left owned by
    /// <see cref="PlayerIds.FirstKeeper"/>, one gold seam and one gem face. The
    /// room's owner is load-bearing: player 1 is the <i>hero</i> side (Q91,
    /// <see cref="PlayerIds"/>), so a test world that gives it player 1 is
    /// testing the enemy's home ground.</summary>
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
                byte owner = room ? PlayerIds.FirstKeeper : PlayerIds.Unowned;
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

    /// <summary>The declared order with one system left out, for tests that
    /// measure one mechanic and would otherwise be measuring two. Use it
    /// sparingly and say why at the call site: a keeper with a dungeon heart and
    /// no imps is staffed on the first tick (<c>ImpSupplySystem</c>), so a
    /// mana-income test that hands a keeper a heart starts paying imp upkeep
    /// immediately unless it excludes that system.</summary>
    public static ISimulationSystem[] SystemsWithout<T>() where T : ISimulationSystem =>
        [.. Systems.SimulationSystems.Default().Where(s => s is not T)];
}
