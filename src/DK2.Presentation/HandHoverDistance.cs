// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Presentation;

public static class HandHoverDistance
{
    public static int Between(FpPosition3 pointer, FpPosition3 target)
    {
        int dx = Math.Abs(pointer.X.Raw - target.X.Raw), dy = Math.Abs(pointer.Y.Raw - target.Y.Raw);
        return Math.Max(dx, dy) + (Math.Min(dx, dy) >> 1);
    }
}
