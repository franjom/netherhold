// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>The tool icon beside the hand (<c>gui\cursors\I-*</c>, table <c>0x0068F978</c>).</summary>
public enum PlacementIcon { None, PlaceRoom, Sell, PlaceDoor, PlaceTrap }

/// <summary>The tool as the hover-cage flag <c>0x004096B0</c> sees it (modes 0, 3, 4, 6, 7, and 8/10).</summary>
public enum HoverCageTool { Hand, Room, Sell, Door, Trap, SpellOrQuery }

/// <summary>A half-open tile rectangle, <c>(X0, Y0)</c> inclusive to <c>(X1, Y1)</c> exclusive.</summary>
public readonly record struct TileRectangle(int X0, int Y0, int X1, int Y1)
{
    public bool Contains(int x, int y) => x >= X0 && x < X1 && y >= Y0 && y < Y1;
}

/// <summary>One frame of placement feedback: the selection cage (null draws none), blue when
/// <see cref="Valid"/> and red otherwise, the tool icon with the No Go icon over it, and the
/// price beside them (negative a cost, positive a refund, zero none).</summary>
public readonly record struct PlacementFeedback(
    TileRectangle? Cage, bool Valid, int Price, PlacementIcon Icon, bool NoGo, bool Affordable)
{
    public static PlacementFeedback None => default;
}

/// <summary>DK2's per-frame placement cursor (docs/research/placement-cursor.md): the tool arms
/// of <c>0x0040BBE0</c> for room build (<c>0x0040B210</c>), sell (<c>0x0040B600</c>), door
/// (<c>0x0040B3C0</c>) and trap (<c>0x0040B4F0</c>), drawn by <c>0x0040CAE0</c>. Validity comes
/// from the simulation's own placement rules.</summary>
public static class PlacementCursor
{
    /// <summary>Hand_Click's room release (<c>0x00408194..0x004081FD</c>): an end further than
    /// <c>RubberBandAreaLimit</c> from the anchor on an axis is pulled back to anchor ± limit.</summary>
    public static TilePos ClampDrag(TilePos anchor, TilePos end, int limit)
    {
        static int Axis(int from, int to, int limit) =>
            Math.Abs(to - from) <= limit ? to : from + Math.Sign(to - from) * limit;
        return new TilePos(Axis(anchor.X, end.X, limit), Axis(anchor.Y, end.Y, limit));
    }

    /// <summary>The cursor's drag rectangle, half-open, as the room and sell arms clamp it
    /// (<c>0x0040B2B0..0x0040B2F3</c>, <c>0x0040B661..0x0040B6AA</c>) and the sell release does
    /// (<c>0x004083DA..0x0040845F</c>): from the low corner, each side at most the limit long. Dragged
    /// left or up past the limit it keeps the tiles at the pointer's end, where the room release
    /// (<see cref="ClampDrag"/>) keeps those at the anchor.</summary>
    public static TileRectangle DragRectangle(TilePos anchor, TilePos end, int limit)
    {
        int x0 = Math.Min(anchor.X, end.X), y0 = Math.Min(anchor.Y, end.Y);
        int x1 = Math.Max(anchor.X, end.X), y1 = Math.Max(anchor.Y, end.Y);
        if (x1 - x0 > limit) x1 = x0 + limit;
        if (y1 - y0 > limit) y1 = y0 + limit;
        return new TileRectangle(x0, y0, x1 + 1, y1 + 1);
    }

    /// <summary>Whether the one-tile hover cage shows, as the interface's flag <c>0x004096B0</c> decides
    /// it per tool (placement-cursor.md §2a): always in room, sell, door and trap mode; in Hand mode
    /// while the hand holds something or over a dig-taggable tile; with a keeper spell or Query only
    /// over a dig-taggable tile. Its Options, view-mode, over-GUI and camera gates are the caller's.</summary>
    public static bool HoverCageShows(HoverCageTool tool, bool digTaggable, bool handHolds) => tool switch
    {
        HoverCageTool.Room or HoverCageTool.Sell or HoverCageTool.Door or HoverCageTool.Trap => true,
        HoverCageTool.Hand => digTaggable || handHolds,
        _ => digTaggable,
    };

