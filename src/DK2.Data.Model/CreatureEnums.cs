// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>Creature job/activity ids. CONFIRMED: ufdada/dk2-tools creatures_struct.bt.</summary>
public enum JobType
{
    None = 0, Sleep = 1, Eat = 2, Research = 3, Train = 4, Manufacture = 5,
    Guard = 6, Torture = 7, Praying = 8, Drink = 9, Leave = 10,
    DestroyEnemyRooms = 11, DestroyWalls = 12, StealGold = 13, StealSpells = 14,
    Sulk = 15, Rebel = 16, StealManufactureCrates = 17, KillCreatures = 18,
    KillPlayer = 19, Tunnelling = 20, Wait = 22, SendToActionPoint = 23,
    Explore = 24, StealEnemyGold = 25, CombatPitSpectate = 26, JailBreak = 27,
    Tolling = 28,
}

/// <summary>Attack/shot type ids. CONFIRMED: creatures_struct.bt.</summary>
public enum AttackType : byte
{
    None = 0, MeleeBlade = 1, MeleeBlunt = 2, MeleeBody = 3,
    MistressLightning = 4, GasCloud = 5, GasMissile = 6, DarkElfGuidedBolt = 7,
    Slow = 8, Grenade = 9, Drain = 10, MistressHailStorm = 11, DarkElfArrow = 12,
    HasteCreature = 13, WarlockHealCreature = 14, MeleeScythe = 15,
    Invulnerable = 16, ThiefInvisible = 17, Knives = 18, Disruption = 19,
    RaiseDead = 20, Wind = 21, SkeletonArmy = 22, ChickenArrow = 23,
    ElfArcherArrow = 24, WizardFireball = 25, SalamanderFireball = 26,
    WizardFirebomb1 = 27, WizardFirebomb2 = 28, FairyFreeze = 29,
    ElvenArcherGuidedBolt = 30, DarkAngelHailStorm = 31, MonkHealCreature = 32,
    RogueInvisible = 33, FairyLightning = 34, DarkAngelFirebomb = 35,
    ImpTeleport = 36, ImpHaste = 37, ReaperFireball = 38, SalamanderSpit = 39,
    MaidenWeb = 40, MaidenPoisonSpit = 41,
}

/// <summary>CONFIRMED: creatures_struct.bt.</summary>
public enum JobClass : byte { Artisan = 0, Thinker = 1, Fighter = 2, Scout = 3 }

/// <summary>CONFIRMED: creatures_struct.bt.</summary>
public enum FightStyle : byte { NonFighter = 0, Blitzer = 1, Blocker = 2, Flanker = 3, Support = 4 }

/// <summary>
/// Creature definition flags (first flag dword, offset 0xEFC).
/// CONFIRMED: creatures_struct.bt bit order.
/// </summary>
[Flags]
public enum CreatureFlags : uint
{
    IsWorker = 1u << 0,
    CanBePickedUp = 1u << 1,
    CanBeSlapped = 1u << 2,
    AlwaysFlee = 1u << 3,
    CanWalkOnWater = 1u << 4,
    CanWalkOnLava = 1u << 5,
    CanDisarmTraps = 1u << 6,
    IsEvil = 1u << 7,
    Unk8 = 1u << 8,
    IsImmuneToTurncoat = 1u << 9,
    AvailableViaPortal = 1u << 10,
    Unk11 = 1u << 11,
    Unk12 = 1u << 12,
    CanFly = 1u << 13,
    IsHorny = 1u << 14,
    GenerateDeadBody = 1u << 15,
    CanBeHypnotized = 1u << 16,
    IsImmuneToChicken = 1u << 17,
    IsFearless = 1u << 18,
    CanBeElectrocuted = 1u << 19,
    NeedBodyForFightIdle = 1u << 20,
    NotTrainWhenIdle = 1u << 21,
    OnlyAttackableByHorny = 1u << 22,
    CanBeResurrected = 1u << 23,
    DoesntGetAngryWithEnemies = 1u << 24,
    FreesFriendsOnJailBreak = 1u << 25,
    RevealsAdjacentTraps = 1u << 26,
    IsUnique = 1u << 27,
    CameraRollsWhenTurning = 1u << 28,
    Unk29 = 1u << 29,
    MpdRandomInvader = 1u << 30,
    IsMale = 1u << 31,
}
