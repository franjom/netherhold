// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Media;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>The Battle tab's fly-in (docs/research/battle-tab.md §5): when one of the viewer's
/// creatures joins a battle record that held none of the viewer's, <c>0x0042C0D0</c> plays
/// <c>0x32B</c>, sets the latch <c>+0x4D75</c> and starts the second reward tablet (<c>+0x4D05</c>)
/// showing the Fight reward's icon (<c>gui\icons\A-fight-00</c>) at scale 0.15 on reference point
/// (1840, 240); after 2 s on the real clock it glides to the blades' centre in 32 frames, turning
/// once and shrinking to nothing, with the tablet's sparkles. Done, <c>0x0042C390</c> plays
/// <c>0x32C</c> and clears the latch; while the latch is set no other fly-in starts.</summary>
/// <remarks>Step and draw wait while the GUI is hidden, the camera letterboxed or the game paused;
/// the start does not. Not mirrored (ADR-0774): the detail gate and the camera-mode skip. A frame
/// is <see cref="BeginFrame"/>, the caller's draw, <see cref="EndFrame"/>, as <c>0x0042F040</c> orders it.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/battle-tab.md#5-the-fly-in-notice--strongly-inferred")]
public sealed class BattleFlyIn
{
    /// <summary>The start point <c>0x0042C0D0</c> converts with <c>0x0052C5A0</c>: (1840, 240) of
    /// 2560 × 1920.</summary>
    public const float StartReferenceX = 1840, StartReferenceY = 240;
    /// <summary><c>+0x4D1D</c>: the start scale; the end scale <c>+0x4D21</c> is 0.</summary>
    public const float StartScale = 0.15f;
    /// <summary><c>+0x4D58</c>: now + 2000 ms on the real clock.</summary>
    public const double WaitMilliseconds = 2000;
    /// <summary>The reward item whose icon the tablet shows: 2441 = (31 &lt;&lt; 12) / 52, Fight.</summary>
    public const byte FightItem = 31;
    /// <summary>The start and end sounds, in GUI sound slot 15's category.</summary>
    public const uint StartSound = 0x32B, EndSound = 0x32C;

    private static readonly SoundCategory Category = new("gui_button_tab_new_combat");
    private readonly RewardTablet _tablet = new(endScale: 0, holdMilliseconds: 0);

    /// <summary>The latch <c>+0x4D75</c>: a fly-in is running.</summary>
    public bool Running { get; private set; }
    public RewardTablet Tablet => _tablet;
    public SoundRequests Sounds { get; } = new();

    /// <summary><c>0x0042C0D0</c>: refused while one runs; else the start sound, the latch and the
    /// tablet at <paramref name="startX"/>, <paramref name="startY"/> bound for the blades' centre.</summary>
    public bool Start(double now, float startX, float startY, float targetX, float targetY)
    {
        if (Running) return false;
        Sounds.Add(new SoundRequest(Category, new SoundId(StartSound)));
        Running = true;
        _tablet.Start(startX, startY, StartScale, targetX, targetY, now + WaitMilliseconds);
        return true;
    }

    /// <summary>The start of one drawn frame of <c>0x0042C390</c>'s tail: nothing while the GUI is
    /// hidden, the camera letterboxed or the game paused; else the sparkles' spawn. Returns whether
    /// the tablet is to be drawn now and <see cref="EndFrame"/> called after.</summary>
    public bool BeginFrame(double now, bool guiShown, bool letterboxed, bool paused, ReadOnlySpan<Vector3> points, Random random)
    {
        if (!Running || !guiShown || letterboxed || paused) return false;
        _tablet.Spawn(now, points, random);
        return true;
    }

    /// <summary>The step after the draw; when the tablet reports done, the end sound and the latch
    /// cleared (<c>0x0042C66E</c>).</summary>
    public void EndFrame(double now)
    {
        _tablet.Step(now);
        if (!_tablet.Done) return;
        Sounds.Add(new SoundRequest(Category, new SoundId(EndSound)));
        Running = false;
    }
}
