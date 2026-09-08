// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.Text;
using DK2.Data.Model;

namespace DK2.Data.Kwd;

/// <summary>
/// Readers for the definition tables completed for M1. Most field order and
/// widths are transcribed from OpenKeeper's GPLv3 reader and independently
/// constrained by the v1.70 shipped strides. Trigger parameter access is also
/// checked against DKII.EXE; see docs/research/trigger-semantics.md.
/// </summary>
public static class KwdAdditionalRecords
{
    public const int CreatureSpellSize = 266;
    public const int ShotSize = 239;
    public const int EffectSize = 246;
    public const int EffectElementSize = 182;

    public static List<CreatureSpellDef> ParseCreatureSpells(IEnumerable<KwdChunk> chunks) =>
        ParseFixed(chunks, KwdRecordType.CreatureSpells, CreatureSpellSize, ParseCreatureSpell);

    public static List<ShotDef> ParseShots(IEnumerable<KwdChunk> chunks) =>
        ParseFixed(chunks, KwdRecordType.Shots, ShotSize, ParseShot);

    public static List<EffectDef> ParseEffects(IEnumerable<KwdChunk> chunks) =>
        ParseFixed(chunks, KwdRecordType.Effects, EffectSize, ParseEffect);

    public static List<EffectElementDef> ParseEffectElements(IEnumerable<KwdChunk> chunks) =>
        ParseFixed(chunks, KwdRecordType.EffectElements, EffectElementSize, ParseEffectElement);

    private static List<T> ParseFixed<T>(IEnumerable<KwdChunk> chunks, KwdRecordType type,
        int stride, Func<byte[], T> parse)
    {
        var result = new List<T>();
        foreach (KwdChunk chunk in chunks)
        {
            if (chunk.Type != type) continue;
            if (chunk.ItemCount < 0 || chunk.Content.Length != chunk.ItemCount * stride)
                throw new InvalidDataException(
                    $"{type} content of {chunk.Content.Length} bytes does not contain " +
                    $"{chunk.ItemCount} records of {stride} bytes.");

            for (int i = 0; i < chunk.ItemCount; i++)
                result.Add(parse(chunk.Content.AsSpan(i * stride, stride).ToArray()));
        }
        return result;
    }

    private static CreatureSpellDef ParseCreatureSpell(byte[] r) => new(r)
    {
        Name = Ascii(r, 0, 32),
        EditorIcon = Art(r, 32),
        GuiIcon = Art(r, 116),
        ShotData1 = U32(r, 200), ShotData2 = U32(r, 204), Range = I32(r, 208),
        Flags = U32(r, 212), CombatPoints = U16(r, 216), SoundEvent = U16(r, 218),
        NameStringId = U16(r, 220), TooltipStringId = U16(r, 222),
        GeneralDescriptionStringId = U16(r, 224), StrengthStringId = U16(r, 226),
        WeaknessStringId = U16(r, 228), CreatureSpellId = r[230], ShotTypeId = r[231],
        AlternativeShotId = r[232], AlternativeRoomId = r[233], RechargeTime = I32(r, 234),
        AlternativeShot = r[238], Unknown_00EF = r.AsMemory(239, 27),
    };

    private static ShotDef ParseShot(byte[] r) => new(r)
    {
        Name = Ascii(r, 0, 32), MeshResource = Art(r, 32), Light = Light(r, 116),
        AirFriction = I32(r, 140), Mass = I32(r, 144), Speed = I32(r, 148),
        Unknown_0098 = U32(r, 152), Unknown_009C = U32(r, 156), ShotProcessFlags = U32(r, 160),
        Radius = I32(r, 164), Flags = U32(r, 168),
        GeneralEffectId = U16(r, 172), CreationEffectId = U16(r, 174),
        DeathEffectId = U16(r, 176), TimedEffectId = U16(r, 178),
        HitSolidEffectId = U16(r, 180), HitLavaEffectId = U16(r, 182),
        HitWaterEffectId = U16(r, 184), HitThingEffectId = U16(r, 186),
        Health = U16(r, 188), ShotId = r[190],
        DeathShotId = r[191], TimedDelay = r[192], HitSolidShotId = r[193],
        HitLavaShotId = r[194], HitWaterShotId = r[195], HitThingShotId = r[196],
        DamageType = r[197], CollideType = r[198], ProcessType = r[199], AttackCategory = r[200],
        SoundCategory = Ascii(r, 201, 32), Threat = U16(r, 233), BurnDuration = I32(r, 235),
    };

