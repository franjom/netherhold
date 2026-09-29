// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation;

namespace DK2.AI;

/// <summary>Creature values and current command permission captured for a keeper.</summary>
/// <param name="StateId">The current DK2 state (<c>+0x25E</c>).</param>
/// <param name="ResolvedStateId">The state through a walk (<c>0x00478050</c>): the resume state
/// while travelling, else the current one.</param>
/// <param name="MayFight">The fight gate <c>0x0045A6D0(0)</c> as the simulation represents it.</param>
/// <param name="MaxHealth">Level-scaled maximum health, the event's half and quarter basis.</param>
/// <param name="LastDroppedCounter">DK2's <c>+0x46D</c>: turns since spawn or the last drop, zero in the hand.</param>
/// <param name="InMyHand">Whether this keeper's hand holds it (<c>0x004BCAE0</c>).</param>
/// <param name="OwnershipRank">Position in the owner's acquisition-ordered list, or -1 when not listed.</param>
public readonly record struct KeeperCreatureView(
    int Id, byte CreatureTypeId, byte OwnerPlayerId, CreatureActivity Activity,
    int Health, int Level, long LastSlapTick, bool CanSlap,
    int PosX, int PosY, bool InHand, int CarriedByCreatureId, ushort RescuePriorityMask = 0,
    bool PickupPermitted = false, bool CanPickUp = false,
    int StateId = 0, int ResolvedStateId = 0, bool MayFight = false, int MaxHealth = 0,
    ushort LastDroppedCounter = 0, bool InMyHand = false, int OwnershipRank = -1);
