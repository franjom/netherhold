// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.AI;

/// <summary>The nine built-in personalities a skirmish or multiplayer lobby gives a computer
/// keeper (KEEPER_AI.md §3.2). Names by index are OpenKeeper's <c>AIType</c>.</summary>
public enum KeeperPersonality : byte
{
    MasterKeeper = 0,
    Conqueror = 1,
    Psychotic = 2,
    Stalwart = 3,
    Greyman = 4,
    Idiot = 5,
    Guardian = 6,
    ThickSkinned = 7,
    Paranoid = 8,
}

/// <summary>The personality setter <c>0x004F8C60</c>: the lobby's personality, after the level-name
/// remap at <c>0x006A30A0</c>, selects one row of seven parallel tables in <c>.rdata</c> (rooms
/// <c>0x0066DF18</c>, traps and doors <c>0x0066DFE0</c>, initial attack <c>0x0066E050</c>, battles
/// <c>0x0066E168</c>, creatures <c>0x0066E208</c>, spells <c>0x0066E2B8</c>, misc <c>0x0066E300</c>).
/// The room list is computed from the room definitions, the build area is the whole map less its
/// edge, and <c>Flags</c> is forced on, as the campaign loader forces it.</summary>
/// <remarks>The parameters come back in the same form <c>KwdPlayers.ParseBlock</c> gives a campaign
/// keeper, so <see cref="KeeperAI"/> treats both alike (KEEPER_AI.md §6, quirk 8). Fields the setter
/// does not write keep the defaults of <c>0x004F8360</c> (§3.3); <c>EditorAiType</c> is the chosen
/// personality, and <c>RawBlock</c> is empty, as no PLAYERS block exists.</remarks>
[Dk2Behavior(Confidence.Confirmed, "docs/KEEPER_AI.md")]
public static class KeeperAIPersonality
{
    public const int Count = 9;

    /// <summary>The setter takes the first this many qualifying room definitions.</summary>
    public const int RoomListLength = 15;

    /// <summary>A remap entry's id list ends at this value, or after eight ids.</summary>
    private const int RemapListEnd = 10;
    private const int RemapListMax = 8;

    /// <summary>The level-name remap at <c>0x006A30A0</c>, in table order. The map name is compared
    /// case-sensitively; a personality in the entry's list is replaced, any other kept.</summary>
    private static readonly (string Map, KeeperPersonality Replacement, byte[] Replaced)[] Remaps =
    [
        ("Spiral", KeeperPersonality.Conqueror, [0, 3, 4]),
        ("Pass", KeeperPersonality.Conqueror, [0, 3, 4]),
        ("Caverns", KeeperPersonality.Psychotic, [0, 1, 3, 4, 6, 7]),
        ("Clover", KeeperPersonality.Psychotic, [0, 1, 3, 6, 7]),
        ("Olympia", KeeperPersonality.MasterKeeper, [2]),
        ("OnSlaught", KeeperPersonality.MasterKeeper, [2, 3, 4, 5, 6, 7, 8]),
        ("Siege", KeeperPersonality.MasterKeeper, [5, 7, 8]),
        ("Frosty's Castle", KeeperPersonality.MasterKeeper, [4, 6, 8]),
        ("Frosty's Lair", KeeperPersonality.MasterKeeper, [4, 5]),
        ("Frosty's Realm", KeeperPersonality.MasterKeeper, [6]),
        ("Furnace", KeeperPersonality.MasterKeeper, [6]),
        ("No Mans Land", KeeperPersonality.MasterKeeper, [4, 6]),
    ];

    // The tables' values, one column per field, indexed by personality. Each comment names the table
    // and the byte offset within its row; the setter's masks are no-ops on every shipped value.

    // rooms, 22-byte rows
    private static readonly byte[] MinimumRandomTime = [0, 15, 30, 0, 15, 30, 0, 15, 30];      // +0
    private static readonly bool[] CreateEmptyAreas = [false, false, false, false, false, false, false, false, false]; // +1 (i32)
    private static readonly bool[] BiggerLair = [true, false, false, false, false, false, false, false, false];     // +5 (i32)
    private static readonly byte[] DefaultRoomSize = [25, 16, 9, 25, 16, 12, 25, 16, 12];   // +9
    private static readonly byte[] TilesLeftBetweenRooms = [2, 1, 0, 1, 1, 1, 1, 1, 1];    // +10
    private static readonly bool[] SellTakenOver = [false, false, false, false, false, false, false, false, false]; // +11 (i32)
    private static readonly bool[] PlaceNextToIdeal = [false, false, true, false, false, true, false, true, true]; // +15
    private static readonly byte[] BuildLostRoomAfter = [0, 0, 0, 0, 0, 0, 0, 0, 0]; // +16
    private static readonly byte[] CorridorStyle = [1, 0, 0, 1, 0, 2, 2, 2, 0];            // +17
    private static readonly byte[] BuildNewRoomWhenMoreSpace = [0, 0, 1, 0, 0, 1, 0, 0, 1]; // +18
    private static readonly byte[] NeutralRangeFromHeart = [25, 20, 40, 25, 20, 15, 15, 14, 13]; // +19
    private static readonly byte[] NeutralRangeFromClaimed = [4, 3, 5, 3, 3, 3, 2, 2, 1];  // +20
    private static readonly byte[] Flexibility = [0, 0, 0, 0, 0, 0, 0, 0, 0];              // +21

