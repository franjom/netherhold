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

/// <summary>Creature definition flags: the first flag dword, at record offset 0xEFC.
/// CONFIRMED: creatures_struct.bt bit order.</summary>
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

    /// <summary>The hero side: across all 46 shipped creatures this bit is the exact complement of
    /// <see cref="IsEvil"/>, and the original's lair-preference test reads the pair as alignment —
    /// a creature avoids a lair holding a bed whose owner is of the other side. STRONGLY INFERRED
    /// as a name; the complement is CONFIRMED by <c>LairPreferenceOnRealDataTests</c>
    /// (Q151, docs/research/lair-acquisition.md §4).</summary>
    IsGood = 1u << 8,
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

/// <summary>The <b>second</b> creature flag field: one byte at record offset 0x1545, of which only
/// the low four bits are ever set across the 46 shipped creatures. Read as a dword because
/// 0x1545..0x1548 are the record's last four bytes and the upper three are zero on every creature,
/// so nothing is lost and a fifth bit appearing would be visible.
/// <para>The runtime offset the binary tests (<c>byte ptr [def + 0x68a] &amp; 8</c>) is no guide to
/// the file offset — the loader does not preserve record layout, and adjacency gave one wrong
/// answer. The byte was found by searching all 5449 bytes x 8 bits for a bit selecting the roster
/// the flag must select (<c>dk2tools kwd creaturebits</c>): exactly one does, bit 3 with mask 8, as
/// the binary tests it. ADR-0086, docs/research/portals-mercenary.md §3.</para></summary>
[Flags]
public enum CreatureFlags2 : uint
{
    /// <summary>King Reginald, Stone Knight and Jack: immune to whatever the impact routine at
    /// <c>0x004af1e0</c> applies, its only reader. That routine damages a target creature and
    /// doubles the damage when the target's tile carries terrain flag <c>+0x17b &amp; 0x80</c>;
    /// this bit makes it skip the target entirely. Which attack it is has <b>not</b> been
    /// established — the routine is installed into a table rather than called. Q210,
    /// ADR-0087.</summary>
    Unk0 = 1u << 0,

    /// <summary>Stone Knight alone, and <b>nothing reads it</b> — a bounded negative, not a failed
    /// search: no instruction in <i>either</i> executable block tests this byte's bit 1, and none
    /// reads a wider field at <c>+0x687</c>, <c>+0x688</c> or <c>+0x689</c> that would cover the
    /// byte without naming it. Being a flag and not a rate, there is no inert-shipped-value reading
    /// available either. Q210, ADR-0087.</summary>
    Unk1 = 1u << 1,

    /// <summary><b>This creature never becomes angry.</b> Skeleton and Jack. DK2 funnels every
    /// anger change through one wrapper whose first act is to test this bit and return, so a
    /// creature carrying it takes no anger from any source: not hunger, lair, wage, slap or hand.
    /// Three further readers gate on it, each skipping the same follow-on work — the two
    /// unhappiness messages (<c>0x11</c> and <c>0x12</c>, fired as the mood band crosses 1 and 2)
    /// and the event the second raises.
    /// <para>The flag is the rule and the record's values are a decoy: <b>Jack's four
    /// <c>angerNo*</c> fields are 120, 110, 60 and 50</b>, its <c>AngerInHand</c> +500 and its
    /// <c>AngerSlap</c> <b>−5000</b>, and not one is ever read. Reading it off the data instead —
    /// the Skeleton alone has four zeroed <c>angerNo*</c> fields — is right about the Skeleton by
    /// accident and wrong about Jack. ADR-0087, docs/research/creature-anger.md.</para></summary>
    NeverAngered = 1u << 2,

    /// <summary><b>A Hero Portal may generate this creature</b>: the hero side's counterpart to
    /// <see cref="CreatureFlags.AvailableViaPortal"/>. The two are exclusive by room type, not by
    /// owner — DK2 requires this bit when the room is type 25 and <c>AvailableViaPortal</c> when it
    /// is type 3, so neither portal can emit the other's creatures.
    /// <para>Nine creatures carry it: Knight, Giant, Wizard, Elven Archer, Thief, Monk, Fairy,
    /// Guard and Royal Guard — exactly the set
    /// <c>PortalOnRealDataTests.TheCampaignUnlocksNineCreaturesOnlyTheFlagGateKeepsOut</c> found
    /// from the campaign side. <b>The Dwarf is excluded</b>, where bit 30 of the first dword selects
    /// these nine <i>plus</i> the Dwarf and would have a Hero Portal emitting the heroes' worker;
    /// each side's portal excludes its own worker, the Imp likewise lacking
    /// <c>AvailableViaPortal</c>. ADR-0086, docs/research/portals-mercenary.md §3.</para></summary>
    AvailableViaHeroPortal = 1u << 3,
}
