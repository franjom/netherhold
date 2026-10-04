// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;

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
    public const byte HeroPortalTerrain = 36;

    /// <summary>The two Hero Gates the shipped maps use (terrains 33 and 37, rooms 20 and 23): Ownable
    /// and Room, never Attackable, so claimable only once ruined (ADR-0299, ADR-0320).</summary>
    public const byte HeroGate2By2Terrain = 33;
    public const byte HeroGate3By1Terrain = 37;

    /// <summary>Terrain 29, the Hero Gate [ Tile ] (room 19), marked not used and on no shipped map;
    /// here so the step-off's side draw can be tested.</summary>
    public const byte HeroGateTileTerrain = 29;
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

    /// <summary>Terrain 25, the Combat Pit, where a spectator's anger cools (ADR-0186).</summary>
    public const byte CombatPitTerrain = 25;

    /// <summary>Terrain 21, the Torture Chamber (ADR-0193).</summary>
    public const byte TortureChamberTerrain = 21;

    /// <summary>Terrain 19, the Workshop ("Work Shop", ADR-0203).</summary>
    public const byte WorkshopTerrain = 19;
    public const byte GuardRoomTerrain = 18;
    public const byte ManaVaultTerrain = 31;
    public const byte ClaimedVaultTerrain = 32;

    /// <summary>The Alarm trap, its crate the Alarm crate (ADR-0214), and the traps whose firing is
    /// built (ADR-0321) and the Guard Post at their shipped values, upkeep included (<c>TrapRuntimeOnRealDataTests</c>), with no crate. The
    /// Freeze is id 17 here, the fixture's Alarm holding the shipped Freeze's 7.</summary>
    public static TrapDef[] Traps() =>
    [
        new() { Name = "Alarm", TrapId = 7, Health = 100, GoldCost = 250, ManufactureRequired = 1000, CrateObjectId = AlarmCrateObjectId, ShotTypeId = 0, ManaUpkeepPerSecond = 0 },
        FiringTrap("Sentry Trap", SentryTrapId, 1, 24, 5, 250, 0x31598, CannonBallShotId, 250, 0, -2048, 74, 45, 6, 3000, upkeep: 5),
        FiringTrap("Fireburst Trap", FireburstTrapId, 1, 48, 1, 250, 0x3053A, FireburstShotId, 2000, 1, -1761, 185, 45, 16, 8000, upkeep: 10),
        FiringTrap("Lightning Trap", LightningTrapId, 1, 48, 4, 375, 0x31518, LightningTrapShotId, 400, 0, -2048, 74, 45, 12, 6000, upkeep: 25),
        FiringTrap("Freeze Trap", FreezeTrapId, 2, 24, 1, 250, 0x3053A, FreezeTrapShotId, 20, 1, 0, 37, 45, 12, 6000, upkeep: 10),
        FiringTrap("Trigger Trap", TriggerTrapId, 3, 16, 1, 0, 0x2043A, TriggerTrapShotId, 0, 1, 0, 0, 0, 4, 2000, upkeep: 5),
        FiringTrap("Magic Door", MagicDoorTrapId, 2, 32, 4, 500, 0x35BA, DoorFireballShotId, 150, 0, 0, 100, 60, 40, 4000, upkeep: 30),
        FiringTrap("Hero Sentry Trap", HeroSentryTrapId, 1, 24, 5, 100, 0x9598, CannonBallShotId, 250, 0, -2048, 74, 45, 30, 3000, upkeep: 50),
        FiringTrap("Guard Post", GuardPostTrapId, 2, 4, 0, 0, 0xC18, 0, 0, 0, 0, 0, 0, 0, 1000, upkeep: 0),
        FiringTrap("Gas Trap", GasTrapId, 2, 16, 1, 0, 0x3053A, GasTrapCloudShotId, 750, 1, 0, 74, 45, 6, 3000, upkeep: 5),
        FiringTrap("Alarm Trap", AlarmTrapId, 1, 4, 4, 0, 0x3153A, AlarmShotId, 0, 5, 0, 0, 0, 4, 2000, upkeep: 5),
        FiringTrap("Fear Trap", FearTrapId, 1, 24, 3, 125, 0x31518, FearShotId, 0, 3, 0, 600, 60, 12, 6000, upkeep: 5),
        FiringTrap("Spike Trap", SpikeTrapId, 2, 16, 1, 0, 0x53A, SpikeShotId, 750, 1, 0, 74, 45, 8, 4000, upkeep: 5),
        FiringTrap("Boulder Trap", BoulderTrapId, 1, 4, 3, 0, 0x31418, BoulderShotId, 1, 0, 0, 0, 0, 0, 8000),
        FiringTrap("Jack In The Box", JackInTheBoxTrapId, 2, 4, 1, 0, 0x21408, JackInTheBoxShotId, 10000, 2, 0, 0, 0, 0, 8000),
    ];

    public const byte SentryTrapId = 3, FireburstTrapId = 5, LightningTrapId = 6, FreezeTrapId = 17, TriggerTrapId = 9,
        MagicDoorTrapId = 12, HeroSentryTrapId = 14, GuardPostTrapId = 11, GasTrapId = 1, AlarmTrapId = 4, FearTrapId = 8,
        SpikeTrapId = 10, BoulderTrapId = 2, JackInTheBoxTrapId = 16;

    private static TrapDef FiringTrap(string name, byte id, byte selector, int rechargeTicks, int range, int mana, uint flags,
        byte shot, int data1, int data2, int offsetY, ushort threat, int threatSeconds, short gain, ushort health, ushort upkeep = 0) => new()
    {
        Name = name, TrapId = id, Health = health, GoldCost = 1000, ManufactureRequired = 1000, ShotTypeId = shot,
        ManaUpkeepPerSecond = upkeep, TriggerType = selector, RechargeSecondsRaw = rechargeTicks * 1024, RangeTiles = range,
        ManaCostToFire = mana, Flags = flags, ShotData1 = data1, ShotData2 = data2, ShotOffsetY = offsetY, Threat = threat,
        ThreatSecondsRaw = threatSeconds * 4096, HealthGain = gain,
    };

    /// <summary>Two shipped door records: the Wooden Door and the Barricade, whose flags byte
    /// (0x40) the navigation writer reads as a permanent lock. Their <c>HealthGain</c> is the
    /// shipped recovery a second, 2 and 1 (<c>TrapRuntimeOnRealDataTests</c>).</summary>
    public static DoorDef[] Doors() =>
    [
        new() { Name = "Wooden Door", DoorId = 1, Health = 400, HealthGain = 2, GoldCost = 500, ManufactureRequired = 1000, CrateObjectId = WoodenDoorCrateObjectId },
        new() { Name = "Barricade", DoorId = 6, Health = 800, HealthGain = 1, GoldCost = 750, ManufactureRequired = 1000, Flags = DoorFlags.Barricade },
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
            T(CombatPitTerrain, "Combat Pit",
              TerrainFlags.Ownable, 10000, 10000, CombatPitTerrain, ClaimedPath),
            T(TortureChamberTerrain, "Torture Chamber",
              TerrainFlags.Ownable, 10000, 10000, TortureChamberTerrain, ClaimedPath),
            T(WorkshopTerrain, "Work Shop",
              TerrainFlags.Ownable, 10000, 10000, WorkshopTerrain, ClaimedPath),
            T(GuardRoomTerrain, "Guard Room",
              TerrainFlags.Ownable, 10000, 10000, GuardRoomTerrain, ClaimedPath),
            T(ClaimedPath, "Claimed Path",
              TerrainFlags.Ownable | TerrainFlags.Attackable | TerrainFlags.FillInable,
              660, 660, Rock, DirtPath, manaGain: 1),
            T(ReinforcedWall, "Reinforced Wall",
              TerrainFlags.Solid | TerrainFlags.Ownable | TerrainFlags.Taggable | TerrainFlags.Attackable,
              900, 900, ReinforcedWall, Rock, manaGain: 1),
            T(TreasuryTerrain, "Treasury", RoomTerrain, 10000, 10000, TreasuryTerrain, ClaimedPath),
            T(LairTerrain, "Lair", RoomTerrain, 10000, 10000, LairTerrain, ClaimedPath),
            T(PortalTerrain, "Portal", RoomTerrain, 10000, 10000, PortalTerrain, ClaimedPath),
            T(HeroGate2By2Terrain, "Hero Gate [ 2x2 ]", TerrainFlags.Ownable | TerrainFlags.Room, 100, 100, HeroGate2By2Terrain, ClaimedPath),
            T(HeroGate3By1Terrain, "Hero Gate [ 3x1 ]", TerrainFlags.Ownable | TerrainFlags.Room, 100, 100, HeroGate3By1Terrain, ClaimedPath),
            T(HeroGateTileTerrain, "Hero Gate [ Tile ]", TerrainFlags.Ownable | TerrainFlags.Room, 100, 100, HeroGateTileTerrain, ClaimedPath),
            T(HeroPortalTerrain, "Hero Portal", RoomTerrain, 10000, 10000, HeroPortalTerrain, ClaimedPath),
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
        traps: Traps(),
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
            R(RoomTypeId.HeroGate2By2, "Hero Gate [ 2x2 ]", 0, HeroGate2By2Terrain, 0x244u, construction: TileConstruction.HeroGate2By2),
            R(RoomTypeId.HeroGate3By1, "Hero Gate [ 3x1 ]", 0, HeroGate3By1Terrain, 0x244u, construction: TileConstruction.HeroGate3By1),
            R(RoomTypeId.HeroGateTile, "Hero Gate [ Tile ]", 0, HeroGateTileTerrain, 0x2E4Cu, construction: TileConstruction.HeroGateTile),
            R(RoomTypeId.HeroPortal, "Hero Portal", 0, HeroPortalTerrain, 0x1004u, 3, 3,
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
            // [64, 0, 0, 58] -- a book case, and a library book for the walls. That list is the
            // whole reason this room is in the fixture: ObjectCarryRules reads
            // it backwards to find out where a loose object belongs (ADR-0065,
            // Q120).
            R(RoomTypeId.Library, "Library", 600, LibraryTerrain, 0x11ACu, 3, 3,
              objectIds: [BookCaseObjectId, 0, 0, LibraryBookObjectId]),

            // Costs, sizes and flags from the shipped table. The Temple's 5x5
            // DoubleQuad is transcribed rather than rounded off, because it is
            // the most expensive room in the game and the size is part of why.
            R(RoomTypeId.TrainingRoom, "Training Room", 500, TrainingRoomTerrain, 0x13ACu, 3, 3,
              objectIds: [TrainingDummyObjectId, 0, 0, 29]),
            R(RoomTypeId.Temple, "Temple", 3000, TempleTerrain, 0x108Cu, 5, 5,
              construction: TileConstruction.DoubleQuad),
            // With its shipped ObjectIds (roulette table, barrel, bar table, alcove barrels),
            // so the lever's slot can be shown to stay free of them (ADR-0188).
            R(RoomTypeId.Casino, "Casino", 750, CasinoTerrain, 0x13ACu, 3, 3,
              objectIds: [87, 59, 67, 68]),
            R(RoomTypeId.CombatPit, "Combat Pit", 750, CombatPitTerrain, 0x12ACu, 5, 5),
            // With its shipped slot list (three wheels, a chair, coals, water): tests put the
            // devices down by hand, since the fixture carries only the two device records.
            R(RoomTypeId.TortureChamber, "Torture Chamber", 1500, TortureChamberTerrain, 0x13ACu, 3, 3,
              objectIds: [8, 8, 61, 8, 62, 63]),
            // The shipped cost, flags and furniture list (ADR-0203, ADR-0205).
            R(RoomTypeId.Workshop, "Work Shop", 600, WorkshopTerrain, 0x13ACu, 3, 3,
              objectIds: [16, 15, 46, 15, 16]),
            // The shipped cost and flags (ADR-0212); its furniture (71, 112) is not in the fixture.
            R(RoomTypeId.GuardRoom, "Guard Room", 600, GuardRoomTerrain, 0x10A4u),
        ],
        creatures: [Imp(), Goblin(), Vampire(), Skeleton(), BileDemon(), Knight(), Warlock(), Priest(), Thief(), Mistress(), .. extraCreatures],
        variables: Variables(),
        keeperSpells: KeeperSpells(),
        objects: Objects(),
        creatureSpells: CreatureSpells(),
        shots: Shots());

    /// <summary>Object id 22, the Goblin's bed.</summary>
    public const byte GoblinBedObjectId = 22;

    /// <summary>Object id 26, the Vampire's coffin.</summary>
    public const byte VampireBedObjectId = 26;

    /// <summary>Object id 64, the Library's book case: furniture, which no imp carries.</summary>
    public const byte BookCaseObjectId = 64;

    /// <summary>Object id 4, the Spell Book: <see cref="ObjectFlags.TypeSpellBook"/>, carried to
    /// the Library in state 31 (ADR-0183).</summary>
    public const byte SpellBookObjectId = 4;

    /// <summary>Create Imp's keeper spell id, the type id its availability rows use.</summary>
    public const byte CreateImpSpellId = 4;

    /// <summary>Object id 118, the Reveal Map Special: <see cref="ObjectFlags.TypeSpecial"/>,
    /// carried to the Library in state 33.</summary>
    public const byte RevealMapSpecialObjectId = 118;

    /// <summary>Object id 117, Level 1's Increase Level Special.</summary>
    public const byte IncreaseLevelSpecialObjectId = 117;

    /// <summary>Object id 5, the Alarm trap's crate: <see cref="ObjectFlags.TypeCrate"/>, carried
    /// to the Workshop in state 32.</summary>
    public const byte AlarmCrateObjectId = 5;

    /// <summary>Object id 52, the Wooden Door's crate, the shipped door record's <c>0x246</c> (ADR-0203).</summary>
    public const byte WoodenDoorCrateObjectId = 52;

    /// <summary>Object id 62, a Torture Chamber's wall furniture: a place, not a device.</summary>
    public const byte TortureCoalsObjectId = 62;

    /// <summary>Object id 14, the Training Room's floor furniture.</summary>
    public const byte TrainingDummyObjectId = 14;

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
    private static ObjectDef Carried(byte id, string name, ObjectFlags flags) => new()
    {
        Name = name, ObjectId = id, Flags = flags, Hp = 1, ManaValue = 0, RoomCapacity = 0,
        StartState = 0, PickUpPriority = 0, Width = new Fp32(4096), Height = new Fp32(4096),
        Mass = new Fp32(4096), Speed = new Fp32(0), NameStringId = 0, TooltipStringId = 0,
    };

    private static ObjectDef[] Objects() =>
    [
        // The Call to Arms flag, OBJECTS record 10 as shipped: start state 12, the hand may lift
        // it and put it down on any land, 128 health (call-to-arms.md).
        new ObjectDef
        {
            Name = "Call To Arms", ObjectId = CallToArmsRules.ObjectTypeId,
            Flags = ObjectFlags.CanBePickedUp | ObjectFlags.CanBeSlapped | ObjectFlags.CanBeDroppedOnAnyLand
                | ObjectFlags.BoulderCanRollThrough | ObjectFlags.Highlightable | ObjectFlags.CastShadows,
            Hp = 128, ManaValue = 0, RoomCapacity = 1, StartState = 12, PickUpPriority = 176,
            Width = new Fp32(1704), Height = new Fp32(127), Mass = new Fp32(4), Speed = new Fp32(0),
            NameStringId = 2725, TooltipStringId = 2726,
        },
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
            // 0x234000 as shipped: a boulder destroys it (boulder.md).
            Flags = ObjectFlags.Obstacle | ObjectFlags.BoulderCanRollThrough | ObjectFlags.BoulderDestroys | ObjectFlags.Damageable,
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

        // The three an imp carries, one per carry flag (ADR-0183). Ids and flags are the
        // shipped records' (ObjectsOnRealDataTests); nothing reads the other fields.
        Carried(SpellBookObjectId, "Spell Book", ObjectFlags.TypeSpellBook),
        Carried(RevealMapSpecialObjectId, "Special [ Reveal Map ]", ObjectFlags.TypeSpecial),
        Carried(IncreaseLevelSpecialObjectId, "Special [ Increase Level ]", ObjectFlags.TypeSpecial),
        Carried(AlarmCrateObjectId, "Crate Alarm", ObjectFlags.TypeCrate),
        Carried(WoodenDoorCrateObjectId, "Crate Wooden Door", ObjectFlags.TypeCrate),

        // The Library's furniture, the shipped Rooms.kwd ObjectIds for room 6. It carries
        // no carry flag, so no imp fetches it; the fields below are NOT transcribed.
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
        // The two torture devices, as shipped (ADR-0193).
        new ObjectDef
        {
            Name = "Torture Wheel", ObjectId = TortureRules.WheelObjectId,
            Flags = ObjectFlags.Obstacle | ObjectFlags.BoulderCanRollThrough | ObjectFlags.Highlightable,
            Hp = 1, ManaValue = 0, RoomCapacity = 0, StartState = 0, PickUpPriority = 176,
            Width = new Fp32(3686), Height = new Fp32(819), Mass = new Fp32(8192), Speed = new Fp32(0),
            NameStringId = 0, TooltipStringId = 0,
        },
        new ObjectDef
        {
            Name = "Electric Chair", ObjectId = TortureRules.ElectricChairObjectId,
            Flags = ObjectFlags.Obstacle | ObjectFlags.Highlightable,
            Hp = 1, ManaValue = 0, RoomCapacity = 0, StartState = 0, PickUpPriority = 176,
            Width = new Fp32(1083), Height = new Fp32(1228), Mass = new Fp32(8192), Speed = new Fp32(0),
            NameStringId = 0, TooltipStringId = 0,
        },

        // The Training Room's, the Workshop's, the Casino's and the Torture Chamber's wall
        // furniture, as shipped: what a room's capacity counts (ADR-0205).
        Furniture(TrainingDummyObjectId, "Training Dummy", 0x02004000u, 1, 2297),
        Furniture(29, "Training Target", 0x00014000u, 1, 2048),
        // The Boulder, OBJECTS record 74 as shipped: rolls through and destroys, 4000 health, a tile
        // wide, process state 17 (boulder.md).
        new ObjectDef
        {
            Name = "Boulder", ObjectId = BoulderRules.ObjectId, Flags = (ObjectFlags)0x238200u, Hp = 4000, ManaValue = 0,
            RoomCapacity = 0, StartState = BoulderRules.ProcessState, PickUpPriority = 0, Width = new Fp32(4096),
            Height = new Fp32(4096), Mass = new Fp32(0), Speed = new Fp32(0), NameStringId = 0, TooltipStringId = 0,
        },
        Furniture(15, "Anvil", 0x02014000u, 128, 2252),
        Furniture(16, "Steel Bucket", 0x02014000u, 128, 2252),
        Furniture(46, "Work Bench", 0x02004000u, 1, 3276),
        Furniture(87, "Roulette Table", 0x02014000u, 1, 3276),
        Furniture(59, "Barrel", 0x02014000u, 1, 1875),
        Furniture(67, "Bar Table", 0x02004000u, 1, 2451),
        Furniture(68, "Alcove Barrels", 0x00010000u, 1, 2457),
        Furniture(TortureCoalsObjectId, "Torture Coals", 0x00010000u, 1, 1228),
        Furniture(63, "Torture Water", 0x00010000u, 1, 1228),

        // The Casino's lever, as shipped (ADR-0188): an obstacle a hand could lift, which
        // nothing here lets it do.
        new ObjectDef
        {
            Name = "Casino Pay Out Adjuster",
            ObjectId = AutoFurnitureRules.CasinoLeverObjectId,
            Flags = ObjectFlags.CanBePickedUp | ObjectFlags.Obstacle,
            Hp = 1,
            ManaValue = 0,
            RoomCapacity = 0,
            StartState = 0,
            PickUpPriority = 1,
            Width = new Fp32(1685),
            Height = new Fp32(1228),
            Mass = new Fp32(8192),
            Speed = new Fp32(0),
            NameStringId = 2506,
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

    /// <summary>A room's furniture as shipped: the fields that differ between the eleven records
    /// transcribed, the rest being 0 in every one (<c>RoomFurnitureOnRealDataTests</c>).</summary>
    private static ObjectDef Furniture(byte id, string name, uint flags, int hp, int width) => new()
    {
        Name = name, ObjectId = id, Flags = (ObjectFlags)flags, Hp = (ushort)hp, ManaValue = 0,
        RoomCapacity = 0, StartState = 0, PickUpPriority = 176, Width = new Fp32(width),
        Height = new Fp32(hp == 128 ? 128 : 1228), Mass = new Fp32(8192), Speed = new Fp32(0),
        NameStringId = 0, TooltipStringId = 0,
    };

    /// <summary>Stands one piece of <paramref name="type"/>'s own furniture on
    /// <paramref name="at"/>, auto-placed, so the room has places: a room's capacity counts
    /// its auto-placed furniture (ADR-0205, <see cref="AutoFurnitureRules.TryTotalCapacity"/>).
    /// <b>Not a state DK2 reaches</b> when the room is smaller than 3x3, which gets no
    /// furniture; tests keep their one-tile rooms for the walk's sake, and furniture is not a
    /// steering obstacle here, so the piece changes nothing but the count.</summary>
    public static ObjectState Furnish(
        GameDatabase database, WorldState world, RoomTypeId type, TilePos at, byte owner)
    {
        ObjectState thing = world.SpawnObject(
            database, database.Room(type).ObjectIds[0], owner, (at.Y * world.Width) + at.X);
        thing.AutoPlaced = true;
        return thing;
    }

    /// <summary>What <see cref="DK2.Simulation.Systems.AutoFurnitureSystem"/> places, placed
    /// once: every room's own furniture, auto-placed. For tests whose rooms are big enough to
    /// get some and whose simulation does not run that system.</summary>
    public static void FurnishRooms(GameDatabase database, WorldState world)
    {
        var pieces = new List<(int TileIndex, byte ObjectTypeId)>();
        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            AutoFurnitureRules.DesiredFurniture(world, database, room, pieces);
            foreach ((int tile, byte type) in pieces)
            {
                if (type == HatcheryRules.CoopObjectId || database.ObjectById(type) is null) continue;
                world.SpawnObject(database, type, room.OwnerPlayerId, tile).AutoPlaced = true;
            }
        }
    }

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
        // Id, shot and bonus shot transcribed from the shipped record and guarded by
        // ResearchTreeOnRealDataTests: shot 2 makes an Imp (1) at level 1, and the bonus shot
        // an upgraded cast fires, 60, makes one at level 4. Location selector 1 and target
        // selector 4 are the shipped record's bytes 0xEF and 0xDA (ADR-0312).
        new()
        {
            Name = KeeperSpellName.CreateImp, ManaCost = 1500, ManaUpkeepPerSecond = 7,
            KeeperSpellId = CreateImpSpellId, LocationSelector = 1, TargetSelector = 4,
            ShotTypeId = 2, ShotData1 = 1, ShotData2 = 1,
            BonusShotTypeId = 60, BonusShotData1 = 1, BonusShotData2 = 4,
        },
        // Call to Arms as shipped: 10000 to cast, 90 a second to keep, no cooldown, location
        // selector 4 and target selector 4, the Create Object shot (5) putting object 10 down
        // whether or not upgraded (call-to-arms.md).
        new()
        {
            Name = KeeperSpellName.CallToArms, ManaCost = 10000, ManaUpkeepPerSecond = 90,
            KeeperSpellId = CallToArmsRules.SpellId, LocationSelector = 4, TargetSelector = 4,
            ShotTypeId = CreateObjectShotId, ShotData1 = CallToArmsRules.ObjectTypeId, ShotData2 = 0,
            BonusShotTypeId = CreateObjectShotId, BonusShotData1 = CallToArmsRules.ObjectTypeId, BonusShotData2 = 0,
        },
    ];

    /// <summary>Shot 5, Create Object, as shipped: flag 4 counts its one health down, so it
    /// dies on its first tick and its death process (0x80, type 2) puts the object down.</summary>
    public const byte CreateObjectShotId = 5;

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
        MeleeHitDelay = new Fp32(0),
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
        MeleeHitDelay = Fp32.FromDouble(0.75),
        MeleeRecharge = Fp32.FromDouble(1.3),
        // A test value, not the shipped Goblin's: five seconds stunned by Lightning (creature-status-effects.md §2).
        StunDuration = Fp32.FromDouble(5),
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

        // What a torture device does to it, as shipped (ADR-0193): 10 health an act, the
        // mood that only its own keeper's chamber applies, and 80 seconds to convert.
        TortureHpChange = -10,
        TortureMoodChange = 1092,
        TortureTimeToConvert = Fp32.FromDouble(80),

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

        // Its purse, as shipped: a wage's half and the Casino's winnings stop at 5000
        // (ADR-0187). InitialGoldHeld ships as 500 and nothing reads it, so it is 0 here.
        MaxGoldHeld = 5000,
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

    public const byte MistressCreatureId = 4;

    /// <summary>The Mistress, the one creature with the Torture job (ADR-0196): her shipped
    /// happy table, Train and Torture at 50 each, and her torture numbers, 0 health and 0 mood
    /// an act. Everything else is the Goblin's. <c>CreatureNeedsOnRealDataTests</c> guards the
    /// overrides.</summary>
    public static CreatureDef Mistress() => Goblin() with
    {
        Name = "Mistress",
        CreatureId = MistressCreatureId,
        Hp = 2000,
        HappyJobs = [new(JobType.Train, 10, 0, 50), new(JobType.Torture, 0, 0, 50)],
        UnhappyJobs = [new(JobType.Sulk, 0, 0, 10), new(JobType.Drink, 0, 0, 90)],
        TortureHpChange = 0,
        TortureMoodChange = 0,
        TortureTimeToConvert = Fp32.FromDouble(280),
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
        MaxGoldHeld = 0, // the shipped Skeleton's: it never takes gold (creature-table-dump.md)

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
        MeleeHitDelay = Fp32.FromDouble(1.25),
        MeleeRecharge = Fp32.FromDouble(3.5),
        Pay = 600,
        Fear = 300,
        Threat = 120,

        // The shipped Bile Demon is a Blocker, one of the fifteen heavy melee that pick the
        // nearest hostile (combat-target-selection.md §1); the fixture's one built arm.
        FightStyle = FightStyle.Blocker,

        HungerRate = Fp32.FromDouble(220),
        HungerFill = 5,
        HpFromChicken = 200,
        TimeAwake = 270,
        TimeSleep = 9,
        LairObjectId = 18,
    };

    public const byte KnightCreatureId = 20;

    /// <summary>A hero: the Goblin's record with the good alignment, the Blocker arm and no fear,
    /// so the alignment discount, a hero's own acquisition and the retaliation rules have a
    /// creature that stands its ground. The real Knight's stats are not these.</summary>
    public static CreatureDef Knight() => Goblin() with
    {
        Name = "Knight",
        CreatureId = KnightCreatureId,
        Flags = CreatureFlags.IsGood | CreatureFlags.GenerateDeadBody | CreatureFlags.CanWalkOnWater
                | CreatureFlags.CanBePickedUp | CreatureFlags.CanBeSlapped | CreatureFlags.IsFearless,
        FightStyle = FightStyle.Blocker,
        LairObjectId = 0,
    };

    public const byte WarlockCreatureId = 21;

    /// <summary>The Goblin's record with the Support arm and no fear, for the scores that prefer a
    /// crowded enemy and the Blitzer's dive at the backline. Not the real Warlock's stats.</summary>
    public static CreatureDef Warlock() => Goblin() with
    {
        Name = "Warlock",
        CreatureId = WarlockCreatureId,
        Flags = Goblin().Flags | CreatureFlags.IsFearless,
        FightStyle = FightStyle.Support,
        LairObjectId = 0,
        // The shipped Warlock's first two slots: Fireball from level 1 and Firebomb from level 8,
        // both winding up 11345/4096 s — eleven ticks (combat-spells.md §3).
        Spells =
        [
            new CreatureSpellSlot(FireballSpellId, 1, new Fp32(11345), 1),
            new CreatureSpellSlot(FirebombSpellId, 8, new Fp32(11345), 1),
        ],
    };

    public const byte PriestCreatureId = 22;
    public const byte ThiefCreatureId = 23;

    /// <summary>A Support with three friendly slots at their shipped words — Haste, Cast Armour and Heal
    /// — so the last castable slot wins (spell-friendly-arm.md §8.1). Not a shipped creature's stats.</summary>
    public static CreatureDef Priest() => Goblin() with
    {
        Name = "Priest", CreatureId = PriestCreatureId, Flags = Goblin().Flags | CreatureFlags.IsFearless,
        FightStyle = FightStyle.Support, LairObjectId = 0,
        Spells =
        [
            new CreatureSpellSlot(HasteSpellId, 1, new Fp32(4096), 1),
            new CreatureSpellSlot(ArmourSpellId, 1, new Fp32(4096), 1),
            new CreatureSpellSlot(HealSpellId, 1, new Fp32(4096), 1),
        ],
    };

    /// <summary>A fighter with the self-only Invisible (bit 7).</summary>
    public static CreatureDef Thief() => Goblin() with
    {
        Name = "Thief", CreatureId = ThiefCreatureId, Flags = Goblin().Flags | CreatureFlags.IsFearless, LairObjectId = 0,
        Spells = [new CreatureSpellSlot(InvisibleSpellId, 1, new Fp32(4096), 1)],
    };

    public const byte FireballSpellId = 1;
    public const byte FirebombSpellId = 2;
    public const byte HasteSpellId = 3;
    public const byte ArmourSpellId = 4;
    public const byte InvisibleSpellId = 5;
    public const byte HealSpellId = 6;
    public const byte ModifyHealthShotId = 4;
    public const byte HealInstantShotId = 53;
    public const byte FireballShotId = 1;
    public const byte FirebombShotId = 16;
    public const byte FirebombExplosionShotId = 34;

    /// <summary>The status shots at their shipped ids and flags (shots.md §9): the process type and
    /// the event that runs it. Their data words come from the spell that fires them.</summary>
    public const byte HasteShotId = 6;
    public const byte InvulnerableShotId = 7;
    public const byte FreezeShotId = 8;
    public const byte ArmourShotId = 9;
    public const byte InvisibleShotId = 22;
    public const byte LightningShotId = 30;
    public const byte SlowShotId = 38;
    public const byte DrainShotId = 42;
    public const byte ReaperFireballShotId = 52;
    public const byte PoisonSpitShotId = 62;

    private static readonly KwdArtResource NoArt = new("", 0, default, 0, 0, 0, 0);
    private static readonly KwdLight NoLight = new(0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>The Warlock's two attack spells at their shipped values (combat-spells.md §1–§2):
    /// attack flag, four-tile range, 5 s and 7.5 s recharges, 300 and 600 damage.</summary>
    public static CreatureSpellDef[] CreatureSpells() =>
    [
        new(new byte[266])
        {
            Name = "Warlock Fireball", EditorIcon = NoArt, GuiIcon = NoArt, CreatureSpellId = FireballSpellId,
            Flags = 0x101, Range = 16384, RechargeTime = 20480, ShotTypeId = FireballShotId, ShotData1 = 300, ShotData2 = 0,
            Unknown_00EF = default,
        },
        new(new byte[266])
        {
            Name = "Warlock Firebomb", EditorIcon = NoArt, GuiIcon = NoArt, CreatureSpellId = FirebombSpellId,
            Flags = 0x101, Range = 16384, RechargeTime = 30720, ShotTypeId = FirebombShotId, ShotData1 = 600, ShotData2 = 0,
            Unknown_00EF = default,
        },
        Friendly("Haste Creature", HasteSpellId, 0x104, SpellRules.HasteBuff, HasteShotId, 150, 15),
        Friendly("Cast Armour", ArmourSpellId, 0x104, SpellRules.ArmourBuff, ArmourShotId, 80, 50),
        Friendly("Invisible", InvisibleSpellId, 0x84, SpellRules.InvisibleBuff, InvisibleShotId, 15, 0),
        Friendly("Heal", HealSpellId, 0x104, SpellRules.HealBuff, HealInstantShotId, 3000, 0),
        // Flag carriers for the possessed spell arm's branches (keeper-spell-possession.md); not
        // shipped records: teleport (bit 5), speed (bit 6) and an attack firing shot type 30.
        new(new byte[266])
        {
            Name = "Test Teleport", EditorIcon = NoArt, GuiIcon = NoArt, CreatureSpellId = TeleportTestSpellId,
            Flags = 0x20, Range = 0, RechargeTime = 20480, ShotTypeId = 0, Unknown_00EF = default,
        },
        new(new byte[266])
        {
            Name = "Test Speed", EditorIcon = NoArt, GuiIcon = NoArt, CreatureSpellId = SpeedTestSpellId,
            Flags = 0x40, Range = 0, RechargeTime = 20480, ShotTypeId = 0, ShotData1 = 150, ShotData2 = 15, Unknown_00EF = default,
        },
        new(new byte[266])
        {
            Name = "Test Homing", EditorIcon = NoArt, GuiIcon = NoArt, CreatureSpellId = HomingTestSpellId,
            Flags = 0x101, Range = 16384, RechargeTime = 20480, ShotTypeId = LightningShotId, Unknown_00EF = default,
        },
    ];

    public const byte TeleportTestSpellId = 40;
    public const byte SpeedTestSpellId = 41;
    public const byte HomingTestSpellId = 42;
    public const byte PossessorCreatureId = 24;

    /// <summary>A Goblin carrying the three test flag spells, for the possessed spell arm. Pass it to
    /// <see cref="Database"/>; not a shipped creature.</summary>
    public static CreatureDef Possessor() => Goblin() with
    {
        Name = "Possessor", CreatureId = PossessorCreatureId, LairObjectId = 0,
        Spells =
        [
            new CreatureSpellSlot(TeleportTestSpellId, 1, new Fp32(4096), 1),
            new CreatureSpellSlot(SpeedTestSpellId, 1, new Fp32(4096), 1),
            new CreatureSpellSlot(HomingTestSpellId, 1, new Fp32(4096), 1),
        ],
    };

    /// <summary>A friendly spell (bit 2) with a four-tile range and a five-second recharge.</summary>
    private static CreatureSpellDef Friendly(string name, byte id, uint flags, byte alternativeShot, byte shot, uint data1, uint data2) =>
        new(new byte[266])
        {
            Name = name, EditorIcon = NoArt, GuiIcon = NoArt, CreatureSpellId = id, Flags = flags, Range = 16384, RechargeTime = 20480,
            ShotTypeId = shot, AlternativeShot = alternativeShot, ShotData1 = data1, ShotData2 = data2, Unknown_00EF = default,
        };

    /// <summary>The shots those spells fire, at their shipped values (shots.md §9): the Fireball dies
    /// on solid and on a thing and lives 60 ticks; the Firebomb chains its explosion on either hit;
    /// the explosion stands still and hurts a linear area for the ticks its health lasts.</summary>
    public static ShotDef[] Shots() =>
    [
        Status("Create Object", CreateObjectShotId, 2048, 1, 4, 0x80, 0, ShotRules.CreateObjectProcess, attackCategory: 12),
        new(new byte[239])
        {
            Name = "Fireball", MeshResource = NoArt, Light = NoLight, SoundCategory = "", ShotId = FireballShotId,
            Speed = 3686, Radius = 819, Health = 60, Flags = 0x127, ShotProcessFlags = 0x1, CollideType = 2, DamageType = 7,
            AttackCategory = 4, Unknown_0098 = 50,
        },
        new(new byte[239])
        {
            Name = "Firebomb", MeshResource = NoArt, Light = NoLight, SoundCategory = "", ShotId = FirebombShotId,
            Speed = 2048, Radius = 819, Health = 128, Flags = 0x123, ShotProcessFlags = 0xC, CollideType = 2, DamageType = 7,
            HitSolidShotId = FirebombExplosionShotId, HitThingShotId = FirebombExplosionShotId, AttackCategory = 4,
            Unknown_0098 = 250, Unknown_009C = 3,
        },
        new(new byte[239])
        {
            Name = "Fire Bomb Explosion", MeshResource = NoArt, Light = NoLight, SoundCategory = "", ShotId = FirebombExplosionShotId,
            Speed = 0, Radius = 819, Health = 3, Flags = 0x105, ShotProcessFlags = 0x1, CollideType = 2, DamageType = 2,
            AttackCategory = 4, Unknown_0098 = 250, Unknown_009C = 3,
        },
        Status("Increase Speed", HasteShotId, 3686, 1, 0x23, 0x104, 1, 4),
        Status("Invulnerable", InvulnerableShotId, 2048, 1, 0x23, 0x4, 1, 10),
        Status("Mistress Freeze", FreezeShotId, 4096, 50, 0x27, 0x4, 2, 11),
        Status("Modify Armour", ArmourShotId, 2048, 76, 0x27, 0x4, 1, 5),
        Status("Invisible", InvisibleShotId, 2048, 1, 0x26, 0x44, 1, 20),
        Status("Lightning Shot", LightningShotId, 0, 8, 0x104, 0x40, 0, 9),
        Status("Decrease Speed", SlowShotId, 3686, 1, 0x23, 0x104, 2, 4),
        Status("Drain", DrainShotId, 2048, 128, 0x27, 0x4, 2, 35),
        Status("Reaper Fireball", ReaperFireballShotId, 3686, 128, 0x123, 0x4, 2, 29, damageType: 7, attackCategory: 4),
        Status("Maiden Poison Spit", PoisonSpitShotId, 2048, 20, 0x123, 0x4, 2, 38),
        // The Heal chain (shots.md §8): the instant shot stands on its target and dies on it, chaining Modify Health.
        Status("Heal Instant Shot", HealInstantShotId, 0, 1, 0x2, 0x2, 1, 34, deathShot: ModifyHealthShotId),
        Status("Modify Health", ModifyHealthShotId, 3686, 128, 0x23, 0x4, 3, 3),
        // The trap shots, shipped: Cannon Ball and Door Fireball launch (flag 0x20) and hit on contact; the
        // Fireburst stands and burns an area; the Lightning Trap Shot strikes its target on its first tick;
        // the Freeze Trap Shot freezes what it hits; the Trigger's does nothing.
        Status("Cannon Ball", CannonBallShotId, 3276, 50, 0x127, 0x1, 2, 0, damageType: 7),
        Status("Fireburst", FireburstShotId, 2048, 16, 0x104, 0x1, 2, 0, damageType: 2),
        Status("Lightning Trap Shot", LightningTrapShotId, 0, 8, 0x104, 0x40, 0, 9),
        Status("Freeze Trap Shot", FreezeTrapShotId, 4096, 16, 0x3, 0x4, 2, 11),
        Status("Trigger Trap", TriggerTrapShotId, 4096, 1, 0x4, 0x1, 0, 0),
        Status("Door Fireball", DoorFireballShotId, 3686, 60, 0x127, 0x1, 2, 0, damageType: 7),
        // Read for ADR-0331: the Alarm's message (process 12), the Fear's stub (21), the Spike's square
        // (damage type 1) and the gas cloud's area damage with its process 31.
        Status("Alarm", AlarmShotId, 2048, 1, 0x4, 0x40, 0, ShotRules.AlarmProcess, attackCategory: 14),
        Status("Fear", FearShotId, 3686, 1, 0x24, 0x40, 2, ShotRules.FearProcess, attackCategory: 14),
        Status("Spike", SpikeShotId, 0, 1, 0x4, 0x1, 2, 0, damageType: 1, attackCategory: 13),
        Status("Gas Trap Cloud", GasTrapCloudShotId, 4096, 5, 0x4, 0x2, 2, ShotRules.GasProcess, damageType: 2, attackCategory: 7),
        // The Boulder's shot: launched, a tick long, process 22 on its first tick (boulder.md).
        Status("Boulder", BoulderShotId, 2048, 1, 0x24, 0x40, 3, ShotRules.BoulderProcess),
        // The Jack In The Box's shot, an area shot running process 39, and the Jack's explosion (jack-in-the-box.md).
        Status("Jack In The Box", JackInTheBoxShotId, 2048, 1, 0x24, 0x40, 2, ShotRules.JackInTheBoxProcess, damageType: 2, attackCategory: 13),
        new(new byte[239])
        {
            Name = "Jack In The Box - Explode", MeshResource = NoArt, Light = NoLight, SoundCategory = "", ShotId = JackExplosionShotId,
            Speed = 2048, Radius = 819, Health = 5, Flags = 0x104, ShotProcessFlags = 0x1, CollideType = 2, ProcessType = 0,
            DamageType = 2, AttackCategory = 4, Unknown_0098 = 20000, Unknown_009C = 3,
        },
    ];

    public const byte CannonBallShotId = 44, FireburstShotId = 14, LightningTrapShotId = 59, FreezeTrapShotId = 45,
        TriggerTrapShotId = 39, DoorFireballShotId = 57, AlarmShotId = 13, FearShotId = 25, SpikeShotId = 27, GasTrapCloudShotId = 58,
        BoulderShotId = 26, JackInTheBoxShotId = 64, JackExplosionShotId = 65;

    private static ShotDef Status(string name, byte id, int speed, ushort health, uint flags, uint processFlags, byte collide, byte process, byte damageType = 0, byte attackCategory = 0, byte deathShot = 0) =>
        new(new byte[239])
        {
            Name = name, MeshResource = NoArt, Light = NoLight, SoundCategory = "", ShotId = id, Speed = speed, Radius = 819,
            Health = health, Flags = flags, ShotProcessFlags = processFlags, CollideType = collide, ProcessType = process, DamageType = damageType, AttackCategory = attackCategory, DeathShotId = deathShot,
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
        Set(vars, VariableTypeId.ConvertNeutralRoomHealth, -20000);
        Set(vars, VariableTypeId.AttackRoomHealth, -1500);
        Set(vars, VariableTypeId.RepairRoomHealth, 20000);
        Set(vars, VariableTypeId.GoldMinedFromGems, 25);
        // Variable 74, the hands test's margin: 340 in every shipped level, absent from the global
        // file (imp-hands-full.md). A level 1 Imp's hands are full at 1400 less this.
        Set(vars, VariableTypeId.GameTicks, 340);

        // Fear (combat-fear.md): the back-off runs between these two, in ticks of the flee state;
        // a fleeing creature counts heads within the guard room's reporting distance of its rooms.
        Set(vars, VariableTypeId.BackOffMinimumDurationSeconds, 0);
        Set(vars, VariableTypeId.BackOffMaximumDurationSeconds, 30);
        Set(vars, VariableTypeId.GuardRoomReportingDistanceTiles, 7);
        // Call to Arms (call-to-arms.md): the fight gate's radius and the upgraded upkeep's percentage.
        Set(vars, VariableTypeId.CallToArmsFightDistanceTiles, 5);
        Set(vars, VariableTypeId.UpgradedCallToArmsCostPercentage, 70);
        // Possession's upgraded upkeep percentage (keeper-spell-possession.md §Upkeep), shipped value.
        Set(vars, VariableTypeId.UpgradedPossessionCostPercentage, 70);
        // The possessed spell scan's starting threshold (45 x 2048 / 360 = 256) and the follower cap.
        Set(vars, VariableTypeId.FirstPersonAccuracyAngleDegrees, 45);
        Set(vars, VariableTypeId.MaximumGroupNumberOfFollowers, 8);
        Set(vars, VariableTypeId.FirstPersonRechargeModifyPercentage, 100);
        // Guard duty's two deadlines (guard-room.md), shipped values.
        Set(vars, VariableTypeId.MaxTimeInGuardRoomBeforeWanderingToGuardPostSeconds, 60);
        Set(vars, VariableTypeId.GuardPostReportingDistanceTiles, 7);
        // The Combat Pit's cap and the damage a fighter takes (combat-pit.md), shipped values.
        Set(vars, VariableTypeId.CombatPitMaxExperienceLevel, 8);
        Set(vars, VariableTypeId.PitPercentageDamageTakenOfNormalCombat, 20);
        Set(vars, VariableTypeId.ImpIdleDelayBeforeReevaluationSeconds, 5);
        // The chase limit on a retreating target (combat-approach-and-victory.md §2): 3 s Good, 7 s Evil.
        Set(vars, VariableTypeId.GoodCreaturesChaseEnemyDurationSeconds, 3);
        Set(vars, VariableTypeId.EvilCreaturesChaseEnemyDurationSeconds, 7);

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
        Set(vars, VariableTypeId.ModifyAngerOfPitSpectatorPerSecond, -275);
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
        Set(vars, VariableTypeId.TriggerTrapTriggerSpeedTilesPerSecond, 4096);
        Set(vars, VariableTypeId.BoulderInitialHealth, 4000);
        Set(vars, VariableTypeId.BoulderSpeedTilesPerSecond, 4915);
        Set(vars, VariableTypeId.BoulderToCreatureDamagePercentage, 50);
        Set(vars, VariableTypeId.BoulderSpeedToHealthRatio, 50);
        Set(vars, VariableTypeId.BoulderFearAmount, 701);
        Set(vars, VariableTypeId.BoulderFearRange, 4);

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

        // Tired and not asleep: shipped at zero everywhere (ADR-0199).
        Set(vars, VariableTypeId.CannotSleepModifyCreatureHealthPerSecond, 0);

        // The cell's anger, charged in the same breath as the health (ADR-0171).
        // The shipped global is 150; campaign level 1 overrides it to 100, which is
        // why the rule reads it through the database rather than as a constant.
        Set(vars, VariableTypeId.ModifyAngerInCompanyOfHatedCreaturesPerSecond, 150);
        Set(vars, VariableTypeId.MotionlessInPrisonSeconds, 8);
        // Payday, as shipped: ten minutes between them, and the first one ten
        // minutes in rather than at zero (Q9, ADR-0078).
        Set(vars, VariableTypeId.PayDayFrequencySeconds, 600);
        Set(vars, VariableTypeId.TimeBeforeFirstPayDaySeconds, 600);
        Set(vars, VariableTypeId.PayDayCutOffTimeSeconds, 120);
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
        // The low-mana warning threshold and the warning/scan interval, as shipped (ADR-0277).
        Set(vars, VariableTypeId.LowManaWarningThreshold, 100);
        Set(vars, VariableTypeId.InsufficientManaImpEvaluationPeriodSeconds, 10);

        // The two the melee multiplier reads, as shipped. The Combat Pit's 100 is an
        // increment and not a scale, so a pit fighter hits twice as hard; the first-person
        // 12 is this project's hypothesis for a scalar nothing writes (Q337).
        Set(vars, VariableTypeId.CombatPitMeleeDamageModifierPercentage, 100);

        // The Casino's default mode, as shipped: a coin into the purse and -2000 anger a
        // second; the other mode's -8 is also the stake a purse must beat (ADR-0187).
        Set(vars, VariableTypeId.GamblingModifyCreatureMoneySmilesPerSecond, 1);
        Set(vars, VariableTypeId.GamblingModifyCreatureMoneyMoneyPerSecond, -8);
        Set(vars, VariableTypeId.GamblingModifyCreatureAngerSmilesPerSecond, -2000);
        Set(vars, VariableTypeId.GamblingModifyCreatureAngerMoneyPerSecond, 100);
        // A convert never dies of it, as shipped (ADR-0193).
        Set(vars, VariableTypeId.TortureChanceOfDyingWhenConverted, 0);
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
        // How much a purse holds, to 120% from level 5 (ADR-0187).
        Stat(vars, CreatureStatId.MaxGoldHeld, [105, 110, 115, 120, 120, 120, 120, 120, 120]);
        // How long a captive takes to convert, to 300% at level 10 (ADR-0193).
        Stat(vars, CreatureStatId.TortureTimeToConvert, [105, 110, 115, 120, 130, 140, 150, 200, 300]);

        // The dungeon heart's health and its regeneration, as shipped in the global
        // VARIABLES table (balance-variables-in-the-binary.md rows 174 and 131). A
        // keeper's world cannot be built without them (ADR-0162).
        Set(vars, VariableTypeId.DungeonHeartObjectHealth, 100000);
        Set(vars, VariableTypeId.DungeonHeartHealthRegenerationPerSecond, 25);
        // Variable 224's shipped MeleeDamage record, the possession bonus (Q337).
        vars.CreatureStats.Add(new CreatureStatEntry((int)CreatureStatId.MeleeDamage, 60, 0, FirstPerson: true));
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
