// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art.Kmf;
using DK2.Core;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>An engine's side of <see cref="SceneFrame"/>: what each step of a drawn frame does to its own models. A
/// creature step is called only between a <see cref="PlaceCreature"/> that returned true and the next creature's.</summary>
public interface ISceneFrameHost
{
    /// <summary>Before anything is drawn: the hover overlay's frame starts (the burning red draws into it).</summary>
    void BeginFrame();

    /// <summary>The Hearts' queued health steps (<see cref="HeartPulse.TryTakeHealthAdvance"/>).</summary>
    void AdvanceHeartHealth();

    /// <summary>Step 1: the creature's model at <paramref name="drawn"/>, its turns kept from its last tick; false when it
    /// has no model, which skips its other steps.</summary>
    bool PlaceCreature(CreatureId creature, in CreatureView pose, DrawPoint drawn);

    /// <summary>Step 2: the creature's clip drawn at <paramref name="seconds"/> of the draw clock.</summary>
    void DrawCreatureClip(CreatureId creature, in CreatureView pose, double seconds);

    /// <summary>Step 3: the possessed creature's model left out of the picture, every frame.</summary>
    void HideCreatureModel(CreatureId creature);

    /// <summary>What the clip's last draw found of its footstep marks: 0 none, 1 before the wrap, 2 after it; 0 without a clip.</summary>
    int FootstepCrossing(CreatureId creature);

    /// <summary>Step 4: the creature's footstep (the helper at <c>0x0043CD84</c>). DK2's request carries the creature's
    /// position <c>+0x1A</c>; the drawn place is the remake's choice.</summary>
    void RequestFootstep(CreatureId creature, in CreatureView pose, int crossing);

    /// <summary>Step 5: the burning creature's red overlay.</summary>
    void DrawBurningRed(CreatureId creature, in CreatureView pose);

    /// <summary>Step 6: the creature's lightning crackle.</summary>
    void DrawCrackle(CreatureId creature, GameTick tick);

    /// <summary>The drawn clip's tagged point in the renderers' frame (<see cref="RenderWorld"/>); false without a clip
    /// or without that point.</summary>
    bool TryGetControlPoint(CreatureId creature, uint point, out Vector3 render);

    /// <summary>Whether anything takes a gem sparkle; false skips the sparkle gate.</summary>
    bool TakesGemSparkles { get; }

    /// <summary>The wall clock the sparkle gate paces by.</summary>
    EffectTime EffectClock();

    /// <summary>Step 8: a gem carrier's sparkle at its point 10001, in native units.</summary>
    void RequestGemSparkle(FpPosition3 point);

    /// <summary>After the creatures: each object's drawn pose, clip and Heart beat.</summary>
    void DrawObjects(GameTick tick, float fraction);

    /// <summary>Last: the torches near a changed tile bent again.</summary>
    void RefreshTorches();
}

/// <summary>The drawn creatures and the order a frame draws them in:
/// the hover overlay's frame, the Hearts' health, then per creature its drawn place (<see cref="PoseInterpolator"/>),
/// its clip, the possessed model hidden, its footstep, the burning red, the crackle, its head point
/// (<see cref="Heads"/>) and a gem carrier's sparkle (<see cref="GemSparkleGate"/>), then the objects and the torches.
/// Render only; a drawn frame allocates nothing once each creature's sparkle pace is known.</summary>
public sealed class SceneFrame
{
    private readonly ISceneFrameHost _host;
    private readonly Dictionary<CreatureId, CreatureView> _poses = new();
    private readonly GemSparkleGate _gemSparkles = new();

    public SceneFrame(ISceneFrameHost host) => _host = host ?? throw new ArgumentNullException(nameof(host));

    /// <summary>The draw clock in game seconds as last set (<see cref="SetFrameTime"/>, <see cref="Draw"/>).</summary>
    public double DrawSeconds { get; private set; }

    /// <summary>Each drawn creature's head, written by every <see cref="Draw"/>.</summary>
    public CreatureHeadTable Heads { get; } = new();

    /// <summary>The creature whose model is hidden, <see cref="CreatureId.None"/> for none.</summary>
    public CreatureId HiddenCreature { get; set; }