    private static EffectDef ParseEffect(byte[] r) => new(r)
    {
        Name = Ascii(r, 0, 32), ArtResource = Art(r, 32), Light = Light(r, 116),
        Mass = I32(r, 140), AirFriction = I32(r, 144), Elasticity = I32(r, 148), Radius = I32(r, 152),
        MinSpeedXy = I32(r, 156), MaxSpeedXy = I32(r, 160), MinSpeedYz = I32(r, 164),
        MaxSpeedYz = I32(r, 168), MinScale = I32(r, 172), MaxScale = I32(r, 176),
        Flags = U32(r, 180), EffectId = U16(r, 184), MinHp = U16(r, 186), MaxHp = U16(r, 188),
        FadeDuration = U16(r, 190), NextEffectId = U16(r, 192), DeathEffectId = U16(r, 194),
        HitSolidEffectId = U16(r, 196), HitWaterEffectId = U16(r, 198), HitLavaEffectId = U16(r, 200),
        GenerateIds = U16s(r, 202, 8), OuterOriginRange = U16(r, 218), LowerHeightLimit = U16(r, 220),
        UpperHeightLimit = U16(r, 222), OrientationRange = U16(r, 224), SpriteSpinRateRange = U16(r, 226),
        WhirlpoolRate = U16(r, 228), DirectionalSpread = U16(r, 230), CircularPathRate = U16(r, 232),
        InnerOriginRange = U16(r, 234), GenerateRandomness = U16(r, 236),
        Unknown_00EE = U16(r, 238), Unknown_00F0 = U16(r, 240),
        GenerationType = r[242],
        ElementsPerTurn = r[243], Unknown_00F4 = U16(r, 244),
    };

    private static EffectElementDef ParseEffectElement(byte[] r) => new(r)
    {
        Name = Ascii(r, 0, 32), ArtResource = Art(r, 32), Mass = I32(r, 116),
        AirFriction = I32(r, 120), Elasticity = I32(r, 124), MinSpeedXy = I32(r, 128),
        MaxSpeedXy = I32(r, 132), MinSpeedYz = I32(r, 136), MaxSpeedYz = I32(r, 140),
        MinScale = I32(r, 144), MaxScale = I32(r, 148), ScaleRatio = I32(r, 152),
        Flags = U32(r, 156), EffectElementId = U16(r, 160), MinHp = U16(r, 162), MaxHp = U16(r, 164),
        DeathElementId = U16(r, 166), HitSolidElementId = U16(r, 168), HitWaterElementId = U16(r, 170),
        HitLavaElementId = U16(r, 172), Red = r[174], Green = r[175], Blue = r[176],
        RandomColorIndex = r[177], TableColorIndex = r[178], FadePercentage = r[179],
        NextEffectId = U16(r, 180),
    };

    internal static KwdArtResource Art(byte[] r, int at) => new(
        Ascii(r, at, 64), U32(r, at + 64), r.AsMemory(at + 68, 12),
        r[at + 80], r[at + 81], r[at + 82], r[at + 83]);

    internal static KwdLight Light(byte[] r, int at) => new(
        I32(r, at), I32(r, at + 4), I32(r, at + 8), I32(r, at + 12), U32(r, at + 16),
        r[at + 20], r[at + 21], r[at + 22], r[at + 23]);

