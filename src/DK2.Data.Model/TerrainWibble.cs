// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>
/// The eight bytes before a terrain's id (file <c>0x1CE..0x1D5</c>, runtime <c>+0x1A1..+0x1A8</c>,
/// STRONGLY INFERRED from the loader's shift; kwd-terrain-rooms.md): how far the terrain lattice
/// jitters (wibble) and leans its points, horizontally and vertically, per layer 0 (wall top),
/// 1 (middle) and 2 (floor surface). Presentation only.
/// </summary>
public readonly record struct TerrainWibble(
    byte WibbleH, byte LeanH0, byte LeanH1, byte LeanH2,
    byte WibbleV, byte LeanV0, byte LeanV1, byte LeanV2)
{
    public byte LeanH(int layer) => layer switch { 0 => LeanH0, 1 => LeanH1, _ => LeanH2 };

    public byte LeanV(int layer) => layer switch { 0 => LeanV0, 1 => LeanV1, _ => LeanV2 };
}
