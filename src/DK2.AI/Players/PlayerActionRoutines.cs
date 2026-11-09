// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Players;

namespace DK2.AI.Players;

/// <summary>
/// Pure routines over one <see cref="PlayerObservation"/>: each reads only what the player sees
/// and returns ordinary actions. Orders are deterministic (distance from <paramref name="home"/>,
/// then Y, then X), so identical observations give identical choices. Callers compute
/// <see cref="Home"/> once per decision and pass it down.
/// </summary>
public static class PlayerActionRoutines
{
    /// <summary>A visible creature of another side: heroes or a rival keeper. The observation carries
    /// no alliances, so an allied rival keeper also counts.</summary>
    public static bool IsEnemy(in PlayerVisibleThing thing, PlayerId self) =>
        thing.Kind == PlayerThingKind.Creature && thing.Owner != self && thing.Owner != PlayerIds.Neutral && thing.Owner != PlayerIds.Unowned;

    /// <summary>Own claimed ground a creature can stand on: known, not solid or liquid, the player's.</summary>
    public static bool IsOwnFloor(in PlayerTile tile, PlayerId self) =>
        tile.IsKnown && (tile.Flags & (PlayerTileFlags.Solid | PlayerTileFlags.Water | PlayerTileFlags.Lava)) == 0 && tile.Owner == self;

