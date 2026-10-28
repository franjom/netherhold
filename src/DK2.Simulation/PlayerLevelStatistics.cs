// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>The level-statistics block at <c>player+0x142</c> that is not kept elsewhere on
/// <see cref="PlayerState"/> (GoldMined, HeroesDestroyed and ItemsManufactured are): running
/// counters, then what <c>0x004BE630</c> samples at the verdict (docs/research/level-statistics.md).</summary>
public struct PlayerLevelStatistics
{
    /// <summary><c>+0x142</c>: hearts this player destroyed (<c>0x004DA7B2</c>).</summary>
    public int DungeonHeartsDestroyed;
    /// <summary><c>+0x146</c>: non-hero creatures of other owners killed (<c>0x00488311</c>).</summary>
    public int EnemyCreaturesKilled;
    /// <summary><c>+0x14E</c>: rooms taken over, bridges excepted (<c>0x004E5890</c>).</summary>
    public int RoomsTakenOver;
    /// <summary><c>+0x152</c>: slabs × per-tile cost / 100 of every room taken over (<c>0x004E58D1</c>).</summary>
    public int RoomExtraPoints;
    /// <summary><c>+0x16A</c>: creatures converted by torture (<c>0x004F28FC</c>).</summary>
    public int CreaturesConverted;
    /// <summary><c>+0x173</c>: this player's creatures that died (<c>0x00488336</c>).</summary>
    public int OwnCreaturesKilled;
    /// <summary><c>+0x177</c>: gold paid for room tiles and door and trap blueprints.</summary>
    public int MoneySpent;
    /// <summary><c>+0x17B</c>: research results delivered to a Library (<c>0x004C2FDC</c>).</summary>
    public int ThingsResearched;
    /// <summary><c>+0x183</c>: specials activated, plus those still held at the end.</summary>
    public int SpecialsFound;

    /// <summary>Set once <c>0x004BE630</c> has sampled the fields below.</summary>
    public bool Sampled;
    /// <summary><c>+0x156</c>: tiles owned at the end (DK2 keeps <c>DungeonArea</c> live).</summary>
    public int LandOwned;
    /// <summary><c>+0x15E</c>: mana at the end.</summary>
    public int ManaStored;
    /// <summary><c>+0x166</c>: creatures owned at the end.</summary>
    public int CreaturesCommanded;
    /// <summary><c>+0x16E</c>: mean creature level at the end, 20.12.</summary>
    public int CreatureTraining;
    /// <summary><c>+0x172</c>: 1 for status 2 (won), else 0.</summary>
    public bool LevelWon;
    /// <summary>The game turn of the verdict; <c>TimeTaken</c> (<c>+0x17F</c>) is this over the
    /// level's TicksPerSecond, which the simulation does not hold.</summary>
    public long EndTurn;
    /// <summary>The running counters as the verdict copied them (<c>0x004BEF0F</c>); play goes on after it.</summary>
    public LevelCounters AtVerdict;

    public readonly void HashInto(ref Hash64 hash)
    {
        hash.Add(DungeonHeartsDestroyed); hash.Add(EnemyCreaturesKilled); hash.Add(RoomsTakenOver);
        hash.Add(RoomExtraPoints); hash.Add(CreaturesConverted); hash.Add(OwnCreaturesKilled);
        hash.Add(MoneySpent); hash.Add(ThingsResearched); hash.Add(SpecialsFound);
        hash.Add(Sampled ? (byte)1 : (byte)0); hash.Add(LandOwned); hash.Add(ManaStored);
        hash.Add(CreaturesCommanded); hash.Add(CreatureTraining); hash.Add(LevelWon ? (byte)1 : (byte)0);
        hash.Add(EndTurn);
        AtVerdict.HashInto(ref hash);
    }

    public readonly void Write(BinaryWriter w)
    {
        w.Write(DungeonHeartsDestroyed); w.Write(EnemyCreaturesKilled); w.Write(RoomsTakenOver);
        w.Write(RoomExtraPoints); w.Write(CreaturesConverted); w.Write(OwnCreaturesKilled);
        w.Write(MoneySpent); w.Write(ThingsResearched); w.Write(SpecialsFound);
        w.Write(Sampled); w.Write(LandOwned); w.Write(ManaStored);
        w.Write(CreaturesCommanded); w.Write(CreatureTraining); w.Write(LevelWon); w.Write(EndTurn);
        AtVerdict.Write(w);
    }

    public static PlayerLevelStatistics Read(BinaryReader r) => new()
    {
        DungeonHeartsDestroyed = r.ReadInt32(), EnemyCreaturesKilled = r.ReadInt32(), RoomsTakenOver = r.ReadInt32(),
        RoomExtraPoints = r.ReadInt32(), CreaturesConverted = r.ReadInt32(), OwnCreaturesKilled = r.ReadInt32(),
        MoneySpent = r.ReadInt32(), ThingsResearched = r.ReadInt32(), SpecialsFound = r.ReadInt32(),
        Sampled = r.ReadBoolean(), LandOwned = r.ReadInt32(), ManaStored = r.ReadInt32(),
        CreaturesCommanded = r.ReadInt32(), CreatureTraining = r.ReadInt32(), LevelWon = r.ReadBoolean(),
        EndTurn = r.ReadInt64(), AtVerdict = LevelCounters.Read(r),
    };
}

/// <summary>The block's running counters at one moment, the three kept on <see cref="PlayerState"/> included.</summary>
public struct LevelCounters
{
    public int DungeonHeartsDestroyed, EnemyCreaturesKilled, HeroesDestroyed, RoomsTakenOver, RoomExtraPoints, GoldMined,
        ItemsManufactured, CreaturesConverted, OwnCreaturesKilled, MoneySpent, ThingsResearched, SpecialsFound;

    private readonly int[] Values() => [DungeonHeartsDestroyed, EnemyCreaturesKilled, HeroesDestroyed, RoomsTakenOver,
        RoomExtraPoints, GoldMined, ItemsManufactured, CreaturesConverted, OwnCreaturesKilled, MoneySpent, ThingsResearched,
        SpecialsFound];

    public readonly void HashInto(ref Hash64 hash) { foreach (int value in Values()) hash.Add(value); }
    public readonly void Write(BinaryWriter w) { foreach (int value in Values()) w.Write(value); }

    public static LevelCounters Read(BinaryReader r) => new()
    {
        DungeonHeartsDestroyed = r.ReadInt32(), EnemyCreaturesKilled = r.ReadInt32(), HeroesDestroyed = r.ReadInt32(),
        RoomsTakenOver = r.ReadInt32(), RoomExtraPoints = r.ReadInt32(), GoldMined = r.ReadInt32(),
        ItemsManufactured = r.ReadInt32(), CreaturesConverted = r.ReadInt32(), OwnCreaturesKilled = r.ReadInt32(),
        MoneySpent = r.ReadInt32(), ThingsResearched = r.ReadInt32(), SpecialsFound = r.ReadInt32(),
    };
}