    internal static string Ascii(byte[] r, int at, int size)
    {
        ReadOnlySpan<byte> s = r.AsSpan(at, size);
        int end = s.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? s : s[..end]).Trim();
    }

    internal static string Utf16(byte[] r, int at, int characters)
    {
        ReadOnlySpan<byte> s = r.AsSpan(at, characters * 2);
        int bytes = 0;
        while (bytes + 1 < s.Length && (s[bytes] != 0 || s[bytes + 1] != 0)) bytes += 2;
        return Encoding.Unicode.GetString(s[..bytes]).Trim();
    }

    internal static ushort U16(byte[] r, int at) =>
        BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(at));
    internal static short I16(byte[] r, int at) =>
        BinaryPrimitives.ReadInt16LittleEndian(r.AsSpan(at));
    internal static uint U32(byte[] r, int at) =>
        BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(at));
    internal static int I32(byte[] r, int at) =>
        BinaryPrimitives.ReadInt32LittleEndian(r.AsSpan(at));
    internal static ushort[] U16s(byte[] r, int at, int count) =>
        Enumerable.Range(0, count).Select(i => U16(r, at + i * 2)).ToArray();
    internal static int[] I32s(byte[] r, int at, int count) =>
        Enumerable.Range(0, count).Select(i => I32(r, at + i * 4)).ToArray();
}

/// <summary>Field reader for all twelve THINGS payload tags.</summary>
public static class KwdThings
{
    public static List<ThingRecord> Parse(IEnumerable<KwdChunk> chunks)
    {
        var result = new List<ThingRecord>();
        foreach (KwdChunk chunk in chunks)
        {
            if (chunk.Type != KwdRecordType.Things) continue;
            foreach (KwdTaggedRecords.TaggedRecord tagged in KwdTaggedRecords.Split(chunk))
                result.Add(Parse(tagged.Tag, tagged.Payload.ToArray()));
        }
        return result;
    }

    public static ThingRecord Parse(int tag, byte[] r)
    {
        if (!KwdTaggedRecords.ThingPayloadSizes.TryGetValue(tag, out var layout))
            throw new InvalidDataException($"Unknown THINGS tag {tag}.");
        if (r.Length != layout.PayloadSize)
            throw new InvalidDataException($"THINGS {layout.Name} payload is {r.Length}, expected {layout.PayloadSize}.");

        return tag switch
        {
            194 => new ObjectThing(r) { X = I(r, 0), Y = I(r, 4), Unknown_0008 = r.AsMemory(8, 4), KeeperSpellId = I(r, 12), MoneyAmount = I(r, 16), TriggerId = U(r, 20), ObjectId = r[22], PlayerId = r[23] },
            195 => new TrapThing(r) { X = I(r, 0), Y = I(r, 4), Unknown_0008 = I(r, 8), NumberOfShots = r[12], TrapId = r[13], PlayerId = r[14], Unknown_000F = r[15] },
            196 => new DoorThing(r) { X = I(r, 0), Y = I(r, 4), Unknown_0008 = I(r, 8), TriggerId = U(r, 12), DoorId = r[14], PlayerId = r[15], Flags = r[16], Unknown_0011 = r.AsMemory(17, 3) },
            197 => new ActionPointThing(r) { StartX = I(r, 0), StartY = I(r, 4), EndX = I(r, 8), EndY = I(r, 12), WaitDelay = U(r, 16), Flags = U(r, 18), TriggerId = U(r, 20), ActionPointId = r[22], NextWaypointId = r[23], Name = KwdAdditionalRecords.Ascii(r, 24, 32) },
            198 => Creature198(r),
            199 => Creature199(r),
            200 => Creature200(r),
            201 => Party(r),
            202 => new DeadBodyThing(r) { X = I(r, 0), Y = I(r, 4), Z = I(r, 8), GoldHeld = U(r, 12), CreatureId = r[14], PlayerId = r[15] },
            203 => new EffectGeneratorThing(r) { X = I(r, 0), Y = I(r, 4), Unknown_0008 = I(r, 8), Unknown_000C = I(r, 12), Unknown_0010 = U(r, 16), Unknown_0012 = U(r, 18), EffectIds = Words(r, 20, 4), Frequency = r[28], GeneratorId = r[29], Unknown_001E = r.AsMemory(30, 6) },
            204 => new RoomThing(r) { X = I(r, 0), Y = I(r, 4), Unknown_0008 = I(r, 8), Unknown_000C = U(r, 12), Direction = r[14], Unknown_000F = r[15], InitialHealth = U(r, 16), RoomType = r[18], PlayerId = r[19] },
            205 => new CameraThing(r) { X = I(r, 0), Y = I(r, 4), Z = I(r, 8), MinClipX = I(r, 12), MinClipY = I(r, 16), MinClipZ = I(r, 20), MaxClipX = I(r, 24), MaxClipY = I(r, 28), MaxClipZ = I(r, 32), ViewDistance = I(r, 36), MinViewDistance = I(r, 40), MaxViewDistance = I(r, 44), Zoom = I(r, 48), MinZoom = I(r, 52), MaxZoom = I(r, 56), Lens = I(r, 60), MinLens = I(r, 64), MaxLens = I(r, 68), Flags = (uint)I(r, 72), AngleYaw = U(r, 76), AngleRoll = U(r, 78), AnglePitch = U(r, 80), CameraId = U(r, 82) },
            _ => throw new InvalidDataException($"Unsupported THINGS tag {tag}."),
        };
    }