    /// <summary>The hover cage's colour (bridge <c>+0x36</c> = interface <c>+0x132E</c> bit 0x10). The
    /// reset in <c>0x0040BBE0</c> sets 0x10; over a dig-taggable tile the Hand and room arms return
    /// before touching it and the spell and Query arms never write it, so those cages are blue. Sell,
    /// door and trap rewrite it from their own validity.</summary>
    public static bool HoverCageBlue(HoverCageTool tool, bool digTaggable, bool armValid) =>
        digTaggable && tool is HoverCageTool.Hand or HoverCageTool.Room or HoverCageTool.SpellOrQuery || armValid;

    /// <summary>The hover cage on <paramref name="at"/>, or none on a map-border tile, where
    /// <c>Presentation_Update</c> (<c>0x0043B220</c>) clears the Hand light's cage bit.</summary>
    public static TileRectangle? HoverCage(TilePos at, int width, int height) =>
        at.X <= 0 || at.Y <= 0 || at.X >= width - 1 || at.Y >= height - 1
            ? null : new TileRectangle(at.X, at.Y, at.X + 1, at.Y + 1);

    /// <summary>Hand mode with something held (<c>0x0040AE80</c>): the 1×1 drop cage on the hovered tile,
    /// red when the top item may not be dropped there. DK2 colours it by its drop gate
    /// <c>0x0040D8F0</c> (placement-cursor.md, "The drop cage's colour"), which differs from ours:
    /// allied ground, no walls or doors, Combat Pit, Heart and Portal arms. Until that gate is ported
    /// to the simulation this asks the simulation's observed rules (<see cref="HandRules.DropLands"/>,
    /// <see cref="HandRules.CanDropObjectOn"/>), so the colour follows what a click does here.
    /// None with an empty hand.</summary>
    public static PlacementFeedback HandDrop(WorldState world, GameDatabase database, PlayerId player, TilePos hovered)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (database is null) throw new ArgumentNullException(nameof(database));
        List<HandItem> hand = world.Player(player).Hand;
        if (hand.Count == 0) return PlacementFeedback.None;
        HandItem top = hand[hand.Count - 1];
        bool valid = top.IsObject
            ? HandRules.CanDropObjectOn(world, hovered, player)
            : world.CreatureById(top.Id) is CreatureState creature && database.CreatureById(creature.CreatureTypeId) is CreatureDef definition
              && HandRules.DropLands(world, database, creature, definition, hovered, player);
        return new PlacementFeedback(new TileRectangle(hovered.X, hovered.Y, hovered.X + 1, hovered.Y + 1),
            valid, 0, PlacementIcon.None, false, true);
    }

    /// <summary>The rubber-band limit, its low byte as <c>0x006F2900</c> is read.</summary>
    public static int RubberBandLimit(GameDatabase database) =>
        database[VariableTypeId.RubberBandAreaLimit] & 0xFF;

    /// <summary>Room build (<c>0x0040B210</c>). Hovering, no cage and the hovered tile is priced;
    /// dragging, the clamped rectangle is caged and priced at the room's per-tile price times
    /// the tiles it may take (<c>0x004C5A20</c>; water and lava through the span rule,
    /// <c>0x004C57F0</c>). Valid needs at least one such tile (with none <c>0x004C5AF0</c>
    /// prices 0 and fails) and gold for the cost (<c>money >= cost</c>). Ours counts with
    /// <see cref="RoomRules.CanBuild(GameDatabase, WorldState, TilePos, RoomDef, PlayerId)"/>, a mirror of world
    /// slot <c>+0x22C</c> not walked against it. The hover arm's explored gate (tile <c>+9</c> bit
    /// 0x20) is mirrored; its terrain <c>+0x17B</c> bit 0 gate is not.</summary>
    public static PlacementFeedback Room(WorldState world, GameDatabase database, PlayerId player, RoomDef room,
        TilePos hovered, TilePos? anchor, int gold)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (database is null) throw new ArgumentNullException(nameof(database));
        if (room is null) throw new ArgumentNullException(nameof(room));
        TileRectangle area = anchor is TilePos from
            ? DragRectangle(from, hovered, RubberBandLimit(database))
            : new TileRectangle(hovered.X, hovered.Y, hovered.X + 1, hovered.Y + 1);
        var order = new List<TilePos>();
        for (int y = area.Y0; y < area.Y1; y++)
            for (int x = area.X0; x < area.X1; x++)
                if (world.Contains(new TilePos(x, y))) order.Add(new TilePos(x, y));
        int count = 0;
        HashSet<TilePos> spans = RoomRules.SpannableTiles(database, world, order, room, player);
        foreach (TilePos tile in order)
            if (spans.Contains(tile) || RoomRules.CanBuild(database, world, tile, room, player) == RoomPlacement.Allowed)
                count++;
        bool explored = anchor is not null || !world.FogOfWarEnabled || world.Tile(hovered).IsExploredBy(player);
        int cost = count * RoomRules.CostPerTile(room);
        bool affordable = gold >= cost;
        bool valid = explored && count > 0 && affordable;
        return new PlacementFeedback(anchor is null ? null : area, valid, -cost, PlacementIcon.PlaceRoom, !valid, affordable);
    }

    /// <summary>Sell (<c>0x0040B600</c>): the clamped rectangle while dragging, else the hovered
    /// tile; the refund of each room tile <paramref name="player"/> may sell, valid when above
    /// zero. Not mirrored: the hovered door or trap's refund (<c>0x004AABB0</c>/<c>0x004A77E0</c>).</summary>
    public static PlacementFeedback Sell(WorldState world, GameDatabase database, PlayerId player,
        TilePos hovered, TilePos? anchor)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (database is null) throw new ArgumentNullException(nameof(database));
        TileRectangle area = anchor is TilePos from
            ? DragRectangle(from, hovered, RubberBandLimit(database))
            : new TileRectangle(hovered.X, hovered.Y, hovered.X + 1, hovered.Y + 1);
        int refund = 0;
        for (int y = area.Y0; y < area.Y1; y++)
            for (int x = area.X0; x < area.X1; x++)
            {
                var tile = new TilePos(x, y);
                if (!world.Contains(tile)) continue;
                if (RoomRules.CanSell(database, world.Tile(tile), player, out RoomDef? room) == RoomSale.Allowed && room is not null)
                    refund += RoomRules.RefundPerTile(database, room);
            }
        bool valid = refund > 0;
        return new PlacementFeedback(anchor is null ? null : area, valid, refund, PlacementIcon.Sell, !valid, true);
    }

    /// <summary>Door (<c>0x0040B3C0</c>): no cage. Without a Workshop or a tile under the pointer it
    /// is invalid with no price (DK2 leaves a stale one); else the price is the door's
    /// <c>GoldCost</c> and valid needs the gold and the placement test.</summary>
    public static PlacementFeedback Door(WorldState world, GameDatabase database, PlayerId player, DoorDef door,
        TilePos? hovered, bool hasWorkshop, int gold)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (door is null) throw new ArgumentNullException(nameof(door));
        if (!hasWorkshop || hovered is not TilePos at) return new PlacementFeedback(null, false, 0, PlacementIcon.PlaceDoor, true, true);
        bool affordable = gold >= door.GoldCost;
        bool valid = affordable && BlueprintPlacementRules.CanDoor(world, database, at, player, door) == BlueprintPlacementRules.Verdict.Allowed;
        return new PlacementFeedback(null, valid, -door.GoldCost, PlacementIcon.PlaceDoor, !valid, affordable);
    }

    /// <summary>Trap (<c>0x0040B4F0</c>): as <see cref="Door"/> with the trap's <c>GoldCost</c>.</summary>
    public static PlacementFeedback Trap(WorldState world, GameDatabase database, PlayerId player, TrapDef trap,
        TilePos? hovered, bool hasWorkshop, int gold)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (trap is null) throw new ArgumentNullException(nameof(trap));
        if (!hasWorkshop || hovered is not TilePos at) return new PlacementFeedback(null, false, 0, PlacementIcon.PlaceTrap, true, true);
        bool affordable = gold >= trap.GoldCost;
        bool valid = affordable && BlueprintPlacementRules.CanTrap(world, database, at, player) == BlueprintPlacementRules.Verdict.Allowed;
        return new PlacementFeedback(null, valid, -trap.GoldCost, PlacementIcon.PlaceTrap, !valid, affordable);
    }
}
