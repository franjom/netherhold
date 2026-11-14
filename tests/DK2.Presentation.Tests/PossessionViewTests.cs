// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation.Tests;

/// <summary>The possessed view's frame step (possession-client.md §2 and §3): the numbers below are
/// worked from the read of <c>0x0040E910</c>, not from a recording.</summary>
public sealed class PossessionViewTests
{
    private const int Floor = 0x1000, Eye = 0x600, Width = 0x200;

    /// <summary>An open map; chosen tiles refuse a step.</summary>
    private sealed class World(params (int X, int Y)[] walls) : IPossessionWorld
    {
        public int Width => 20;
        public int Height => 20;
        public bool BlockEverything { get; set; }
        public bool CanStepTo(int tileX, int tileY) => Array.IndexOf(walls, (tileX, tileY)) < 0;
        public (int X, int Y) ResolveAgainstThings(int startX, int startY, int wantedX, int wantedY) => (wantedX, wantedY);
        public bool Blocked(int x, int y) => BlockEverything;
        public int FloorHeight(int x, int y) => Floor;
        public bool WetAt(int x, int y) => false;
    }

    /// <summary>Walk 100, run 200, shuffle 50 a turn: at four turns a second a 100 ms frame's unit is 40, 80 or 20.</summary>
    private static PossessedBody Body(bool creeping = false, bool flying = false, int waddle = 0, int bob = 0, bool leans = false) =>
        new(100, 200, 50, creeping, flying, false, false, Width, Eye, waddle, bob, 4, leans, 0);

    private static PossessionView At(int x, int y, int yaw = 0)
    {
        var view = new PossessionView();
        view.Enter(x, y, Floor, Body() with { Heading = yaw });
        return view;
    }

    private static readonly PossessionKeys Forward = new(true, false, false, false, false, false);

    [Fact]
    public void EnteringPutsTheCameraAtEyeHeightOnTheHorizon_StronglyInferredFromBinary()
    {
        PossessionView view = At(0x5800, 0x5800, yaw: 0x123);

        Assert.Equal((0x5800, 0x5800, Floor + Eye), (view.X, view.Y, view.Z));
        Assert.Equal((0x123, PossessionView.Horizon, 0), (view.Yaw, view.Pitch, view.Roll));
    }

    [Theory]
    [InlineData(0, 0, -40)]         // yaw 0 faces minus Y
    [InlineData(0x200, 40, 0)]      // a quarter turn faces plus X
    public void ForwardMovesOneUnitAlongTheYaw_StronglyInferredFromBinary(int yaw, int dx, int dy)
    {
        PossessionView view = At(0x5800, 0x5800, yaw);

        view.Step(100, Forward, 0, 0, false, Body(), new World());

        Assert.Equal((0x5800 + dx, 0x5800 + dy), (view.X, view.Y));
    }

    [Theory]
    [InlineData(false, true, false, false, false, 0, 20)]      // back: half a unit
    [InlineData(true, true, false, false, false, 0, -20)]      // both: minus half a unit, not zero
    [InlineData(false, false, false, true, false, 26, 0)]      // strafe right: two thirds
    [InlineData(false, false, true, false, false, -26, 0)]
    [InlineData(true, false, false, false, true, 0, -80)]      // run
    public void TheKeysScaleTheUnit_StronglyInferredFromBinary(bool forward, bool back, bool left, bool right, bool run, int dx, int dy)
    {
        PossessionView view = At(0x5800, 0x5800);

        view.Step(100, new PossessionKeys(forward, back, left, right, run, false), 0, 0, false, Body(), new World());

        Assert.Equal((0x5800 + dx, 0x5800 + dy), (view.X, view.Y));
    }

