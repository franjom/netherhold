// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Core;

public readonly record struct FpPosition2(Fp32 X, Fp32 Y)
{
    public TilePos Tile => new(X.Raw >> 12, Y.Raw >> 12);

    public void HashInto(ref Hash64 hash)
    {
        hash.Add(X.Raw);
        hash.Add(Y.Raw);
    }
}
