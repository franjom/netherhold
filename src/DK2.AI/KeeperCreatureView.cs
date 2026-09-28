// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation;

namespace DK2.AI;

/// <summary>Creature values and current command permission captured for a keeper.</summary>
public readonly record struct KeeperCreatureView(
    int Id, byte CreatureTypeId, byte OwnerPlayerId, CreatureActivity Activity,
    int Health, int Level, long LastSlapTick, bool CanSlap,
    int PosX, int PosY, bool InHand, int CarriedByCreatureId);