    private static NeutralCreatureThing Creature198(byte[] r) => new(r)
    { X = I(r, 0), Y = I(r, 4), Z = I(r, 8), GoldHeld = U(r, 12), Level = r[14], Flags = r[15], InitialHealth = I(r, 16), TriggerId = U(r, 20), CreatureId = r[22], Unknown_0017 = r[23] };

    private static GoodCreatureThing Creature199(byte[] r) => new(r)
    { X = I(r, 0), Y = I(r, 4), Z = I(r, 8), GoldHeld = U(r, 12), Level = r[14], Flags = r[15], ObjectiveTargetActionPointId = I(r, 16), InitialHealth = I(r, 20), TriggerId = U(r, 24), ObjectiveTargetPlayerId = r[26], Objective = r[27], CreatureId = r[28], Unknown_001D = r.AsMemory(29, 2), Flags2 = r[31] };

    private static KeeperCreatureThing Creature200(byte[] r) => new(r)
    { X = I(r, 0), Y = I(r, 4), Z = I(r, 8), GoldHeld = U(r, 12), Level = r[14], Flags = r[15], InitialHealth = I(r, 16), ObjectiveTargetActionPointId = I(r, 20), TriggerId = U(r, 24), CreatureId = r[26], PlayerId = r[27] };

    private static HeroPartyThing Party(byte[] r)
    {
        var members = new List<GoodCreatureThing>(16);
        for (int i = 0; i < 16; i++)
            members.Add(Creature199(r.AsSpan(43 + i * 32, 32).ToArray()));
        return new HeroPartyThing(r) { Name = KwdAdditionalRecords.Ascii(r, 0, 32), TriggerId = U(r, 32), PartyId = r[34], Unknown_0023 = I(r, 35), Unknown_0027 = I(r, 39), Members = members };
    }

    private static int I(byte[] r, int at) => KwdAdditionalRecords.I32(r, at);
    private static ushort U(byte[] r, int at) => KwdAdditionalRecords.U16(r, at);
    private static ushort[] Words(byte[] r, int at, int count) => KwdAdditionalRecords.U16s(r, at, count);
}

