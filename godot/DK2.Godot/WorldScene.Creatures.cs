// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art;
using DK2.Assets;
using DK2.Core;
using DK2.Presentation;
using DK2.Simulation;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace DK2.GodotAdapter;

/// <summary>Creatures (plan step 3g), as Unity's <c>DungeonScene</c> draws them: each install model shared with the objects
/// (<see cref="ModelMesh"/>), a <see cref="ModelSlot"/> per creature, its type-6 clip played by the shared
/// <see cref="CreatureAnimationPlayer{TResources}"/> with each segment's positions rewritten in place, and each frame drawn
/// in <see cref="SceneFrame"/>'s order, which this scene hosts; the hover overlay's frame and the burning red are
/// WorldScene.Hover.cs's, the possessed model's hiding WorldScene.Possession.cs's and the crackle WorldScene.Lightning.cs's.
/// Footsteps wait for the sound player.</summary>
public partial class WorldScene : ISceneFrameHost
{
    private readonly SceneFrame _frame;
    private readonly Dictionary<int, CreatureSlot> _creatures = new();
    // The creature the frame's steps are drawing (PlaceCreature to the next).
    private CreatureSlot? _drawn;
    // What creating and updating creatures allocated, for the exit report: the pushes that made a node, those that started
    // a clip (the first DrawWarmUp of them build the clip sets and mesh copies), and those that only moved or turned one.
    private long _createPushBytes, _clipPushWarmUpBytes, _clipPushBytes, _plainPushBytes;
    private int _createPushes, _clipPushes, _plainPushes, _creatureClipStarts, _countedClipStarts;

    public WorldScene() => _frame = new SceneFrame(this);

    /// <summary>A drawn creature's place as the last frame drew it, in the renderers' frame (Unity's
    /// <c>TryGetCreaturePosition</c>, its model's position); false for a creature not drawn.</summary>
    public bool TryGetCreaturePosition(CreatureQueryTarget target, out NVector3 position)
    {
        position = default;
        if (!_creatures.TryGetValue(target.Value, out CreatureSlot? creature)) return false;
        position = creature.Slot.RenderPosition;
        return true;
    }