    // traps and doors, 12-byte rows
    private static readonly byte[] AggressiveVsDefensive = [2, 2, 2, 2, 2, 2, 2, 2, 2];   // +0
    private static readonly byte[] TrapVsDoor = [2, 2, 2, 2, 2, 2, 2, 2, 2];              // +1
    private static readonly byte[] DoorPreference = [0, 0, 1, 0, 0, 1, 0, 2, 1];          // +2
    private static readonly bool[] BoulderLongCorridors = [true, true, false, true, true, false, true, true, false]; // +3 (i32)
    private static readonly bool[] BoulderBreachRoute = [true, true, false, true, false, false, true, true, false]; // +7 (i32)
    private static readonly byte[] TrapAndDoorUsage = [99, 70, 30, 99, 50, 25, 99, 50, 25]; // +11

    // initial attack, 31-byte rows
    private static readonly byte[] MinimumCreatures = [15, 10, 5, 15, 15, 15, 4, 4, 4];   // +0
    private static readonly byte[] SkillLevel = [8, 5, 0, 8, 5, 2, 0, 0, 0];             // +1
    private static readonly byte[] ThreatSuperiority = [20, 125, 0, 100, 100, 100, 200, 200, 200]; // +6
    private static readonly byte[] FirstEnd = [2, 0, 3, 2, 1, 3, 2, 1, 3];               // +19
    private static readonly byte[] FirstStart = [3, 3, 2, 0, 1, 2, 0, 3, 2];             // +20
    private static readonly byte[] BreachAtXPoints = [2, 1, 1, 2, 1, 1, 1, 1, 1];        // +21
    private static readonly byte[] UseCreatures = [50, 75, 100, 50, 40, 30, 40, 20, 10]; // +22
    private static readonly bool[] OnlyAttackAttackers = [false, false, false, false, false, true, true, true, true]; // +23 (i32)
    private static readonly bool[] NeverAttack = [false, false, false, false, false, false, false, true, true]; // +27 (i32)
    // +11 IAAllRoomsPlaced and +15 IAAllSpellsResearched (i32) are 0 in every row.

    // battles, 17-byte rows
    private static readonly byte[] BAThreatSuperiority = [125, 100, 50, 125, 110, 90, 150, 150, 150]; // +0
    private static readonly byte[] SpellAttackVsDefence = [2, 2, 0, 2, 2, 2, 2, 2, 2];    // +1
    private static readonly ushort[] CallToArmsValue = [2000, 1500, 1000, 2150, 2000, 2500, 2000, 2500, 3500]; // +2 (u16)
    private static readonly byte[] ImprisonPercentage = [5, 5, 0, 15, 8, 25, 15, 20, 25]; // +4
    private static readonly bool[] RemoveIfCantReinforce = [true, true, false, true, true, false, true, true, true]; // +5 (i32)
    private static readonly byte[] RetreatHealth = [10, 10, 10, 20, 20, 20, 30, 30, 30];  // +9
    private static readonly byte[] RemoveCtaEnemy = [1, 1, 1, 2, 2, 2, 5, 5, 5];          // +12
    private static readonly byte[] RemoveCtaOwn = [10, 5, 1, 10, 10, 10, 4, 4, 4];         // +14
    // +10 RemoveCallToArmsAfter 240, +13 RemoveCallToArmsWithinRange 5, +15 WaitBeforeAttackingAgain 90.

    // creatures, 19-byte rows
    private static readonly bool[] UnhappyHappy = [true, false, false, true, false, false, true, false, false]; // +0 (i32)
    private static readonly bool[] AngryHappy = [true, true, false, true, true, false, true, true, false]; // +4 (i32)
    private static readonly bool[] AngryDispose = [false, false, true, false, false, true, false, false, true]; // +8 (i32)
    private static readonly byte[] AngryDisposalMethod = [1, 1, 0, 1, 1, 0, 1, 1, 0];     // +12
    private static readonly bool[] UnwantedDispose = [true, false, false, true, false, false, true, false, false]; // +13 (i32)
    private static readonly byte[] MaximumImps = [20, 15, 10, 15, 10, 5, 12, 8, 4];       // +17