/// <summary>Field reader for the two 16-byte TRIGGERS payload tags.</summary>
public static class KwdTriggers
{
    public static List<TriggerRecord> Parse(IEnumerable<KwdChunk> chunks)
    {
        var result = new List<TriggerRecord>();
        foreach (KwdChunk chunk in chunks)
        {
            if (chunk.Type != KwdRecordType.Triggers) continue;
            foreach (KwdTaggedRecords.TaggedRecord tagged in KwdTaggedRecords.Split(chunk))
            {
                byte[] r = tagged.Payload.ToArray();
                if (r.Length != 16) throw new InvalidDataException($"TRIGGERS tag {tagged.Tag} payload is {r.Length}, expected 16.");
                TriggerRecord record = tagged.Tag switch
                {
                    213 => new GenericTriggerRecord(r) { BodyValue = KwdAdditionalRecords.U32(r, 4), Parameters = GenericParameters(r[14], r), Body = r.AsMemory(0, 8), TriggerId = KwdAdditionalRecords.U16(r, 8), NextTriggerId = KwdAdditionalRecords.U16(r, 10), ChildTriggerId = KwdAdditionalRecords.U16(r, 12), Type = r[14], RepeatCount = r[15] },
                    214 => new ActionTriggerRecord(r) { BodyWord4 = KwdAdditionalRecords.U16(r, 4), BodyWord6 = KwdAdditionalRecords.U16(r, 6), BodyDword0 = KwdAdditionalRecords.U32(r, 0), BodyDword4 = KwdAdditionalRecords.U32(r, 4), Parameters = ActionParameters(r[14], r), Body = r.AsMemory(0, 8), TriggerId = KwdAdditionalRecords.U16(r, 8), NextTriggerId = KwdAdditionalRecords.U16(r, 10), ChildTriggerId = KwdAdditionalRecords.U16(r, 12), Type = r[14], RepeatCount = r[15] },
                    _ => throw new InvalidDataException($"Unknown TRIGGERS tag {tagged.Tag}."),
                };
                result.Add(record);
            }
        }
        return result;
    }

    private static IReadOnlyDictionary<string, long> GenericParameters(byte type, byte[] r)
    {
        var p = new Dictionary<string, long>();
        void B(string name, int at) => p[name] = r[at];
        void W(string name, int at) => p[name] = KwdAdditionalRecords.U16(r, at);
        void S(string name, int at) => p[name] = KwdAdditionalRecords.I32(r, at);
        void Unread() { S("Unreached_0000", 0); S("Unreached_0004", 4); }

        // Byte 1 is the flag read, byte 3 the flag compared against. Read off
        // the handler at 0x0051845B: MOV DL,CH takes byte 1 and indexes the
        // flag bank with it (MOV BL,[EBP + ESI + 0x3F70]), while byte 2 bit 0
        // selects the operand -- set means the immediate at +4, clear means
        // flags[byte 3] (MOV AL,[ECX + ESI + 0x3F70]). Identical in shape to
        // type 2, whose byte 1/byte 3 pair was corrected earlier; this one was
        // missed because only type 2 was checked.
        if (type == 1) { B("Comparison", 0); B("FlagId", 1); B("Flag", 2); B("IndirectFlagId", 3); S("Value", 4); }
        else if (type == 2) { B("Comparison", 0); B("TimerId", 1); B("Flag", 2); B("IndirectTimerId", 3); S("Value", 4); }
        else if (type is 3 or 4 or 6 or 8 or 10 or 12 or 13 or 16 or 33 or 43 or 46 or 56 or 57 or 58 or 59 or 60 or 61 or 65 or 72) Unread();
        else if (type is 19 or 20 or 21) { B("Comparison", 0); B("CreatureId", 1); W("Unreached_0002", 2); S("Value", 4); }
        else if (type == 22) { B("Comparison", 0); W("Unreached_0001", 1); B("PlayerId", 3); S("Value", 4); }
        else if (type == 23) { B("Comparison", 0); B("Unreached_0001", 1); B("Unknown_0002", 2); B("Unreached_0003", 3); S("Value", 4); }
        else if (type is 24 or 25 or 26 or 68) { B("Comparison", 0); B("RoomId", 1); W("Unreached_0002", 2); S("Value", 4); }
        else if (type is 27 or 28) { B("Comparison", 0); B("TargetId", 1); B("Flag", 2); B("Unreached_0003", 3); S("Value", 4); }
        else if (type == 29) { B("Unreached_0000", 0); B("TargetId", 1); W("Unreached_0002", 2); S("Unreached_0004", 4); }
        else if (type is 30 or 31 or 32 or 34 or 36 or 37 or 49 or 67 or 71 or 73) { B("Comparison", 0); B("Unreached_0001", 1); W("Unreached_0002", 2); S("Value", 4); }
        else if (type == 35) { B("Comparison", 0); B("CreatureId", 1); W("Unreached_0002", 2); S("Value", 4); }
        else if (type == 39) { B("Comparison", 0); B("TargetId", 1); B("TargetType", 2); B("PlayerId", 3); S("Value", 4); }
        else if (type == 40) { B("Comparison", 0); B("PlayerId", 1); B("TerrainId", 2); B("Unreached_0003", 3); S("Value", 4); }
        else if (type == 41) { B("Unreached_0000", 0); B("PlayerId", 1); B("TerrainId", 2); B("Unreached_0003", 3); S("Unreached_0004", 4); }
        else if (type == 42) { B("Comparison", 0); B("PlayerId", 1); B("TerrainId", 2); B("Unreached_0003", 3); S("Value", 4); }
        else if (type is 44 or 45 or 64) { B("Comparison", 0); B("PlayerId", 1); W("Unreached_0002", 2); S("Value", 4); }
        else if (type == 47) { B("CreatureId", 0); B("Unreached_0001", 1); W("Unreached_0002", 2); S("Unreached_0004", 4); }
        else if (type == 52) { B("Comparison", 0); W("Unreached_0001", 1); B("Level", 3); S("Value", 4); }
        else if (type == 53) { B("TargetType", 0); B("TargetId", 1); W("Unreached_0002", 2); S("Unreached_0004", 4); }
        else if (type == 62) { B("Comparison", 0); B("PlayerId", 1); W("Unreached_0002", 2); S("Value", 4); }
        else if (type == 69) { B("Unreached_0000", 0); B("PlayerId", 1); W("Unreached_0002", 2); S("Unreached_0004", 4); }
        else if (type == 70) { B("Unreached_0000", 0); B("TargetId", 1); B("TargetType", 2); B("PlayerId", 3); S("Unreached_0004", 4); }
        else if (type == 74) { B("Comparison", 0); B("CreatureId", 1); W("Unreached_0002", 2); S("Value", 4); }
        else { S("Unknown_0000", 0); S("Unknown_0004", 4); }
        return p;
    }