    public void CreateCreature(int creatureId, in CreatureView creature, in ResolvedAsset mesh, in ResolvedAsset animation)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        AddCreature(creatureId, creature, mesh, animation);
        CountPush(before, created: true);
    }

    public void UpdateCreature(int creatureId, in CreatureView creature, in ResolvedAsset mesh, in ResolvedAsset animation)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool created = !_creatures.TryGetValue(creatureId, out CreatureSlot? slot);
        if (created) AddCreature(creatureId, creature, mesh, animation);
        else if (slot!.Art != mesh.Reference) ChangeLook(creatureId, slot, creature, mesh, animation);
        else
        {
            PlaceCreature(slot.Slot, creature);
            _frame.Set(new CreatureId(creatureId), creature);
            ConfigureCreatureClip(slot, creature, mesh, animation);
        }
        CountPush(before, created);
    }

    /// <summary>A new look for the same creature (an idle creature's mesh is its idle clip, <c>WorldView</c>): Unity's
    /// <c>UpdateCreature</c> removes it keeping its head, so a tied effect is not let go, and creates it again. Here the
    /// node and its clip copies are kept, and the rest starts afresh: the frame's entry, the clip and the player's
    /// clock, so a deferred clip starts at once as on a new model.</summary>
    private void ChangeLook(int creatureId, CreatureSlot slot, in CreatureView creature, in ResolvedAsset mesh,
        in ResolvedAsset animation)
    {
        _frame.Remove(new CreatureId(creatureId), keepHead: true);
        if (Model(mesh, lastFrame: false) is not ModelMesh model)
        {
            RemoveCreature(creatureId, keepHead: true);
            return;
        }
        slot.Restart(mesh.Reference);
        slot.Slot.ShowStatic(model);
        _frame.Add(new CreatureId(creatureId), creature);
        ConfigureCreatureClip(slot, creature, mesh, animation);
        PlaceCreature(slot.Slot, creature);
        slot.Slot.ApplyLooks();
    }

    public void RemoveCreature(int creatureId) => RemoveCreature(creatureId, keepHead: false);

    private void RemoveCreature(int creatureId, bool keepHead)
    {
        if (_creatures.Remove(creatureId, out CreatureSlot? slot)) slot.Slot.Free();
        _frame.Remove(new CreatureId(creatureId), keepHead);
    }

    /// <summary>Unity's <c>CreateCreature</c>: with the install, a creature it has no model for draws nothing, as an object
    /// does (Godot has no placeholder boxes, audit open point 6).</summary>
    private void AddCreature(int creatureId, in CreatureView creature, in ResolvedAsset mesh, in ResolvedAsset animation)
    {
        if (Model(mesh, lastFrame: false) is not ModelMesh model) return;
        var slot = new CreatureSlot(new ModelSlot(this, model, "Creature" + creatureId, SlotKind.Creature), mesh.Reference);
        _creatures[creatureId] = slot;
        _frame.Add(new CreatureId(creatureId), creature);
        ConfigureCreatureClip(slot, creature, mesh, animation);
        PlaceCreature(slot.Slot, creature);
        slot.Slot.ApplyLooks();
    }

    /// <summary>Unity's <c>PositionCreature</c>: a model stands at the creature's point and height, modelled in tiles about
    /// its feet, turned by <see cref="MeshOrientation.Mesh"/>; each frame then moves it between turns, keeping the turns.</summary>
    private static void PlaceCreature(ModelSlot slot, in CreatureView creature) =>
        slot.Place(new NVector3(creature.PosX / (float)Fp32.One, creature.Height / (float)Fp32.One, -creature.PosY / (float)Fp32.One),
            MeshOrientation.Mesh(creature.MeshYaw, creature.Pitch, creature.Roll), Vector3.One);

    /// <summary>Unity's <c>ConfigureCreatureClip</c>: a clip starts once per <see cref="AnimationClipRequest.Identity(in
    /// CreatureView, AssetRef)"/>; a creature playing none, or whose clip the install lacks, shows its model.</summary>
    private void ConfigureCreatureClip(CreatureSlot creatureSlot, in CreatureView creature, in ResolvedAsset mesh,
        in ResolvedAsset animation)
    {
        CreatureClipIdentity identity = AnimationClipRequest.Identity(creature, animation.Reference);
        if (creatureSlot.HasClip && creatureSlot.Clip == identity) return;
        creatureSlot.HasClip = true;
        creatureSlot.Clip = identity;
        ModelSlot slot = creatureSlot.Slot;
        double start = AnimationClipRequest.StartSeconds(creature.AnimationStartedAt, _frame.DrawSeconds);
        if (creature.PlaysAnimation && ClipSetFor(animation) is ClipSet set)
        {
            _creatureClipStarts++;
            slot.Animation ??= new AnimatedModel(slot);
            slot.Animation.Player.Configure(set.Clips, set.Templates, set.Timing, creature.AnimationLoops, start,
                CreatureAnimationArt.AuthoredMovementScale(set.Resource, creature.AnimationMovementSpeed),
                creature.AnimationMovementSpeed, AnimationClipRequest.Replacement(creature), creature.AnimationPaused,
                creature.AnimationInitialMovementSpeed, footstepMarks: creature.FootstepMarks);
            return;
        }
        slot.StopAnimation();
        if (Model(mesh, lastFrame: false) is ModelMesh model) slot.ShowStatic(model);
    }

    private void CountPush(long before, bool created = false)
    {
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (created)
        {
            _countedClipStarts = _creatureClipStarts;
            _createPushBytes += bytes;
            _createPushes++;
            return;
        }
        if (_creatureClipStarts == _countedClipStarts)
        {
            _plainPushBytes += bytes;
            _plainPushes++;
            return;
        }
        _countedClipStarts = _creatureClipStarts;
        if (_clipPushes++ < DrawWarmUp) _clipPushWarmUpBytes += bytes;
        else _clipPushBytes += bytes;
    }


    bool ISceneFrameHost.PlaceCreature(CreatureId creature, in CreatureView pose, DrawPoint drawn)
    {
        if (!_creatures.TryGetValue(creature.Value, out _drawn)) return false;
        _drawn.Slot.MoveTo(new NVector3(drawn.X, drawn.Height, -drawn.Y));
        return true;
    }

    void ISceneFrameHost.DrawCreatureClip(CreatureId creature, in CreatureView pose, double seconds) =>
        _drawn!.Slot.Animation?.Player.Draw(seconds, pose.AnimationMovementSpeed, pose.AnimationPaused);

    // Every drawn frame, as a new look shows its node again (ModelSlot.ApplyLooks); WorldScene.Possession.cs shows it again.
    void ISceneFrameHost.HideCreatureModel(CreatureId creature)
    {
        if (_drawn!.Slot.Node.Visible) _drawn.Slot.Node.Visible = false;
    }

    int ISceneFrameHost.FootstepCrossing(CreatureId creature) => _drawn!.Slot.Animation?.Player.FootstepCrossing ?? 0;

    // A footstep is a sound only (CreatureFootsteps): it waits for the sound player (step 4).
    void ISceneFrameHost.RequestFootstep(CreatureId creature, in CreatureView pose, int crossing) { }

    void ISceneFrameHost.DrawCrackle(CreatureId creature, GameTick tick) => DrawCreatureLightning(creature, tick);

    bool ISceneFrameHost.TryGetControlPoint(CreatureId creature, uint point, out NVector3 render)
    {
        render = default;
        ModelSlot slot = _drawn!.Slot;
        if (slot.Animation?.Player is not { } player || !player.TryGetControlPoint(point, out NVector3 local)) return false;
        render = slot.ToRender(local);
        return true;
    }

    EffectTime ISceneFrameHost.EffectClock() => new(_frameMilliseconds);

    private void FreeCreatures()
    {
        int playing = 0;
        foreach (CreatureSlot slot in _creatures.Values) if (slot.Slot.Animation?.Player.Clock != null) playing++;
        GD.Print("DK2.Godot creatures: ", _creatures.Count, " drawn (", playing, " playing a clip); ", _createPushes,
            " pushes that made one allocated ", _createPushBytes, " B, those that started a clip ", _clipPushWarmUpBytes, " B in the first ", Math.Min(_clipPushes, DrawWarmUp), " and ", _clipPushBytes,
            " B in ", Math.Max(0, _clipPushes - DrawWarmUp), " after; ", _plainPushes, " that moved or turned one allocated ",
            _plainPushBytes, " B");
        foreach (CreatureSlot slot in _creatures.Values) slot.Slot.Free();
        _creatures.Clear();
        _frame.Clear();
    }

    /// <summary>A drawn creature: its model, the art it was made from and the clip it last started.</summary>
    private sealed class CreatureSlot(ModelSlot slot, AssetRef art)
    {
        public ModelSlot Slot { get; } = slot;
        public AssetRef Art { get; private set; } = art;
        public bool HasClip { get; set; }
        public CreatureClipIdentity Clip { get; set; }

        /// <summary>Made from <paramref name="art"/> again: no clip started and the player stopped.</summary>
        public void Restart(AssetRef art)
        {
            Art = art;
            HasClip = false;
            Slot.Animation?.Player.Stop();
        }
    }
}
