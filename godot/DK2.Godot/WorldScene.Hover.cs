// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Presentation;
using DK2.Simulation;
using NVector3 = System.Numerics.Vector3;

namespace DK2.GodotAdapter;

/// <summary>Picks drawn creatures, objects, doors and traps for hover. Draws the tint and burning overlays selected by the shared presentation logic.</summary>
public partial class WorldScene
{
    private readonly HoverOverlayPlan _hoverOverlay = new HoverOverlayPlan();
    // The models whose hover looks were asked for or applied: tried each commit, dropped once they show none.
    private readonly List<ModelSlot> _hoverSlots = new List<ModelSlot>();

    /// <summary>The view the pick culls by (the classic pose's frustum); null before the camera is placed.</summary>
    public ViewFrustum? PickFrustum
    {
        get
        {
            return View == null ? null : PlacedFrustum;
        }
    }

    /// <summary>After <see cref="WorldHoverPicker.Begin"/>, offer every drawn creature, object
    /// that is a hover target, door and trap offered with its measures; the rendered result, else the 3x3 sweep over where
    /// the creatures and objects are drawn. Allocation-free.</summary>
    public WorldHoverHit PickHoverEntity(WorldHoverPicker picker, NVector3 origin, NVector3 direction)
    {
        foreach (KeyValuePair<int, CreatureSlot> entry in _creatures)
        {
            if (_frame.Creatures.TryGetValue(new CreatureId(entry.Key), out CreatureView pose))
            {
                Offer(picker, entry.Value.Slot, new WorldHoverHit(WorldHoverKind.Creature, pose.Tile, entry.Key), origin, direction);
            }
        }

        foreach (KeyValuePair<int, ModelSlot> entry in _objects)
        {
            if (WorldHoverPicker.OffersObject(entry.Key, entry.Value.Pose.ObjectTypeId))
            {
                Offer(picker, entry.Value, new WorldHoverHit(WorldHoverKind.Object, entry.Value.Pose.Tile, entry.Key), origin, direction);
            }
        }

        foreach (KeyValuePair<StructureId, ModelSlot> entry in _structures)
        {
            if (_structureViews.TryGetValue(entry.Key, out StructureView structure))
            {
                Offer(picker, entry.Value, WorldHoverPicker.StructureHit(entry.Key, structure.Tile), origin, direction);
            }
        }

        if (picker.TryRenderedResult(out WorldHoverHit rendered))
        {
            return rendered;
        }

        foreach (KeyValuePair<CreatureId, CreatureView> entry in _frame.Creatures)
        {
            if (picker.SweepsCreature(entry.Value.Tile) && _creatures.TryGetValue(entry.Key.Value, out CreatureSlot? creature))
            {
                picker.AddSweepCreature(entry.Key.Value, entry.Value.Tile, creature.Slot.RenderPosition);
            }
        }

        foreach (KeyValuePair<int, ModelSlot> entry in _objects)
        {
            if (picker.SweepsObject(entry.Key, entry.Value.Pose.ObjectTypeId, entry.Value.Pose.Tile))
            {
                picker.AddSweepObject(entry.Key, entry.Value.Pose.Tile, entry.Value.RenderPosition);
            }
        }

        return picker.SweepResult();
    }

    private static void Offer(WorldHoverPicker picker, ModelSlot slot, WorldHoverHit candidate, NVector3 origin, NVector3 direction)
    {
        if (slot.Sphere is PickSphere broad && !picker.Holds(broad.Centre, broad.Radius, out _))
        {
            return;
        }

        slot.Measure(out PickBox bounds, out PickSphere? sphere);
        if (!picker.TryAdmit(candidate, bounds, sphere, out HoverPickProbe probe))
        {
            return;
        }

        picker.Offer(candidate, probe, slot.NearestTriangle(origin, direction, picker.TerrainDistance));
    }

    /// <summary>The hover tint <see cref="HoverOverlayPlan"/> plans on the hit's model
    /// (the caller decides which picks are tinted, <see cref="HoverTint"/>), the gold twin and the red pass; shown from
    /// <see cref="CommitHoverOverlay"/> until the next frame asks again.</summary>
    public void DrawHoverOverlay(WorldHoverHit hit, bool coversBlended = false)
    {
        if (!HoverOverlayPlan.Overlays(hit.Kind) || !_hoverOverlay.TryBegin(hit, coversBlended, out HoverOverlayDraw plan))
        {
            return;
        }

        ModelSlot? slot = hit.Kind switch
        {
            WorldHoverKind.Creature => _creatures.TryGetValue(hit.EntityId, out CreatureSlot? creature) ? creature.Slot : null,
            WorldHoverKind.Object => _objects.TryGetValue(hit.EntityId, out ModelSlot? thing) ? thing : null,
            _ => _structures.TryGetValue(new StructureId(hit.Kind == WorldHoverKind.Door ? StructureKind.Door : StructureKind.Trap,
                hit.EntityId), out ModelSlot? structure) ? structure : null,
        };
        if (slot == null)
        {
            return;
        }

        bool swap = hit.Kind == WorldHoverKind.Object && HoverOverlayPlan.Swaps(plan, slot.Pose.HoverTexture);
        slot.Hover = slot.Hover.With(new HoverLooks(swap, HoverOverlayPlan.Submesh(plan, false, blended: false).Red,
            HoverOverlayPlan.Submesh(plan, false, blended: true).Red));
        if (!_hoverSlots.Contains(slot))
        {
            _hoverSlots.Add(slot);
        }

        _hoverOverlay.Drawn(plan);
    }

    /// <summary>Shows the frame's hover looks on each model whose looks changed, after every <see cref="DrawHoverOverlay"/>
    /// of the frame (the burning red in <see cref="DrawFrame"/>, then the Hand's).</summary>
    public void CommitHoverOverlay()
    {
        for (int i = _hoverSlots.Count - 1; i >= 0; i--)
        {
            ModelSlot slot = _hoverSlots[i];
            slot.CommitHover();
            if (slot.Freed || !slot.Hover.Any)
            {
                _hoverSlots.RemoveAt(i);
            }
        }
    }

    // SceneFrame's first step: no creature is red yet, and last frame's looks are asked for again or dropped.
    void ISceneFrameHost.BeginFrame()
    {
        _hoverOverlay.BeginFrame();
        foreach (ModelSlot slot in _hoverSlots)
        {
            slot.Hover = default;
        }
    }

    // Apply the hover overlay's red pass to burning creatures.
    void ISceneFrameHost.DrawBurningRed(CreatureId creature, in CreatureView pose)
    {
        DrawHoverOverlay(new WorldHoverHit(WorldHoverKind.Creature, pose.Tile, creature.Value));
    }
}
