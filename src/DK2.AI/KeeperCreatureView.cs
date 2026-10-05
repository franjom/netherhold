// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.AI;

/// <summary>Creature values and current command permission captured for a keeper.</summary>
/// <param name="StateId">The current DK2 state; <paramref name="ResolvedStateId"/> is it through a walk.</param>
/// <param name="MayFight">The fight gate <c>0x0045A6D0(0)</c> as the simulation represents it.</param>
/// <param name="MaxHealth">Level-scaled maximum health, the event's half and quarter basis.</param>
/// <param name="LastDroppedCounter">DK2's <c>+0x46D</c>: turns since spawn or the last drop, zero in the hand.</param>
/// <param name="InMyHand">Whether this keeper's hand holds it (<c>0x004BCAE0</c>); <paramref name="OwnershipRank"/> is its place in the owner's list, -1 unlisted.</param>
/// <param name="UnderCallToArms"><c>0x004888C0</c>: the owner's flag, bit 20 or states 190 to 192 through a walk.</param>
/// <param name="FightTargetOwner">The owner of its engagement's target (<c>+0x502</c>), or 0 with none.</param>
/// <param name="CallToArmsSiteAllowed">For an own fighter: the place validators pass at its tile; <paramref name="HeartRouteExists"/>: a walker routes from two tiles east of the heart to it.</param>
/// <param name="SpellTargetAllowed">For a foreign creature: <c>0x004BBA40</c>'s validators pass for Thunderbolt, <paramref name="ChickenTargetAllowed"/> for Chicken, and <paramref name="InfernoPlaceAllowed"/> <c>0x004BB8A0</c>'s for Inferno at its tile; <paramref name="HealTargetAllowed"/>: for an own creature, for Heal.</param>
/// <param name="CombatPitFighter">Its <c>+0x3E6</c> bit 15, the Combat Pit's fighter mark.</param> <param name="AttachedToRoom">Whether it holds a room id (<c>+0x3B9</c>, <see cref="RoomMembershipRules"/>).</param> <param name="Experience">Experience toward the next level (<c>+0x3FF</c>); <paramref name="DroppedToDoJob"/> is <c>+0x2BC</c>, the job of the room the hand last dropped it on.</param>
/// <param name="Anger">The six counters at <c>+0x26E</c>, from which make-happy reads the band and largest bucket.</param>
public readonly record struct KeeperCreatureView(
    int Id, byte CreatureTypeId, byte OwnerPlayerId, CreatureActivity Activity,
    int Health, int Level, long LastSlapTick, bool CanSlap,
    int PosX, int PosY, bool InHand, int CarriedByCreatureId, ushort RescuePriorityMask = 0,
    bool PickupPermitted = false, bool CanPickUp = false,
    int StateId = 0, int ResolvedStateId = 0, bool MayFight = false, int MaxHealth = 0,
    ushort LastDroppedCounter = 0, bool InMyHand = false, int OwnershipRank = -1, bool UnderCallToArms = false,
    byte FightTargetOwner = 0, bool CallToArmsSiteAllowed = false, bool HeartRouteExists = false,
    bool SpellTargetAllowed = false, bool CombatPitFighter = false, bool AttachedToRoom = false,
    bool HealTargetAllowed = false, bool ChickenTargetAllowed = false, bool InfernoPlaceAllowed = false,
    int Experience = 0, JobType DroppedToDoJob = JobType.None, CreatureAnger Anger = default);