    // spells, 8-byte rows
    private static readonly byte[] UseSightOfEvil = [2, 1, 2, 2, 1, 2, 2, 1, 1];          // +0
    private static readonly bool[] LightningInWater = [true, false, false, true, false, false, true, false, false]; // +2 (i32)
    private static readonly byte[] CallToArmsUsage = [1, 1, 2, 0, 0, 2, 0, 1, 2];         // +7
    // +1 UseKeeperSpells 2 and +6 SpellPreference 2 in every row.

    // misc, 18-byte rows
    private static readonly byte[] MoveForResearch = [0, 1, 2, 0, 1, 2, 0, 1, 2];         // +0
    private static readonly ushort[] TendancyMoney = [9000, 6000, 6000, 9000, 6000, 6000, 9000, 6000, 6000]; // +1 (u16)
    private static readonly byte[] SpecialRange = [10, 8, 15, 8, 5, 2, 5, 4, 3];          // +3
    private static readonly byte[] SpecialFindChance = [90, 50, 20, 90, 50, 20, 90, 50, 20]; // +5
    private static readonly byte[] TilesPerImpRatio = [15, 25, 35, 15, 20, 25, 15, 15, 15]; // +6
    private static readonly byte[] ImprisonedAction = [0, 2, 1, 0, 2, 1, 0, 3, 1];        // +7
    private static readonly byte[] Openness = [50, 50, 50, 20, 20, 20, 15, 15, 15];       // +13
    // +4 SpecialSearchChance 0, +8 starting mana 100 (i32), +12 TendancySpeed 1, +14 ManaValue and
    // +16 GoldValue 100 (u16) in every row.

    /// <summary>The personality the setter uses on <paramref name="mapName"/>: the first remap entry
    /// whose name equals it (ordinal) replaces a listed personality; no entry keeps it.</summary>
    public static KeeperPersonality Remap(KeeperPersonality personality, string mapName)
    {
        foreach ((string map, KeeperPersonality replacement, byte[] replaced) in Remaps)
        {
            if (!string.Equals(map, mapName, StringComparison.Ordinal)) continue;
            for (int i = 0; i < replaced.Length && i < RemapListMax && replaced[i] != RemapListEnd; i++)
                if (replaced[i] == (byte)personality) return replacement;
            return personality;
        }
        return personality;
    }

