// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>One-based campaign map identity; slot25 is the native unused placeholder.</summary>
public readonly record struct CampaignSlot
{
    private static readonly string?[] Names = { null, "level1", "level2", "level3", "level4", "level5", "level6a", "level6b",
        "level7", "level8", "level9", "level10", "level11a", "level11b", "level11c", "level12", "level13", "level14",
        "level15a", "level15b", "level16", "level17", "level18", "level19", "level20", null,
        "secret1", "secret2", "secret3", "secret4", "secret5" };
    public CampaignSlot(int value)
    {
        if (value is < 1 or > 30) throw new ArgumentOutOfRangeException(nameof(value));
        Value = (byte)value;
    }
    public byte Value { get; }
    public static CampaignSlot None => default;
    public bool IsCampaign => Value is >= 1 and <= 24;
    public bool IsSecret => Value is >= 26 and <= 30;
    public string? LevelName => Names[Value];
    public static CampaignSlot FromPickTag(byte tag) => tag switch
    {
        >= 1 and <= 30 => new(tag),
        >= 151 and <= 174 => new(tag - 150),
        _ => None,
    };
}
