// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

public sealed class PossessionObjectDeathViewTests
{
    [Fact]
    public void NonCreatureDeathKeepsTheCameraPointAndFacesItDownOverTheFloor()
    {
        var from = new CameraFlightState(4096, 8192, 6000, 0, 4096, 49152, 2500, 512, 0);
        var preset = new CameraThing(new byte[84]) { Zoom = 0, Lens = 4096, ViewDistance = 49152 };
        CameraFlightState target = PossessionObjectDeathView.Target(from, preset, new Fp32(4096));
        Assert.Equal(4096, target.X);
        Assert.Equal(8192, target.Y);
        Assert.Equal(4096 + 0x100, target.Z);
        Assert.Equal(2500 & 0x7FF, target.Yaw);
        Assert.Equal(400, target.Pitch);
        Assert.Equal(200, target.Roll);
        Assert.Equal(0, target.ViewDistance);
    }
}