    /// <summary>The drawn creatures' last views, in the order a frame draws them.</summary>
    public SceneCreatures Creatures => new(_poses);

    /// <summary>The draw clock for the clips the coming present starts.</summary>
    public void SetFrameTime(GameTick tick, float fraction) => DrawSeconds = PoseInterpolator.DrawSeconds(tick, fraction);

    /// <summary>A creature the scene has a model for; one already tracked keeps its head.</summary>
    public void Add(CreatureId creature, in CreatureView pose)
    {
        Heads.Add(creature);
        _poses[creature] = pose;
    }

    /// <summary>A drawn creature's new view.</summary>
    public void Set(CreatureId creature, in CreatureView pose) => _poses[creature] = pose;

    /// <summary>A creature no longer drawn; <paramref name="keepHead"/> keeps its head for a new look of the same creature,
    /// so a tied effect is not let go.</summary>
    public void Remove(CreatureId creature, bool keepHead)
    {
        if (!keepHead)
        {
            Heads.Remove(creature);
        }

        _gemSparkles.Forget(creature);
        _poses.Remove(creature);
    }

    /// <summary>Every creature and head dropped and the draw clock at 0; the hidden creature and sparkle pace stay.</summary>
    public void Clear()
    {
        Heads.Clear();
        _poses.Clear();
        DrawSeconds = 0d;
    }

    /// <summary>One drawn frame at <paramref name="fraction"/> (clamped) of the way from <paramref name="tick"/> to the next.</summary>
    public void Draw(GameTick tick, float fraction)
    {
        fraction = PoseInterpolator.Clamp01(fraction);
        _host.BeginFrame();
        SetFrameTime(tick, fraction);
        _host.AdvanceHeartHealth();
        foreach (KeyValuePair<CreatureId, CreatureView> entry in _poses)
        {
            CreatureId creature = entry.Key;
            CreatureView pose = entry.Value;
            // Position only: a creature keeps its last tick's turns (PoseInterpolator).
            if (!_host.PlaceCreature(creature, pose, PoseInterpolator.Position(pose, fraction)))
            {
                continue;
            }

            _host.DrawCreatureClip(creature, pose, DrawSeconds);
            // Every frame: a new look or a restored model brings its own renderers.
            if (creature == HiddenCreature)
            {
                _host.HideCreatureModel(creature);
            }
            // 0x0043CC30's footstep arm tests the clip's marks through 0x0043CD84 and requests the footstep; asking at each
            // drawn frame over the frame's fractions is HYPOTHESIS (sound-effects-continuous.md, the request).
            int crossing = _host.FootstepCrossing(creature);
            if (crossing != 0)
            {
                _host.RequestFootstep(creature, pose, crossing);
            }

            if (pose.BurningRed)
            {
                _host.DrawBurningRed(creature, pose);
            }

            _host.DrawCrackle(creature, tick);
            Heads.Set(creature, _host.TryGetControlPoint(creature, KmfControlPoint.Head, out Vector3 head)
                ? RenderWorld.ToNative(head) : null);
            // A gem carrier's sparkle at its point 10001, paced by GemSparkleGate.
            if (pose.GemCarrier && _host.TakesGemSparkles
                && _host.TryGetControlPoint(creature, CreatureView.GemDustSparkle.ControlPoint, out Vector3 sparkle)
                && _gemSparkles.TryPass(creature, _host.EffectClock()))
            {
                _host.RequestGemSparkle(RenderWorld.ToNative(sparkle));
            }
        }
        _host.DrawObjects(tick, fraction);
        _host.RefreshTorches();
    }
}

/// <summary>The drawn creatures by id, enumerable without allocating.</summary>
public readonly struct SceneCreatures
{
    private readonly Dictionary<CreatureId, CreatureView> _poses;

    internal SceneCreatures(Dictionary<CreatureId, CreatureView> poses) => _poses = poses;

    public int Count => _poses.Count;

    public CreatureView this[CreatureId creature] => _poses[creature];

    public bool TryGetValue(CreatureId creature, out CreatureView pose) => _poses.TryGetValue(creature, out pose);

    public Dictionary<CreatureId, CreatureView>.Enumerator GetEnumerator() => _poses.GetEnumerator();
}
