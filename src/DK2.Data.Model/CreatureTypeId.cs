// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>A creature definition id, distinct from a player or a live creature's identity.</summary>
public readonly record struct CreatureTypeId(byte Value)
{
    public static implicit operator CreatureTypeId(byte value) => new(value);
}
