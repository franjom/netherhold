// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;

namespace DK2.Presentation;

/// <summary>The win screen's reward tablet (interface <c>+0x4C6E</c>, campaign-win-sequence.md §5):
/// the mesh <c>Tablet</c>, its front showing the reward's GUI icon, grows to 0.75 while it turns once
/// about two axes, settles, and holds 2 s; while it grows two sparkles leave each of its control points.
/// Lengths are DK2 screen pixels, kept here relative to the tablet's centre.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/campaign-win-sequence.md")]
public sealed class RewardTablet
{
    /// <summary>The growth frames (<c>0x0042EE80</c>: frame counter <c>+0x2C</c> below 0x20).</summary>
    public const int GrowFrames = 32;
    /// <summary>The target scale <c>+0x1C</c> (<c>0x0042BE90</c>).</summary>
    public const float FinalScale = 0.75f;
    /// <summary>Screen pixels per mesh unit at scale 1: <c>0x0042F040</c> passes scale × 4096.</summary>
    public const float PixelsPerUnit = 4096;
    /// <summary>The hold after the sparkles are gone: <c>+0x57</c> = 2 seconds.</summary>
    public const double HoldMilliseconds = 2000;
    /// <summary>DK2 angle units per turn: the draw multiplies by π × −1/1024.</summary>
    public const int AngleUnitsPerTurn = 2048;
    /// <summary>The centre DK2 passes through <c>0x0052C5A0</c>: (1280, 800) of 2560 × 1920.</summary>
    public const float CentreX = 0.5f, CentreY = 800f / 1920f;

    /// <summary>One sparkle (<c>0x0042F309</c>, 0x18 bytes): position, velocity and frames left, which
    /// is also its drawn size (LIKELY).</summary>
    public struct Sparkle
    {
        public float X, Y, VelocityX, VelocityY;
        public int Life;
    }

    private Sparkle[] _sparkles = new Sparkle[256];
    private int _sparkleCount;
    private double _holdEnds;

    public int Frame { get; private set; }
    /// <summary><c>+0x18</c>: 0 at the start of each item.</summary>
    public float Scale { get; private set; }
    /// <summary><c>+0x20</c> and <c>+0x28</c>, the turns about axes 0 and 2; <c>+0x24</c> stays 0.</summary>
    public int Angle { get; private set; }
    /// <summary><c>+0x4B</c>: the item's tablet has finished.</summary>
    public bool Done { get; private set; }
    public ReadOnlySpan<Sparkle> Sparkles => _sparkles.AsSpan(0, _sparkleCount);

    /// <summary>The texture a material of <paramref name="textureCount"/> frames shows for an item: the step
    /// stores <c>(id &lt;&lt; 12) / 52</c> (<c>+0x4F</c>), the draw passes it × 2⁻¹² and <c>0x00579730</c> rounds
    /// count × that + 1/1024 − 0.49999 to the nearest, clamped to the frames. Tablet.kmf's front has 51
    /// frames (Alarm … reaper_talisman-01d), so item n shows frame n − 1.</summary>
    public static int TextureFrame(int textureCount, byte itemId)
    {
        float fraction = (uint)(itemId << 12) / 52u * (1f / 4096f);
        int frame = (int)Math.Round(textureCount * fraction + 1f / 1024f - 0.49999f, MidpointRounding.ToEven);
        return Math.Clamp(frame, 0, Math.Max(0, textureCount - 1));
    }

    /// <summary><c>0x0042BE90</c>: a new item starts at scale 0, frame 0, not done, with no hold armed.
    /// The sparkle list is not cleared; an item only ends once it is empty.</summary>
    public void Restart()
    {
        Frame = 0;
        Scale = 0;
        Done = false;
        _holdEnds = 0;
    }

    /// <summary>The draw's turn (<c>0x0042F040</c>): <see cref="Angle"/> about axes 2 and 0. The order and
    /// sense of the two <c>Mat3_SetAxisRotation</c> products are unread.</summary>
    public Matrix4x4 Turn()
    {
        float radians = Angle * MathF.PI / -1024f;
        return Matrix4x4.CreateRotationZ(radians) * Matrix4x4.CreateRotationX(radians);
    }

    /// <summary>A mesh point relative to the centre in screen pixels (y down): turned, then scaled by
    /// <see cref="Scale"/> × 4096 (<c>0x00577DC0</c>; z is depth).</summary>
    public Vector3 Project(Vector3 point, in Matrix4x4 turn) => Vector3.Transform(point, turn) * (Scale * PixelsPerUnit);

    /// <summary>One drawn frame: <c>0x0042F040</c>'s sparkles, then <c>0x0042EE80</c>'s step.</summary>
    /// <param name="points">The mesh's control points; each gives two sparkles while the tablet grows.
    /// DK2 takes every point at graphics detail 5 and above and every second one below; the remake
    /// has no detail setting and always takes every point.</param>
    /// <param name="random">MSVC <c>rand</c>'s 0..32767 in DK2 (<c>0x006359F0</c>); presentation only.</param>
    public void DrawFrame(double now, ReadOnlySpan<Vector3> points, Random random)
    {
        if (Frame < GrowFrames && points.Length > 0)
        {
            Matrix4x4 turn = Turn();
            for (int i = points.Length - 1; i >= 0; i--)
            {
                Vector3 at = Project(points[i], turn);
                for (int k = 0; k < 2; k++)
                    Add(new Sparkle
                    {
                        X = at.X, Y = at.Y,
                        VelocityX = (random.Next(32768) - 16384) / 8192f,
                        VelocityY = (random.Next(32768) - 16384) / 8192f,
                        Life = random.Next(32768) / 8192 + 8,
                    });
            }
        }
        Step(now);
    }

    private void Add(in Sparkle sparkle)
    {
        if (_sparkleCount == _sparkles.Length) Array.Resize(ref _sparkles, _sparkles.Length * 2);
        _sparkles[_sparkleCount++] = sparkle;
    }

    /// <summary><c>0x0042EE80</c>. Each sparkle loses a frame and leaves at 0, else moves; then the turn
    /// and scale take the frame's step: scale closes 1/(32 − frame) of the gap to 0.75 (the position
    /// lerp is idle, start and target both being the centre), frame 32 shrinks it by 5 %, frame 33 sets
    /// 0.75. Past frame 34 with no sparkles a 2 s hold is armed, and when it passes the item is done.
    /// DK2 gates all but the sparkles on the start time <c>+0x53</c>, which <c>0x0042BE90</c> sets to the
    /// clock, so the gate never holds and is left out.</summary>
    private void Step(double now)
    {
        int kept = 0;
        for (int i = 0; i < _sparkleCount; i++)
        {
            Sparkle s = _sparkles[i];
            if (--s.Life == 0) continue;
            s.X += s.VelocityX;
            s.Y += s.VelocityY;
            _sparkles[kept++] = s;
        }
        _sparkleCount = kept;

        int frame = Frame;
        Angle = 0;
        if (frame < GrowFrames)
        {
            Angle = frame * (AngleUnitsPerTurn / GrowFrames);
            Scale += (FinalScale - Scale) / (GrowFrames - frame);
        }
        if (frame == GrowFrames) Scale *= 0.95f;
        if (frame == GrowFrames + 1) Scale = FinalScale;
        Frame = frame + 1;
        if (Frame <= GrowFrames + 2 || _sparkleCount != 0) return;
        if (_holdEnds == 0) _holdEnds = now + HoldMilliseconds;
        else if (now >= _holdEnds) Done = true;
    }
}