    private static IReadOnlyDictionary<string, long> ActionParameters(byte type, byte[] r)
    {
        var p = new Dictionary<string, long>();
        void B(string name, int at) => p[name] = r[at];
        void W(string name, int at) => p[name] = KwdAdditionalRecords.U16(r, at);
        void D(string name, int at) => p[name] = KwdAdditionalRecords.U32(r, at);
        void S16(string name, int at) => p[name] = KwdAdditionalRecords.I16(r, at);
        void Unread() { D("Unreached_0000", 0); D("Unreached_0004", 4); }

        if (type == 22) { B("TerrainId", 0); B("PlayerId", 1); W("Unreached_0002", 2); W("PosX", 4); W("PosY", 6); }
        else if (type == 32) { D("Unreached_0000", 0); W("PosX", 4); W("PosY", 6); }
        else if (type == 49) { B("Unreached_0000", 0); B("PlayerId", 1); W("Unreached_0002", 2); W("PosX", 4); W("PosY", 6); }
        else if (type == 18) { B("PlayerOneId", 0); B("PlayerTwoId", 1); B("Available", 2); B("Unreached_0003", 3); D("Unreached_0004", 4); }
        else if (type is 33 or 36 or 37 or 38 or 44 or 45 or 51) { B("Available", 0); B("Unreached_0001", 1); W("Unreached_0002", 2); D("Unreached_0004", 4); }
        else if (type == 50) { D("Unreached_0000", 0); D("Value", 4); }
        else if (type == 8) { B("TimerId", 0); B("Unreached_0001", 1); W("Unreached_0002", 2); D("Unreached_0004", 4); }
        else if (type == 23) { B("TimerId", 0); B("Unreached_0001", 1); W("Unreached_0002", 2); D("Value", 4); }
        else if (type == 7) { B("FlagId", 0); B("Flag", 1); W("Unreached_0002", 2); D("Value", 4); }
        else if (type == 6) { B("PlayerId", 0); B("MakeType", 1); B("TargetId", 2); B("Available", 3); D("Unreached_0004", 4); }
        else if (type == 46) { B("Available", 0); B("Unreached_0001", 1); W("Unreached_0002", 2); W("PosX", 4); W("PosY", 6); }
        else if (type == 26) { B("ActionPointId", 0); B("Unreached_0001", 1); W("Unreached_0002", 2); D("Unreached_0004", 4); }
        else if (type is 42 or 47) { B("TargetId", 0); B("Unreached_0001", 1); W("Unreached_0002", 2); D("Unreached_0004", 4); }
        else if (type == 4) { W("ObjectiveId", 0); W("Unreached_0002", 2); B("ActionPointId", 4); B("Unreached_0005", 5); W("Unreached_0006", 6); }
        else if (type == 24) { D("SpeechId", 0); D("Unknown_0004", 4); }
        else if (type == 25) Unread();
        else if (type == 30) { D("Value", 0); D("Unreached_0004", 4); }
        else if (type == 34) Unread();
        else if (type == 31) { B("PathId", 0); B("ActionPointId", 1); B("Available", 2); B("Unreached_0003", 3); D("Unreached_0004", 4); }
        else if (type == 9) { B("MakeType", 0); B("TargetId", 1); B("Available", 2); B("Unreached_0003", 3); D("Value", 4); }
        else if (type == 16) { B("ActionPointId", 0); B("Available", 1); W("Unreached_0002", 2); S16("Value", 4); W("Unreached_0006", 6); }
        else if (type == 17) { B("ActionPointId", 0); B("Available", 1); W("Unreached_0002", 2); D("Unreached_0004", 4); }
        else if (type == 27) { B("ActionPointId", 0); B("Available", 1); S16("Angle", 2); D("Time", 4); }
        else if (type == 1) { B("CreatureId", 0); B("PlayerId", 1); B("Level", 2); B("Flag", 3); W("PosX", 4); W("PosY", 6); }
        else if (type == 15) { B("PlayerId", 0); B("ObjectiveType", 1); W("Unreached_0002", 2); D("ActionPointId", 4); }
        else if (type == 14) { B("PartyId", 0); B("PartyType", 1); W("Unreached_0002", 2); B("ActionPointId", 4); B("Unreached_0005", 5); W("Unreached_0006", 6); }
        else if (type == 48) { B("GeneratorId", 0); B("Available", 1); W("Unreached_0002", 2); D("Unreached_0004", 4); }
        else if (type == 28) { B("CreatureId", 0); B("Level", 1); W("Unreached_0002", 2); D("Unreached_0004", 4); }
        else if (type is 3 or 5) Unread();
        else if (type == 2) { B("ObjectId", 0); B("PlayerId", 1); W("Unreached_0002", 2); W("PosX", 4); W("PosY", 6); }
        else if (type is 10 or 11 or 20 or 21 or 29 or 43) Unread();
        else if (type == 35) { B("Unknown_0000", 0); B("Unreached_0001", 1); W("Unreached_0002", 2); D("Unreached_0004", 4); }
        else if (type is 39 or 40) { B("Unknown_0000", 0); B("PlayerId", 1); W("Unreached_0002", 2); W("PosX", 4); W("PosY", 6); }
        else if (type == 41) { W("Unknown_0000", 0); W("Unreached_0002", 2); W("PosX", 4); W("PosY", 6); }
        else { D("Unknown_0000", 0); D("Unknown_0004", 4); }
        return p;
    }
}

