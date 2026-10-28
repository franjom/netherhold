// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.AI;

/// <summary>A placed door's tile coordinates, owner and definition id.</summary>
public readonly record struct KeeperDoorView(byte DoorTypeId, byte OwnerPlayerId, int X, int Y);