    [Fact]
    public void ACreepingCreatureShufflesWhateverTheRunKeySays_StronglyInferredFromBinary()
    {
        PossessionView view = At(0x5800, 0x5800);

        view.Step(100, Forward with { Run = true, Creep = true }, 0, 0, false, Body(creeping: true), new World());

        Assert.Equal(0x5800 - 20, view.Y);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, true, null)]
    [InlineData(false, false, null)]
    public void TheCreepKeyAsksOnlyForAChange_StronglyInferredFromBinary(bool key, bool creeping, bool? asked)
    {
        PossessionView view = At(0x5800, 0x5800);

        PossessionFrame frame = view.Step(16, default(PossessionKeys) with { Creep = key }, 0, 0, false, Body(creeping), new World());

        Assert.Equal(asked, frame.SetCreeping);
    }

    [Fact]
    public void AWallStopsTheBodyItsWidthShort_StronglyInferredFromBinary()
    {
        PossessionView view = At(0x5DF0, 0x5800, yaw: 0x200);

        view.Step(100, Forward, 0, 0, false, Body(), new World((6, 5)));

        Assert.Equal((0x6000 - Width, 0x5800), (view.X, view.Y));
    }

    /// <summary>Both neighbours free and only the diagonal refused: the axis with the larger travel is
    /// put back, X on a tie, and the other axis keeps its step.</summary>
    [Fact]
    public void ABlockedDiagonalSlidesAlongTheOtherAxis_StronglyInferredFromBinary()
    {
        PossessionView view = At(0x5F00, 0x5010, yaw: 0x100);

        view.Step(100, Forward, 0, 0, false, Body(), new World((6, 4)));

        Assert.Equal((0x6000 - Width, 0x5010 - 29), (view.X, view.Y));
    }

    [Fact]
    public void ABlockedPointDropsTheMove_StronglyInferredFromBinary()
    {
        PossessionView view = At(0x5800, 0x5800);

        view.Step(100, Forward, 0, 0, false, Body(), new World { BlockEverything = true });

        Assert.Equal((0x5800, 0x5800), (view.X, view.Y));
    }

    /// <summary>Scale 4, two cycle steps in: the 32-step sine is at its quarter, so a bob of 1.0 lifts 512; backing halves it.</summary>
    [Theory]
    [InlineData(true, 512)]
    [InlineData(false, 256)]
    public void TheBobRidesTheWalkCycle_StronglyInferredFromBinary(bool forward, int bob)
    {
        PossessionView view = At(0x5800, 0x5800);

        view.Step(100, new PossessionKeys(forward, !forward, false, false, false, false), 0, 0, false, Body(bob: 0x1000), new World());

        Assert.Equal(Floor + Eye + bob, view.Z);
    }

    [Fact]
    public void AFootfallSoundsOnceItsHalfCycleIsDue_StronglyInferredFromBinary()
    {
        PossessionView view = At(0x5800, 0x5800);

        PossessionFrame first = view.Step(100, Forward, 0, 0, false, Body(), new World());
        PossessionFrame second = view.Step(1, Forward, 0, 0, false, Body(), new World());

        Assert.Null(first.LeftFoot);
        Assert.NotNull(first.RightFoot);
        Assert.Null(second.RightFoot);
    }

    [Theory]
    [InlineData(10, 0, false, 20, PossessionView.Horizon)]
    [InlineData(0, 10, false, 0, PossessionView.Horizon - 20)]      // mouse down looks down
    [InlineData(0, 10, true, 0, PossessionView.Horizon + 20)]
    [InlineData(0, -200, false, 0, PossessionView.MaxPitch)]
    [InlineData(0, 200, false, 0, PossessionView.MinPitch)]
    public void TheMouseTurnsAndPitchesWithinFortyFiveDegrees_StronglyInferredFromBinary(int mouseX, int mouseY, bool invert, int yaw, int pitch)
    {
        PossessionView view = At(0x5800, 0x5800);

        view.Step(16, default, mouseX, mouseY, invert, Body(), new World());

        Assert.Equal((yaw, pitch), (view.Yaw, view.Pitch));
    }

    /// <summary>The lean is limited to 0x40 on the positive side only, then halved every frame.</summary>
    [Fact]
    public void TheLeanIsLimitedOnOneSideAndDecays_StronglyInferredFromBinary()
    {
        PossessionView right = At(0x5800, 0x5800), left = At(0x5800, 0x5800);
        PossessedBody body = Body(leans: true);

        right.Step(16, default, 100, 0, false, body, new World());
        Assert.Equal(0x20, right.Roll);
        right.Step(16, default, 0, 0, false, body, new World());
        Assert.Equal(0x10, right.Roll);

        left.Step(16, default, -100, 0, false, body, new World());
        Assert.Equal(0x800 - 50, left.Roll);
    }

    [Fact]
    public void ACreatureThatDoesNotLeanKeepsItsRoll_StronglyInferredFromBinary()
    {
        PossessionView view = At(0x5800, 0x5800);

        view.Step(16, default, 100, 0, false, Body(), new World());

        Assert.Equal(0, view.Roll);
    }

    [Fact]
    public void ThePoseCarriesTheEyeHeightNotTheCamerasOwn_StronglyInferredFromBinary()
    {
        PossessionView view = At(0x5800, 0x5800, yaw: 0x300);
        PossessedBody body = Body(bob: 0x1000);
        view.Step(100, Forward, 0, 0, false, body, new World());

        Assert.Equal((view.X, view.Y, Eye, 0x300), view.Pose(body));
    }

    /// <summary>A flier below eye height gains the shortfall on top of its own vertical step; level flight keeps its height.</summary>
    [Fact]
    public void AFlierIsRaisedToEyeHeight_StronglyInferredFromBinary()
    {
        var view = new PossessionView();
        view.Enter(0x5800, 0x5800, Floor - 0x100, Body(flying: true));

        view.Step(100, Forward, 0, 0, false, Body(flying: true), new World());

        Assert.Equal(Floor + Eye, view.Z);
    }
}