/// <summary>Reader for LEVEL metadata and its typed 72-byte file-reference table.</summary>
public static class KwdLevel
{
    private const int BaseMetadataSize = 25603;
    private const int ExtendedMetadataSize = 25683;
    private const int PathSize = 72;

    public static LevelDef? Parse(IEnumerable<KwdChunk> chunks)
    {
        KwdChunk? chunk = chunks.SingleOrDefault(c => c.Type == KwdRecordType.Level);
        if (chunk is null) return null;
        byte[] r = chunk.Content;
        int metadata = Check(r, BaseMetadataSize) == 222 ? BaseMetadataSize
            : Check(r, ExtendedMetadataSize) == 222 ? ExtendedMetadataSize
            : throw new InvalidDataException("LEVEL metadata does not end at either shipped v1.70 boundary.");
        int height = KwdAdditionalRecords.U16(chunk.HeaderBlock, 2);
        int innerSize = checked((int)KwdAdditionalRecords.U32(r, metadata + 4));
        int expectedInner = checked(chunk.ItemCount * PathSize + height * 4);
        if (innerSize != expectedInner || r.Length != metadata + 8 + innerSize)
            throw new InvalidDataException($"LEVEL inner content is {innerSize} bytes; expected {expectedInner}.");

        var paths = new List<KwdLevelFilePath>(chunk.ItemCount);
        int at = metadata + 8;
        for (int i = 0; i < chunk.ItemCount; i++, at += PathSize)
            paths.Add(new KwdLevelFilePath((KwdRecordType)KwdAdditionalRecords.I32(r, at), KwdAdditionalRecords.I32(r, at + 4), KwdAdditionalRecords.Ascii(r, at + 8, 64)));
        uint[] tail = Enumerable.Range(0, height).Select(i => KwdAdditionalRecords.U32(r, at + i * 4)).ToArray();

        return new LevelDef(r)
        {
            Name = KwdAdditionalRecords.Utf16(r, 0, 64), Description = KwdAdditionalRecords.Utf16(r, 128, 1024),
            Author = KwdAdditionalRecords.Utf16(r, 2176, 64), Email = KwdAdditionalRecords.Utf16(r, 2304, 64),
            Information = KwdAdditionalRecords.Utf16(r, 2432, 1024), TriggerId = KwdAdditionalRecords.U16(r, 4480),
            TicksPerSecond = KwdAdditionalRecords.U16(r, 4482), Unknown_1184 = r.AsMemory(4484, 520),
            Messages = Enumerable.Range(0, 512).Select(i => KwdAdditionalRecords.Utf16(r, 5004 + i * 40, 20)).ToArray(),
            Flags = KwdAdditionalRecords.U16(r, 25484), SoundCategory = KwdAdditionalRecords.Ascii(r, 25486, 32),
            TalismanPieces = r[25518], PreviousRewards = r.AsMemory(25519, 4), NextRewards = r.AsMemory(25523, 4),
            SoundTrack = r[25527], TextTableId = r[25528], TitleTextId = KwdAdditionalRecords.U16(r, 25529),
            PlotTextId = KwdAdditionalRecords.U16(r, 25531), DebriefTextId = KwdAdditionalRecords.U16(r, 25533),
            ObjectiveTextId = KwdAdditionalRecords.U16(r, 25535), Unknown_63B9 = KwdAdditionalRecords.U16(r, 25537),
            SubObjective1TextId = KwdAdditionalRecords.U16(r, 25539), SubObjective2TextId = KwdAdditionalRecords.U16(r, 25541),
            SubObjective3TextId = KwdAdditionalRecords.U16(r, 25543), SpecialLevelIndex = KwdAdditionalRecords.U16(r, 25545),
            IntroductionOverrideObjects = r.AsMemory(25547, 8),
            IntroductionOverrideTextIds = KwdAdditionalRecords.U16s(r, 25555, 8), TerrainPath = KwdAdditionalRecords.Ascii(r, 25571, 32),
            Unknown_6403 = r.AsMemory(BaseMetadataSize, metadata - BaseMetadataSize), Paths = paths, UnknownPathTail = tail,
        };
    }

    private static uint Check(byte[] r, int at) => at + 4 <= r.Length ? KwdAdditionalRecords.U32(r, at) : 0;
}
