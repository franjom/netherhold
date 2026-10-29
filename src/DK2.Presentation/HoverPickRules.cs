// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>Which things the Hand's screen picker may return (Hand_ResolveScreenTarget 0x005992B0),
/// by its two stages: the rendered hit (<see cref="RenderedPick"/>) and the 3×3 fallback
/// (0x0044DEC0). docs/research/hand-cursor-rendering.md.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/hand-cursor-rendering.md")]
public static class HoverPickRules
{
    /// <summary>Creature type 0x2E, which the fallback never returns (0x0044DEC0).</summary>
    public const byte FallbackRefusedCreatureType = 0x2E;

    /// <summary>Object type 13, which the fallback accepts without CanBePickedUp (0x0044DEC0).</summary>
    public const byte FallbackObjectType = 13;

    /// <summary>Whether a mesh draw carries the thing's tag into the rendered-hit list
    /// (Thing_DrawRenderInfo 0x0059B880): a creature whose definition has CanBePickedUp
    /// (<c>+0x686</c> bit 1) and is not dying (<c>+0x3E6</c> bit 21); an object whose definition has
    /// CanBePickedUp, CanBeSlapped or Highlightable (<c>+0xF4</c> &amp; 0x400300); a door or trap
    /// always. Dead bodies (kind 6) never. The creature arm also needs the sink's <c>+0xD1</c>
    /// clear, which binding26 (default PAGE UP) sets while held (0x0059C130).</summary>
    public static bool RenderedCreature(CreatureFlags definition, bool dying, bool objectsOnly = false) =>
        !objectsOnly && (definition & CreatureFlags.CanBePickedUp) != 0 && !dying;

    /// <inheritdoc cref="RenderedCreature"/>
    public static bool RenderedObject(ObjectFlags definition) =>
        (definition & (ObjectFlags.CanBePickedUp | ObjectFlags.CanBeSlapped | ObjectFlags.Highlightable)) != 0;

    /// <summary>The fallback's creature test (0x0044DEC0): not type 0x2E, not dying, and not in
    /// state 0xD6, 0x4C or 0xD1.</summary>
    public static bool FallbackCreature(byte creatureTypeId, bool dying, int stateId, bool objectsOnly = false) =>
        !objectsOnly && creatureTypeId != FallbackRefusedCreatureType && !dying && stateId is not (0xD6 or 0x4C or 0xD1);

    public static int FallbackObjectRadius(bool objectsOnly) => Fp32.One / (objectsOnly ? 2 : 4);

    /// <summary>The fallback's object test (0x0044DEC0): definition CanBePickedUp
    /// (<c>+0xF4</c> &amp; 0x100) or object type 13.</summary>
    public static bool FallbackObject(ObjectFlags definition, byte objectTypeId) =>
        objectTypeId == FallbackObjectType || (definition & ObjectFlags.CanBePickedUp) != 0;

    /// <summary><c>+0x3E6</c> bit 21 (CreatureDying) as our state answers it: the falling or dying
    /// states, or a collapse (status-flowers.md §3, inferred).</summary>
    public static bool IsDying(CreatureState creature) =>
        creature.Dk2StateId is CreatureDeathRules.FallingState or CreatureDeathRules.DyingState
        || CreatureDeathRules.HasCollapsed(creature);

    /// <summary>Whether <paramref name="hit"/> may come from the rendered stage.</summary>
    public static bool Rendered(WorldState world, GameDatabase database, WorldHoverKind kind, int id, bool objectsOnly = false) => kind switch
    {
        WorldHoverKind.Creature => world.CreatureById(id) is { } creature
            && RenderedCreature(database.CreatureById(creature.CreatureTypeId).Flags, IsDying(creature), objectsOnly),
        WorldHoverKind.Object => world.ObjectById(id) is { } thing
            ? RenderedObject(ObjectFlagsOf(database, thing.ObjectTypeId))
            : WorldView.IsStorePile(id) && RenderedObject(ObjectFlagsOf(database, ObjectHoverHighlight.StorePileObjectId)),
        WorldHoverKind.Door or WorldHoverKind.Trap => true,
        _ => false,
    };

    /// <summary>Whether the resolver (0x005992B0) turns a door or trap that won the rendered hit into
    /// an entity hit: hit kind 5 (door) or 7 (trap) only when the viewer owns it (thing <c>+0x28</c>
    /// against the viewer's number). Another keeper's door or trap leaves the hit on the tile, and the
    /// fallback sweep is not tried.</summary>
    public static bool ResolvesStructure(byte ownerPlayerId, PlayerId viewer) => ownerPlayerId == viewer.Value;

    /// <summary>The resolver's object arm (0x005992B0, kind 4): an object becomes the hover entity when
    /// the viewer owns it (thing <c>+0x28</c>), or when the neutral player owns it (the view's second
    /// player word, set from <c>[0x006F2548]</c>) and the tile under the object belongs to the viewer or
    /// to the neutral player (tile <c>+0x8 &amp; 0xFFF</c>). Anything else, a neutral gold pile on another
    /// keeper's floor among them, leaves the hit on the tile with no fallback.</summary>
    public static bool ResolvesObject(byte ownerPlayerId, byte tileOwnerPlayerId, PlayerId viewer) =>
        ownerPlayerId == viewer.Value
        || ownerPlayerId == PlayerIds.Neutral && (tileOwnerPlayerId == viewer.Value || tileOwnerPlayerId == PlayerIds.Neutral);