    /// <summary>Where distances are measured from: the own Dungeon Heart's centre, else the own
    /// floor's, else the map's.</summary>
    public static TilePos Home(PlayerObservation observation)
    {
        PlayerTileGrid grid = observation.Tiles;
        PlayerId self = observation.Stamp.Player;
        long hx = 0, hy = 0, hn = 0, fx = 0, fy = 0, fn = 0;
        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                PlayerTile tile = grid[new TilePos(x, y)];
                if (tile.Owner != self || !tile.IsKnown) continue;
                if (tile.Room == RoomTypeId.DungeonHeart) { hx += x; hy += y; hn++; }
                else if (IsOwnFloor(tile, self)) { fx += x; fy += y; fn++; }
            }
        if (hn > 0) return new TilePos((int)(hx / hn), (int)(hy / hn));
        if (fn > 0) return new TilePos((int)(fx / fn), (int)(fy / fn));
        return new TilePos(grid.Width / 2, grid.Height / 2);
    }

    /// <summary>Tiles worth a dig mark: not already marked, not in <paramref name="skip"/>, beside own
    /// floor, and either known diggable rock or not yet seen (a mark on unseen ground is a bet the game
    /// allows a human too; whether it sticks follows the human path). Gold seams first when
    /// <paramref name="preferGold"/>, then nearest <paramref name="home"/>, then Y, then X.</summary>
    public static List<TilePos> Frontier(PlayerObservation observation, TilePos home, bool preferGold, ICollection<TilePos> skip)
    {
        PlayerTileGrid grid = observation.Tiles;
        PlayerId self = observation.Stamp.Player;
        var candidates = new List<(int Gold, long Distance, TilePos Tile)>();
        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                var at = new TilePos(x, y);
                PlayerTile tile = grid[at];
                if ((tile.Flags & PlayerTileFlags.Tagged) != 0 || skip.Contains(at)) continue;
                bool diggable = !tile.IsKnown || (tile.Flags & (PlayerTileFlags.Solid | PlayerTileFlags.Diggable)) == (PlayerTileFlags.Solid | PlayerTileFlags.Diggable);
                if (!diggable || !BesideOwnFloor(grid, at, self)) continue;
                int gold = preferGold && (tile.Flags & PlayerTileFlags.GoldSeam) != 0 ? 0 : 1;
                candidates.Add((gold, DistanceSquared(at, home), at));
            }
        candidates.Sort((a, b) => a.Gold != b.Gold ? a.Gold.CompareTo(b.Gold)
            : a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance)
            : a.Tile.Y != b.Tile.Y ? a.Tile.Y.CompareTo(b.Tile.Y) : a.Tile.X.CompareTo(b.Tile.X));
        var tiles = new List<TilePos>(candidates.Count);
        foreach ((_, _, TilePos tile) in candidates) tiles.Add(tile);
        return tiles;
    }

    /// <summary>The build for <paramref name="room"/> on the best free square of own floor not in
    /// <paramref name="tried"/>, when the panel offers it and the player can pay for the whole square.</summary>
    public static PlayerAction.BuildRoom? BuildRoom(PlayerObservation observation, TilePos home, RoomTypeId room, int side, ICollection<TilePos> tried)
    {
        if ((observation.Offers.Actions & PlayerActionKinds.BuildRoom) == 0) return null;
        PlayerRoomOffer? offer = null;
        foreach (PlayerRoomOffer candidate in observation.Offers.Rooms)
            if (candidate.Room == room) offer = candidate;
        if (offer is not PlayerRoomOffer priced || (long)priced.Cost.Value * side * side > observation.Gold.Value) return null;
        foreach (TilePos corner in RoomSquares(observation, home, side))
            if (!tried.Contains(corner))
                return new PlayerAction.BuildRoom(room, corner, new TilePos(corner.X + side - 1, corner.Y + side - 1));
        return null;
    }

    /// <summary>Top-left corners of free squares of own floor, nearest home first, then Y, then X.</summary>
    public static List<TilePos> RoomSquares(PlayerObservation observation, TilePos home, int side)
    {
        PlayerTileGrid grid = observation.Tiles;
        var corners = new List<(long Distance, TilePos Corner)>();
        for (int y = 0; y + side <= grid.Height; y++)
            for (int x = 0; x + side <= grid.Width; x++)
            {
                var corner = new TilePos(x, y);
                if (SquareFree(observation, corner, side))
                    corners.Add((DistanceSquared(new TilePos(x + side / 2, y + side / 2), home), corner));
            }
        corners.Sort((a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance)
            : a.Corner.Y != b.Corner.Y ? a.Corner.Y.CompareTo(b.Corner.Y) : a.Corner.X.CompareTo(b.Corner.X));
        var result = new List<TilePos>(corners.Count);
        foreach ((_, TilePos corner) in corners) result.Add(corner);
        return result;
    }

    /// <summary>The panel button that selects <paramref name="room"/>, when offered.</summary>
    public static PlayerAction? SelectRoom(PlayerObservation observation, RoomTypeId room)
    {
        GuiButtonTarget button = GuiButtonTarget.Room(room);
        foreach (GuiButtonTarget panel in observation.Offers.Panels)
            if (panel == button) return new PlayerAction.SelectPanel(button);
        return null;
    }

    /// <summary>The visible enemy nearest <paramref name="home"/>, or null.</summary>
    public static PlayerVisibleThing? NearestEnemy(PlayerObservation observation, TilePos home)
    {
        PlayerId self = observation.Stamp.Player;
        PlayerVisibleThing? enemy = null;
        foreach (PlayerVisibleThing thing in observation.Things)
            if (IsEnemy(thing, self) && (enemy is null || DistanceSquared(thing.Tile, home) < DistanceSquared(enemy.Value.Tile, home)))
                enemy = thing;
        return enemy;
    }

    /// <summary>An own non-worker the hand may lift that is not already within <paramref name="near"/>
    /// tiles of <paramref name="enemy"/> or of <paramref name="placed"/> (where a drop would put it);
    /// the nearest home first.</summary>
    public static PlayerVisibleThing? Fighter(PlayerObservation observation, CampaignPlayerOptions options, TilePos enemy, TilePos placed, int near, TilePos home)
    {
        PlayerId self = observation.Stamp.Player;
        PlayerVisibleThing? best = null;
        foreach (PlayerVisibleThing thing in observation.Things)
        {
            if (thing.Kind != PlayerThingKind.Creature || thing.Owner != self || (thing.Actions & PlayerThingActions.PickUp) == 0
                || thing.CreatureType is not CreatureTypeId type || options.IsWorker(type)
                || DistanceSquared(thing.Tile, enemy) <= (long)near * near
                || DistanceSquared(thing.Tile, placed) <= (long)near * near) continue;
            if (best is null || DistanceSquared(thing.Tile, home) < DistanceSquared(best.Value.Tile, home)) best = thing;
        }
        return best;
    }

    /// <summary>The first special the hand can activate whose type <paramref name="options"/> allows.</summary>
    public static PlayerAction? Special(PlayerObservation observation, CampaignPlayerOptions options)
    {
        foreach (PlayerVisibleThing thing in observation.Things)
            if (thing.Kind == PlayerThingKind.Special && (thing.Actions & PlayerThingActions.Activate) != 0
                && thing.ObjectType is ObjectTypeId type && options.AllowsSpecial(type))
                return new PlayerAction.ActivateSpecial(thing.Handle);
        return null;
    }

    /// <summary>The own non-room floor tile nearest <paramref name="to"/> not in <paramref name="skip"/>.</summary>
    public static TilePos? NearestOwnFloor(PlayerObservation observation, TilePos to, ICollection<TilePos> skip)
    {
        PlayerTileGrid grid = observation.Tiles;
        PlayerId self = observation.Stamp.Player;
        TilePos? best = null;
        long bestDistance = long.MaxValue;
        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                var at = new TilePos(x, y);
                PlayerTile tile = grid[at];
                if (!IsOwnFloor(tile, self) || tile.Room != default || skip.Contains(at)) continue;
                long d = DistanceSquared(at, to);
                if (d < bestDistance) { bestDistance = d; best = at; }
            }
        return best;
    }

    /// <summary>Whether every tile of the square at <paramref name="corner"/> shows <paramref name="room"/>.</summary>
    public static bool RoomCovers(PlayerObservation observation, TilePos corner, int side, RoomTypeId room)
    {
        PlayerTileGrid grid = observation.Tiles;
        for (int dy = 0; dy < side; dy++)
            for (int dx = 0; dx < side; dx++)
            {
                var at = new TilePos(corner.X + dx, corner.Y + dy);
                if (!grid.Contains(at) || grid[at].Room != room) return false;
            }
        return true;
    }

    public static long DistanceSquared(TilePos a, TilePos b)
    {
        long dx = a.X - b.X, dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static bool SquareFree(PlayerObservation observation, TilePos corner, int side)
    {
        PlayerTileGrid grid = observation.Tiles;
        PlayerId self = observation.Stamp.Player;
        for (int dy = 0; dy < side; dy++)
            for (int dx = 0; dx < side; dx++)
            {
                var at = new TilePos(corner.X + dx, corner.Y + dy);
                if (!grid.Contains(at)) return false;
                PlayerTile tile = grid[at];
                if (!IsOwnFloor(tile, self) || tile.Room != default || (tile.Flags & PlayerTileFlags.Tagged) != 0) return false;
            }
        return true;
    }

    private static bool BesideOwnFloor(PlayerTileGrid grid, TilePos at, PlayerId self)
    {
        foreach ((int dx, int dy) in Neighbours)
        {
            var next = new TilePos(at.X + dx, at.Y + dy);
            if (grid.Contains(next) && IsOwnFloor(grid[next], self)) return true;
        }
        return false;
    }

    /// <summary>North, east, south, west.</summary>
    private static readonly (int Dx, int Dy)[] Neighbours = [(0, -1), (1, 0), (0, 1), (-1, 0)];
}
