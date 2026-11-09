// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>An object definition id (<see cref="ObjectDef"/>), distinct from a creature type or a
/// live object's identity.</summary>
public readonly record struct ObjectTypeId(byte Value)
{
    public static implicit operator ObjectTypeId(byte value) => new(value);
}
