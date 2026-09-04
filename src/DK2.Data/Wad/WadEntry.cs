// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Wad;

/// <summary>One file stored inside a DK2 WAD archive.</summary>
public sealed record WadEntry
{
    public required string Name { get; init; }
    public required uint DataOffset { get; init; }
    /// <summary>Bytes stored in the archive (compressed size when compressed).</summary>
    public required uint StoredSize { get; init; }
    /// <summary>Bytes after extraction (== StoredSize when uncompressed).</summary>
    public required uint UncompressedSize { get; init; }
    public required bool IsCompressed { get; init; }
}
