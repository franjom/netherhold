// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>What the possessed view asks of the world each frame. Positions are 1/4096 tile.</summary>
public interface IPossessionWorld
{
    int Width { get; }
    int Height { get; }

    /// <summary><c>0x0040F6B0</c>: may the possessed creature, standing on its own tile, enter this tile.</summary>
    bool CanStepTo(int tileX, int tileY);

    /// <summary><c>0x004D1BC0</c>: the wanted point pushed clear of the things between it and the start.</summary>
    (int X, int Y) ResolveAgainstThings(int startX, int startY, int wantedX, int wantedY);

    /// <summary>World slots <c>+0x20C</c> and <c>+0x240</c>: the point is solid, or its tile holds a built, closed door.</summary>
    bool Blocked(int x, int y);

    /// <summary>World slot <c>+0x204</c>: the floor height under a point.</summary>
    int FloorHeight(int x, int y);

    /// <summary>Terrain <c>+0x17B</c> bit 7 under a point, which picks the second pair of footstep sounds.</summary>
    bool WetAt(int x, int y);
}

/// <summary>The possessed creature as the movement step reads it (possession-client.md §2, §5a).</summary>
/// <param name="WalkSpeed">The three results of <c>0x0048ABB0</c>, per turn, in 1/4096 tile.</param>
/// <param name="StepLocked"><c>+0x3E6</c> bit 13: skips the keys, the walk cycle and the waddle.</param>
/// <param name="KeysLocked">A requested claim bob (<c>+0x389</c>): skips the keys only.</param>
public readonly record struct PossessedBody(
    int WalkSpeed, int RunSpeed, int ShuffleSpeed, bool Creeping, bool Flying, bool StepLocked, bool KeysLocked,
    int BodyWidth, int EyeHeight, int Waddle, int Bob, int WalkCycleScale, bool LeansWhenTurning, int Heading);

/// <summary>The held movement bindings: DK2's 16 to 19, 5 and 4.</summary>
public readonly record struct PossessionKeys(bool Forward, bool Back, bool Left, bool Right, bool Run, bool Creep);

/// <summary>One footfall the step asks to be heard: the left or right foot, on wet or dry ground.</summary>
public readonly record struct PossessionFootstep(bool Right, bool Wet, int X, int Y, int Z);

/// <summary>What one frame of the step produced besides the camera's own change.</summary>
public readonly record struct PossessionFrame(bool? SetCreeping, PossessionFootstep? LeftFoot, PossessionFootstep? RightFoot);

/// <summary>DK2's first-person camera while a creature is possessed (camera mode 2). The camera is the
/// authority: <see cref="Step"/> moves it from the keys and the mouse as <c>0x0040E910</c> does, and
/// <see cref="Pose"/> is what the client posts once a turn as the creature's place (<c>0x004039A0</c>).
/// Angles are 2048 to a turn; a pitch of 0x200 is the horizon.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/possession-client.md")]
public sealed class PossessionView
{
    /// <summary>Pitch limits of <c>Camera_Update</c>'s mode-2 arm: 45 degrees either side of the horizon.</summary>
    public const int MinPitch = 0x100, MaxPitch = 0x300, Horizon = 0x200;

    private const int AngleMask = 0x7FF, Tile = 0x1000, TileMask = ~0xFFF;
    private const int LeanLimit = 0x40, WalkingFootSpread = 0x30000, RunningFootSpread = 0x2000;

    private long _clock;
    private long _cycleStamp;
    private uint _cycle, _lastCycle;
    private uint _leftFootDue = 0x600, _rightFootDue = 0x200;

    public int X { get; private set; }
    public int Y { get; private set; }
    public int Z { get; private set; }
    public int Yaw { get; private set; }
    public int Pitch { get; private set; } = Horizon;
    public int Roll { get; private set; }

    /// <summary>Puts the camera on a creature just possessed: on its point at eye height, level. The
    /// starting pitch is <c>Camera_SetMode</c>'s constant. That the view starts facing the creature's
    /// heading is the owner's recollection (2026-11-14); the timed move into mode 2 is unread.</summary>
    public void Enter(int x, int y, int floorHeight, in PossessedBody body)
    {
        X = x; Y = y; Z = floorHeight + body.EyeHeight;
        Yaw = body.Heading & AngleMask; Pitch = Horizon; Roll = 0;
        _clock = _cycleStamp = 0; _cycle = _lastCycle = 0;
        _leftFootDue = 0x600; _rightFootDue = 0x200;
    }

