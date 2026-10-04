// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Core;

public readonly record struct FpPosition3(Fp32 X, Fp32 Y, Fp32 Z)
{
    public void HashInto(ref Hash64 hash)
    {
        hash.Add(X.Raw);
        hash.Add(Y.Raw);
        hash.Add(Z.Raw);
    }
}

public readonly record struct Dk2Angle(ushort Raw)
{
    public int Value => Raw & 0x7FF;
}
