// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

public readonly record struct KwdTriggerId(ushort Value)
{
    public static KwdTriggerId None => new(0);
}