    /// <summary>Whether a rendered-hit winner becomes the hover entity (0x005992B0): doors and traps by
    /// <see cref="ResolvesStructure"/>; objects by <see cref="ResolvesObject"/> on the tile they stand on
    /// (a store pile, which has no object record, is taken to be owned by its room tile's owner: our assumption, unread); creatures always
    /// (kind 2 own, 3 other). A dead body would need kind 6, which no rendered hit can carry
    /// (<see cref="Rendered"/>).</summary>
    public static bool Resolves(WorldState world, WorldHoverHit hit, PlayerId viewer)
    {
        switch (hit.Kind)
        {
            case WorldHoverKind.Door: return Resolves(hit.Kind, world.DoorById(hit.EntityId)?.PlayerId, viewer);
            case WorldHoverKind.Trap: return Resolves(hit.Kind, world.TrapById(hit.EntityId)?.PlayerId, viewer);
            case WorldHoverKind.Object:
                if (world.ObjectById(hit.EntityId) is { } thing)
                    return (uint)thing.TileIndex < (uint)world.Tiles.Length
                        && ResolvesObject(thing.OwnerPlayerId, world.TileAtIndex(thing.TileIndex).OwnerPlayerId, viewer);
                if (!WorldView.IsStorePile(hit.EntityId) || !world.Contains(hit.Tile)) return false;
                byte roomOwner = world.Tile(hit.Tile).OwnerPlayerId;
                return ResolvesObject(roomOwner, roomOwner, viewer);
            default: return true;
        }
    }

    /// <summary>The door and trap test on the hit's owner: a door or trap (or one not found) needs the
    /// viewer's own; every other kind passes here (objects are <see cref="ResolvesObject"/>).</summary>
    public static bool Resolves(WorldHoverKind kind, byte? owner, PlayerId viewer) =>
        kind is not (WorldHoverKind.Door or WorldHoverKind.Trap) || owner is byte o && ResolvesStructure(o, viewer);

    /// <summary>The rendered stage's screen-circle test (0x00588A60 over the list the mesh draws fill,
    /// 0x00581107 static and 0x00584AC1 animated): the pointer must lie inside the projection of the
    /// model's bounding sphere, centre the KMF Origin through the draw's matrix and position, radius
    /// the KMF BoundsScale times the draw's scale; 0x00575F10 projects the radius as focal / depth.
    /// The same test in view space: <paramref name="offsetSquared"/> is the squared distance, in the
    /// plane at the centre's view depth, between the centre and the pointer ray. A sphere reaching
    /// behind the eye (depth - radius negative) gets an unbounded circle, so it always holds.</summary>
    public static bool CircleHolds(float centreDepth, float radius, float offsetSquared) =>
        centreDepth - radius < 0f || offsetSquared < radius * radius;

    /// <summary>Whether <paramref name="id"/> may come from the fallback sweep.</summary>
    public static bool Fallback(WorldState world, GameDatabase database, WorldHoverKind kind, int id, bool objectsOnly = false) => kind switch
    {
        WorldHoverKind.Creature => world.CreatureById(id) is { } creature
            && FallbackCreature(creature.CreatureTypeId, IsDying(creature), creature.Dk2StateId, objectsOnly),
        WorldHoverKind.Object => world.ObjectById(id) is { } thing
            ? FallbackObject(ObjectFlagsOf(database, thing.ObjectTypeId), thing.ObjectTypeId)
            : WorldView.IsStorePile(id)
              && FallbackObject(ObjectFlagsOf(database, ObjectHoverHighlight.StorePileObjectId), ObjectHoverHighlight.StorePileObjectId),
        _ => false,
    };

    private static ObjectFlags ObjectFlagsOf(GameDatabase database, byte objectTypeId) =>
        database.ObjectById(objectTypeId)?.Flags ?? 0;
}

/// <summary>The rendered-hit stage (Render_FindPointerThing 0x00572820): every tagged mesh whose
/// screen circle holds the pointer is offered. The first pass keeps the nearest triangle under the
/// pointer (Render_PickAnimatedMesh 0x00585AD0, the static twin 0x00582D30: depth the mean of the
/// triangle's three vertex depths); only when no triangle is hit does the second keep the nearest
/// mesh centre.</summary>
public struct RenderedPick
{
    private float _triangleDepth, _centreDepth;
    private int _triangleItem, _centreItem;
    private bool _anyTriangle, _anyCentre;

    /// <summary>Offers one item: <paramref name="triangleDepth"/> is its nearest hit triangle's depth,
    /// or null when the pointer is on none of its triangles.</summary>
    public void Offer(int item, float? triangleDepth, float centreDepth)
    {
        if (triangleDepth is float depth && (!_anyTriangle || depth < _triangleDepth))
        {
            _anyTriangle = true; _triangleDepth = depth; _triangleItem = item;
        }
        if (!_anyCentre || centreDepth < _centreDepth)
        {
            _anyCentre = true; _centreDepth = centreDepth; _centreItem = item;
        }
    }

    /// <summary>The picked item, and whether the first (triangle) pass found it; false with no offer.</summary>
    public readonly bool TryResult(out int item, out bool byTriangle)
    {
        byTriangle = _anyTriangle;
        item = _anyTriangle ? _triangleItem : _centreItem;
        return _anyTriangle || _anyCentre;
    }
}
