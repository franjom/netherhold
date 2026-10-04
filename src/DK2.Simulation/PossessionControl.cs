// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

public readonly record struct PossessionPose(FpPosition3 Position, Dk2Angle Heading)
{
    public void HashInto(ref Hash64 hash)
    {
        Position.HashInto(ref hash);
        hash.Add(Heading.Raw);
    }
}
