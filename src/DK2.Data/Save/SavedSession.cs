// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Data.Save;

/// <summary>Our save header; GameMode and ReturnPageId retain their byte/word on-disk widths.</summary>
public sealed record SavedSessionHeader(string LevelName, byte GameMode, ushort ReturnPageId,
    DateTime SavedAtUtc, string DisplayName);

public sealed record SavedSession(SavedSessionHeader Header, byte[] Snapshot, CampaignProfile Profile);
public sealed record SavedSessionEntry(string Path, SavedSessionHeader Header);
