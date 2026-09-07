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

    /// <summary>
    /// The hero side. Named on 2026-09-05 from two directions at once: across all
    /// 46 shipped creatures this bit is the exact complement of
    /// <see cref="IsEvil"/> (every hero, Prince to Royal Guard, and nothing
    /// evil), and the original's lair-preference test reads the pair as
    /// alignment — a creature avoids a lair holding a bed whose owner is of the
    /// other side (Q151, docs/research/lair-acquisition.md §4). STRONGLY
    /// INFERRED as a name; the complement is CONFIRMED by
    /// <c>LairPreferenceOnRealDataTests</c>.
    /// </summary>
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

/// <summary>
/// The <b>second</b> creature flag field: one byte at record offset 0x1545, of
/// which only the low four bits are ever set across the 46 shipped creatures.
/// Read as a dword because 0x1545..0x1548 are the record's last four bytes and
/// the upper three are zero on every creature — so nothing is lost and a fifth
/// bit appearing would be visible.
///
/// <para><b>Why there is a second flag field at all, and how it was found</b>
/// (2026-09-07, Q86 amendment 2). DK2's portal candidate builder tests two
/// different creature flags depending on the room type — bit 10 of the first
/// dword for a Portal, and <c>byte ptr [def + 0x68a] &amp; 8</c> for a Hero
/// Portal. The runtime offset is no guide to the file offset: the loader does
/// not preserve record layout, and reasoning from adjacency produced one wrong
/// answer (file 0xF00, which holds a 3000/2800 value and not flags).</para>
///
/// <para>What located it was the <i>roster</i>. The flag has to select the
/// creatures a Hero Portal generates, and <c>dk2tools kwd creaturebits</c>
/// searched all 5449 bytes x 8 bits for a set matching: exactly one byte's bit 3
/// does, and it is a byte tested with mask 8, as the binary tests it.</para>
///
/// <para>Bits 0 and 2 have named readers at the same runtime offset and the same
/// masks, which corroborates the mapping; what they mean is Q210. Bit 1 is set on
/// one creature and nothing was found reading it.</para>
/// </summary>
[Flags]
public enum CreatureFlags2 : uint
{
    /// <summary>
    /// King Reginald, Stone Knight and Jack — <b>immune to whatever the impact
    /// routine at <c>0x004af1e0</c> applies</b>, which is its only reader. That
    /// routine deals damage to a target creature and <b>doubles it</b> when the
    /// target's tile carries terrain flag <c>+0x17b &amp; 0x80</c>; this bit
    /// makes the routine skip the target entirely.
    ///
    /// <para>Which attack it is has <b>not</b> been established — the routine is
    /// installed into a table rather than called, so naming it means reading the
    /// table. Do not assume: a damage source that doubles on one terrain is
    /// suggestive, and suggestive is what bit 30 was. Q210.</para>
    /// </summary>
    Unk0 = 1u << 0,

    /// <summary>
    /// Stone Knight alone, and <b>nothing reads it</b>. Now a bounded negative
    /// rather than a failed search: no instruction in <i>either</i> executable
    /// block tests this byte's bit 1, and no instruction reads a wider field at
    /// <c>+0x687</c>, <c>+0x688</c> or <c>+0x689</c> that would cover the byte
    /// without naming it. It is a flag rather than a rate, so there is no
    /// shipped-value-makes-it-inert reading available either. Q210.
    /// </summary>
    Unk1 = 1u << 1,

    /// <summary>
    /// <b>This creature never becomes angry.</b> Skeleton and Jack. DK2 funnels
    /// every anger change in the game through one wrapper, and that wrapper's
    /// first act is to test this bit and return — so a creature carrying it
    /// takes no anger from any source: not from hunger, not from a missing lair,
    /// not from a missing wage, not from a slap, not from the hand.
    ///
    /// <para><b>This replaced an inference that was right about the Skeleton and
    /// wrong about Jack.</b> The rule used to be read off the data — the
    /// Skeleton is the only creature whose four <c>angerNo*</c> fields are all
    /// zero — which gives the correct answer for the Skeleton by an accident of
    /// its record. <b>Jack's four fields are 120, 110, 60 and 50</b>, with an
    /// <c>AngerInHand</c> of +500 and an <c>AngerSlap</c> of <b>−5000</b>, and
    /// not one of them is ever read. The values are a decoy; the flag is the
    /// rule.</para>
    ///
    /// <para>Three other readers gate on it besides the wrapper, each skipping
    /// the same follow-on work — the two unhappiness messages
    /// (<c>0x11</c> and <c>0x12</c>, fired as the mood band crosses 1 and 2) and
    /// the event the second one raises.</para>
    /// </summary>
    NeverAngered = 1u << 2,

    /// <summary>
    /// <b>A Hero Portal may generate this creature.</b> The hero side's
    /// counterpart to <see cref="CreatureFlags.AvailableViaPortal"/>, and the
    /// two are exclusive by room type rather than by owner: DK2 requires this
    /// bit when the room is type 25 and requires <c>AvailableViaPortal</c> when
    /// it is type 3, so neither portal can ever emit the other's creatures.
    ///
    /// <para>Nine creatures carry it: Knight, Giant, Wizard, Elven Archer,
    /// Thief, Monk, Fairy, Guard and Royal Guard. That is <i>exactly</i> the set
    /// <c>PortalOnRealDataTests.TheCampaignUnlocksNineCreaturesOnlyTheFlagGateKeepsOut</c>
    /// found from the other direction — the creatures the campaign makes
    /// available to keepers that only the Portal's flag gate keeps out. The two
    /// flags partition the portal-generated roster between the sides.</para>
    ///
    /// <para><b>The Dwarf is excluded</b>, and that is the detail that makes the
    /// bit trustworthy. A neighbouring flag (bit 30 of the first dword) selects
    /// these nine <i>plus</i> the Dwarf; gating on that would have a Hero Portal
    /// emitting the heroes' worker. Each side's portal excludes its own worker —
    /// the Imp likewise lacks <c>AvailableViaPortal</c>.</para>
    /// </summary>
    AvailableViaHeroPortal = 1u << 3,
}