    /// <summary><c>0x004F8C60</c>: the parameters of <paramref name="personality"/> on the named map,
    /// for a world of <paramref name="width"/> by <paramref name="height"/> tiles.</summary>
    public static KeeperAIParameters Parameters(KeeperPersonality personality, string mapName, GameDatabase database,
        int width, int height)
    {
        if ((byte)personality >= Count) throw new ArgumentOutOfRangeException(nameof(personality));
        int p = (byte)Remap(personality, mapName);

        // The skill level is capped at CombatPitMaxExperienceLevel (byte 0x006F2976, variable 192).
        byte skill = SkillLevel[p];
        if (database.Variables.TryGet(VariableTypeId.CombatPitMaxExperienceLevel, out int cap) && cap <= skill)
            skill = (byte)cap;

        return new KeeperAIParameters
        {
            EditorAiType = (byte)personality,
            MinimumRandomTimeBeforePlacingReseachedRoom = MinimumRandomTime[p],
            CreateEmptyRoomShapeAreasWhenIdle = CreateEmptyAreas[p],
            AutomaticallyBuildBiggerLairWhenPortalTakenOver = BiggerLair[p],
            DefaultRoomSize = DefaultRoomSize[p],
            TilesLeftBetweenRooms = TilesLeftBetweenRooms[p],
            SellRecentlyTakenOverRoom = SellTakenOver[p],
            PlaceRoomsNextToIdeal = PlaceNextToIdeal[p],
            BuildLostRoomAfterMinimumTime = BuildLostRoomAfter[p],
            CorridorStyle = CorridorStyle[p],
            BuildNewRoomWhenMoreSpaceRequired = BuildNewRoomWhenMoreSpace[p],
            NeutralRoomRangeFromHeart = NeutralRangeFromHeart[p],
            NeutralRoomRangeFromClaimedArea = NeutralRangeFromClaimed[p],
            Flexibility = Flexibility[p],
            RoomList = RoomList(database),

            AggressiveVsDefensiveTrapPreference = AggressiveVsDefensive[p],
            TrapVsDoorPreference = TrapVsDoor[p],
            DoorPreference = DoorPreference[p],
            BoulderTrapsOnLongCorridors = BoulderLongCorridors[p],
            BoulderTrapOnBreachRoute = BoulderBreachRoute[p],
            TrapAndDoorUsage = TrapAndDoorUsage[p],

            IAMinimumCreatures = MinimumCreatures[p],
            IACreatureSkillLevel = skill,
            IAThreatSuperiority = ThreatSuperiority[p],
            IAAllRoomsPlaced = false,
            IAAllSpellsResearched = 0,
            IAFirstEnd = FirstEnd[p],
            IAFirstStart = FirstStart[p],
            BreachAtXPoints = BreachAtXPoints[p],
            IAUseCreatures = UseCreatures[p],
            IAOnlyAttackAttackers = OnlyAttackAttackers[p],
            NeverAttack = NeverAttack[p],

            BAThreatSuperiority = BAThreatSuperiority[p],
            AttackingVsDefensiveSpellPreference = SpellAttackVsDefence[p],
            BACallToArmsValue = CallToArmsValue[p],
            ImprisonPercentage = ImprisonPercentage[p],
            BARemoveIfCantReinforce = RemoveIfCantReinforce[p],
            BARetreatHealth = RetreatHealth[p],
            RemoveCallToArmsAfter = 240,
            RemoveCallToArmsEnemyCreatures = RemoveCtaEnemy[p],
            RemoveCallToArmsWithinRange = 5,
            RemoveCallToArmsOwnCreatures = RemoveCtaOwn[p],
            WaitBeforeAttackingAgain = 90,

            UnhappyCreaturesHappy = UnhappyHappy[p],
            AngryCreaturesHappy = AngryHappy[p],
            AngryCreaturesDisposeOf = AngryDispose[p],
            AngryDisposalMethod = AngryDisposalMethod[p],
            UnwantedCreatureDisposeOf = UnwantedDispose[p],
            MaximumImps = MaximumImps[p],

            UseSightOfEvil = UseSightOfEvil[p],
            UseKeeperSpells = 2,
            LightningInWater = LightningInWater[p],
            SpellPreference = 2,
            CallToArmsUsage = CallToArmsUsage[p],

            ProbabilityOfMovingCreatureForResearch = MoveForResearch[p],
            TendancyMoney = TendancyMoney[p],
            SpecialRange = SpecialRange[p],
            SpecialSearchChance = 0,
            SpecialFindChance = SpecialFindChance[p],
            TilesPerImpRatio = TilesPerImpRatio[p],
            ImprisonedCreatureAction = ImprisonedAction[p],
            StartingMana = 100,
            TendancySpeed = 1,
            Openness = Openness[p],
            ManaValue = 100,
            GoldValue = 100,

            // (1, 1) to (width - 1, height - 1). Flags (+0x96) is the word 0xFFFF; ours keeps its low
            // byte, as the record path reads it (six bits are used).
            MaxBuildAreaMinX = 1,
            MaxBuildAreaMinY = 1,
            MaxBuildAreaMaxX = (ushort)(width - 1),
            MaxBuildAreaMaxY = (ushort)(height - 1),
            Flags = 0xFF,
            RawBlock = ReadOnlyMemory<byte>.Empty,
        };
    }

    /// <summary>The setter's room list: walking the room definitions by index (world slot
    /// <c>+0x90</c>, then <c>+0x98</c> by id), the first <see cref="RoomListLength"/> that are
    /// <see cref="RoomFlags.Buildable"/> (<c>+0x43D</c> bit 7), a Portal (3) or a Hero Portal (25).</summary>
    /// <remarks>Slot <c>+0x98</c> searches downward from <c>id - 1</c> over a list sorted by id, so
    /// ours takes the definitions in id order. Past the last definition <c>+0x90</c> answers 0,
    /// <c>+0x98</c> finds no definition and the setter faults; the shipped rooms fill the list.
    /// Deviates: ours fills a shortfall with 0.</remarks>
    public static byte[] RoomList(GameDatabase database)
    {
        var list = new byte[RoomListLength];
        int n = 0;
        foreach (RoomDef room in database.Rooms.Values.OrderBy(r => r.RoomId))
        {
            if (n == RoomListLength) break;
            if (Qualifies(room)) list[n++] = room.RoomId;
        }
        return list;

        static bool Qualifies(RoomDef room) =>
            room.Flags.HasFlag(RoomFlags.Buildable)
            || room.RoomId == (byte)RoomTypeId.Portal || room.RoomId == (byte)RoomTypeId.HeroPortal;
    }
}
