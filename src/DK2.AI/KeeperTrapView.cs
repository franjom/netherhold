// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.AI;

/// <summary>A placed trap's tile coordinates, owner, definition id and definition threat.</summary>
public readonly record struct KeeperTrapView(byte TrapTypeId, byte OwnerPlayerId, int X, int Y, ushort Threat);
