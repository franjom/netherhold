// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;

namespace DK2.Presentation;

/// <summary>The win screen's reward tablet (interface <c>+0x4C6E</c>, campaign-win-sequence.md §5):
/// the mesh <c>Tablet</c>, its front showing the reward's GUI icon, grows to 0.75 while it turns once
/// about two axes, settles, and holds 2 s; while it grows two sparkles leave each of its control points.
/// Lengths are DK2 screen pixels. The win screen's tablet stands still, so its positions are relative to
/// its centre; the Battle tab's fly-in (<c>+0x4D05</c>, <see cref="BattleFlyIn"/>) is the same object
/// with a start and target point, a start and end scale, a 2 s start gate and no hold.</summary>
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
    /// is also its drawn size (HYPOTHESIS).</summary>
    public struct Sparkle
    {
        public float X, Y, VelocityX, VelocityY;
        public int Life;
    }

    private Sparkle[] _sparkles = new Sparkle[256];
    private int _sparkleCount;
    private double _holdEnds;
    private bool _holdArmed;
    private double _startsAt;
    private readonly float _endScale;
    private readonly double _holdMilliseconds;
    private float _targetX, _targetY;

    /// <summary>The win screen's tablet: from scale 0 to <see cref="FinalScale"/> in place, a 2 s hold.</summary>
    public RewardTablet() : this(FinalScale, HoldMilliseconds) { }

    /// <param name="endScale">The target scale <c>+0x1C</c>.</param>
    /// <param name="holdMilliseconds">The hold <c>+0x57</c> in milliseconds after the sparkles are gone.</param>
    public RewardTablet(float endScale, double holdMilliseconds)
    {
        _endScale = endScale;
        _holdMilliseconds = holdMilliseconds;
    }

    /// <summary><c>+0x00</c>/<c>+0x04</c>: the tablet's centre, in the caller's pixel frame (the win
    /// screen's is its own centre, so 0).</summary>
    public float X { get; private set; }
    public float Y { get; private set; }

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
        _holdArmed = false;
        _startsAt = 0;
        X = Y = _targetX = _targetY = 0;
    }

    /// <summary>A start as <c>0x0042C0D0</c> makes the fly-in's: at (<paramref name="x"/>,
    /// <paramref name="y"/>) and <paramref name="scale"/>, gliding to the target after the real clock
    /// reaches <paramref name="startsAt"/> (<c>+0x53</c>; the fly-in's is interface <c>+0x4D58</c>).</summary>
    public void Start(float x, float y, float scale, float targetX, float targetY, double startsAt)
    {
        Restart();
        X = x; Y = y; Scale = scale;
        _targetX = targetX; _targetY = targetY;
        _startsAt = startsAt;
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

    /// <summary>One drawn frame: <c>0x0042F040</c>'s sparkles, then <c>0x0042EE80</c>'s step. DK2 draws
    /// between the two (<c>0x0042F430</c>, then the step at <c>0x0042F43B</c>); a caller that draws
    /// in that order calls <see cref="Spawn"/>, draws, then <see cref="Step"/>.</summary>
    /// <param name="points">The mesh's control points; each gives two sparkles while the tablet grows.
    /// DK2 takes every point at graphics detail 5 and above and every second one below; the remake
    /// has no detail setting and always takes every point.</param>
    /// <param name="random">MSVC <c>rand</c>'s 0..32767 in DK2 (<c>0x006359F0</c>); presentation only.</param>
    public void DrawFrame(double now, ReadOnlySpan<Vector3> points, Random random)
    {
        Spawn(now, points, random);
        Step(now);
    }

    /// <summary><c>0x0042F2B1</c>..<c>0x0042F394</c>: past the start gate and below frame 32, two
    /// sparkles from each control point at the tablet's current place.</summary>
    public void Spawn(double now, ReadOnlySpan<Vector3> points, Random random)
    {
        if (Frame < GrowFrames && points.Length > 0 && now >= _startsAt)
        {
            Matrix4x4 turn = Turn();
            for (int i = points.Length - 1; i >= 0; i--)
            {
                Vector3 at = Project(points[i], turn);
                for (int k = 0; k < 2; k++)
                    Add(new Sparkle
                    {
                        X = X + at.X, Y = Y + at.Y,
                        VelocityX = (random.Next(32768) - 16384) / 8192f,
                        VelocityY = (random.Next(32768) - 16384) / 8192f,
                        Life = random.Next(32768) / 8192 + 8,
                    });
            }
        }
    }

    private void Add(in Sparkle sparkle)
    {
        if (_sparkleCount == _sparkles.Length) Array.Resize(ref _sparkles, _sparkles.Length * 2);
        _sparkles[_sparkleCount++] = sparkle;
    }

    /// <summary><c>0x0042EE80</c>. Each sparkle loses a frame and leaves at 0, else moves; then, once
    /// the real clock has reached the start gate, the turn, position and scale take the frame's step:
    /// position and scale each close 1/(32 − frame) of their gap to the target (on the win screen the
    /// position lerp is idle, start and target both being the centre), frame 32 shrinks the scale by
    /// 5 %, frame 33 sets the target scale. Past frame 34 with no sparkles the hold is armed, and when
    /// it passes the item is done. The win screen's gate is the clock itself (<c>0x0042BE90</c>), so it
    /// never holds; the fly-in's is now + 2 s (<c>0x0042EEE8</c>..<c>0x0042EEF1</c>).</summary>
    public void Step(double now)
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
        if (now < _startsAt) return;

        int frame = Frame;
        Angle = 0;
        if (frame < GrowFrames)
        {
            Angle = frame * (AngleUnitsPerTurn / GrowFrames);
            X += (_targetX - X) / (GrowFrames - frame);
            Y += (_targetY - Y) / (GrowFrames - frame);
            Scale += (_endScale - Scale) / (GrowFrames - frame);
        }
        if (frame == GrowFrames) Scale *= 0.95f;
        if (frame == GrowFrames + 1) Scale = _endScale;
        Frame = frame + 1;
        if (Frame <= GrowFrames + 2 || _sparkleCount != 0) return;
        if (!_holdArmed) { _holdArmed = true; _holdEnds = now + _holdMilliseconds; }
        else if (now >= _holdEnds) Done = true;
    }
}