    /// <summary>The pose game action 25 carries: the camera's X and Y, the definition's eye height as
    /// the height (never the camera's own), and the camera's yaw as the heading.</summary>
    public (int X, int Y, int Height, int Heading) Pose(in PossessedBody body) => (X, Y, body.EyeHeight & 0xFFFF, Yaw);

    /// <summary>One frame of <c>0x0040E910</c>, then the pitch limit <c>Camera_Update</c> applies.
    /// <paramref name="mouseX"/> and <paramref name="mouseY"/> are the frame's mouse deltas.</summary>
    public PossessionFrame Step(int frameMilliseconds, PossessionKeys keys, int mouseX, int mouseY, bool invertMouse,
        in PossessedBody body, IPossessionWorld world, int turnsPerSecond = 4)
    {
        _clock += frameMilliseconds;
        bool? setCreeping = keys.Creep != body.Creeping ? keys.Creep : null;

        // Mode 2 wins over mode 1; the unit is the per-turn speed spread over the frame.
        bool run = keys.Run;
        int speed = body.Creeping ? body.ShuffleSpeed : run ? body.RunSpeed : body.WalkSpeed;
        uint raw = (uint)(((ulong)unchecked((uint)(speed * turnsPerSecond * frameMilliseconds)) * 0x10624DD3UL) >> 32);
        uint unit = raw >> 6;

        int strafe = 0, forward = 0;
        bool backing = false;
        if (!body.StepLocked)
        {
            bool moving = false;
            if (!body.KeysLocked)
            {
                if (keys.Left) { moving = true; strafe = -(int)(unit * 2 / 3); }
                if (keys.Right) { moving = true; strafe += (int)(unit * 2 / 3); }
                if (keys.Back) { moving = true; backing = true; forward = (int)(raw >> 7); }
                if (keys.Forward) { moving = true; forward -= (int)unit; }
            }
            if (!moving && !body.Flying)
            {
                _cycle = 0; _cycleStamp = _clock;
                _leftFootDue = 0x600; _rightFootDue = 0x200;
            }
            else
                _cycle = (uint)((_clock - _cycleStamp) / 50);
            if (_lastCycle != _cycle && !body.Flying && body.Waddle != 0)
                strafe += Over4096(Over(TrapRules.Sine((int)((uint)body.WalkCycleScale * _cycle & 0x3F) * 32) * body.Waddle, 9));
        }
        strafe = Math.Clamp(strafe, -Tile, Tile);
        forward = Math.Clamp(forward, -Tile, Tile);

        PossessionFootstep? left = null, right = null;
        if (strafe != 0 || forward != 0 || body.Flying)
            Move(strafe, forward, backing, run, body, world, ref left, ref right);

        // The look: yaw by twice the mouse, a lean for the definitions that roll, then pitch.
        Yaw = (Yaw + mouseX * 2) & AngleMask;
        if (body.LeansWhenTurning) Lean(mouseX);
        Pitch = (Pitch + (invertMouse ? mouseY : -mouseY) * 2) & AngleMask;
        Pitch = Math.Clamp(Pitch, MinPitch, MaxPitch);

        _lastCycle = _cycle;
        return new PossessionFrame(setCreeping, left, right);
    }

