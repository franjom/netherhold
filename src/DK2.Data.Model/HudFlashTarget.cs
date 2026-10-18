// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>The category/button pair in a FLASH_BUTTON trigger, packed in file byte order.</summary>
public readonly record struct HudFlashTarget(ushort Value)
{
    public static readonly HudFlashTarget Mana = new(0xF200);
    public static readonly HudFlashTarget Gold = new(0xF300);
    public byte Category => (byte)Value;
    public byte Button => (byte)(Value >> 8);
}