    private void Move(int strafe, int forward, bool backing, bool run, in PossessedBody body, IPossessionWorld world,
        ref PossessionFootstep? left, ref PossessionFootstep? right)
    {
        int sin = TrapRules.Sine(Yaw), cos = TrapRules.Cosine(Yaw);
        int dx = (strafe * cos >> 16) - (forward * sin >> 16);
        int dy = (forward * cos >> 16) + (strafe * sin >> 16);
        int dz = TrapRules.Cosine(Pitch) * forward >> 16;

        int widthX = body.BodyWidth * Math.Sign(dx), widthY = body.BodyWidth * Math.Sign(dy);
        int targetX = Math.Min(Math.Max(X + dx + widthX, 0), world.Width << 12);
        int targetY = Math.Min(Math.Max(Y + dy + widthY, 0), world.Height << 12);

        bool crossesX = ((X ^ targetX) & TileMask) != 0, crossesY = ((Y ^ targetY) & TileMask) != 0;
        bool blockX = crossesX && !world.CanStepTo(targetX >> 12, Y >> 12);
        bool blockY = crossesY && !world.CanStepTo(X >> 12, targetY >> 12);
        if (blockX || blockY || !world.CanStepTo(targetX >> 12, targetY >> 12))
        {
            // The slide: a refused axis goes back to its tile's edge; with only the diagonal
            // refused, the axis with the larger travel does, X on a tie (0x0040EFB3).
            bool snapX = blockX || (!blockY && Math.Abs(Y - targetY) <= Math.Abs(X - targetX));
            bool snapY = blockY || !snapX;
            if (snapX) targetX = (X & TileMask) + (dx > 0 ? Tile : 0);
            if (snapY) targetY = (Y & TileMask) + (dy > 0 ? Tile : 0);
        }

        (int x, int y) = world.ResolveAgainstThings(X, Y, targetX - widthX, targetY - widthY);
        if (world.Blocked(x, y)) return;

        int floor = world.FloorHeight(x, y) + body.EyeHeight;
        int z;
        if (!body.Flying)
        {
            z = floor + Bob(body, backing);
            Roll = Over4096(Over4096(TrapRules.Sine((int)((uint)body.WalkCycleScale * _cycle & 0x3F) * 32) * body.Waddle)) & AngleMask;
            Footsteps(x, y, z, run, body, world, ref left, ref right);
        }
        else
        {
            z = Z;
            if (z < floor) dz += floor - z;
            z += dz;
            if (body.Bob != 0) z += Bob(body, backing);
        }
        X = x; Y = y; Z = z;
    }

    private int Bob(in PossessedBody body, bool backing)
    {
        int scaled = Over(TrapRules.Sine((int)((uint)body.WalkCycleScale * _cycle & 0x1F) * 64) * body.Bob, 7);
        int bob = Over4096(scaled);
        return backing ? (bob - (scaled >> 31)) >> 1 : bob;
    }

    /// <summary>Each foot sounds once per half cycle, placed to its side of the creature's heading;
    /// the spread is 0x30000 walking and 0x2000 with Run held, as read.</summary>
    private void Footsteps(int x, int y, int z, bool run, in PossessedBody body, IPossessionWorld world,
        ref PossessionFootstep? left, ref PossessionFootstep? right)
    {
        uint phase = (_cycle + 0x14) * (uint)body.WalkCycleScale;
        uint beat = (phase & 0x1FFFFFF) * 0x20;
        int spread = run ? RunningFootSpread : WalkingFootSpread;
        bool wet = world.WetAt(x, y);
        if (_leftFootDue < beat && beat - _leftFootDue > 0x800)
        {
            left = Foot(false, body.Heading - 0x200);
            _leftFootDue = (beat & 0xFFFFF800) + 0x600;
        }
        if (_rightFootDue < beat && beat - _rightFootDue > 0x800)
        {
            right = Foot(true, body.Heading + 0x200);
            _rightFootDue = ((phase & 0x1FFFFFF) >> 6) * 0x800 + 0x200;
        }

        PossessionFootstep Foot(bool rightFoot, int side)
        {
            int offX = (int)((long)spread * TrapRules.Sine(side) >> 16), offY = -(int)((long)spread * TrapRules.Cosine(side) >> 16);
            return new PossessionFootstep(rightFoot, wet, x + offX / 16, y + offY / 16, z);
        }
    }

    /// <summary><c>0x0044DD40</c>: the lean into a turn. Only a positive roll is limited; the test on
    /// the negative side never fires in DK2, and is kept that way (ADR-0783). Every call then halves
    /// the roll toward zero, and a roll of one either side stays.</summary>
    private void Lean(int mouseX)
    {
        if (mouseX != 0)
        {
            Roll = (Roll + mouseX) & AngleMask;
            if (Roll is > LeanLimit and < 0x400) Roll = LeanLimit;
        }
        if (Roll == 0) return;
        if (Roll < 0x400)
            Roll -= Roll * 0x20 >> 6;
        else
        {
            int step = Over((0x800 - Roll) * 0x20, 6);
            Roll = Roll <= 0x800 - step ? (Roll + step) & AngleMask : 0;
        }
    }

    /// <summary>A signed divide by two to the <paramref name="shift"/> that truncates toward zero, as
    /// DK2's compiler wrote it.</summary>
    private static int Over(int value, int shift) => (value + ((value >> 31) & ((1 << shift) - 1))) >> shift;

    private static int Over4096(int value) => Over(value, 12);
}
