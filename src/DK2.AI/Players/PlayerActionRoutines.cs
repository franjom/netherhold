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

    /// <summary>An enemy (<see cref="IsEnemy"/>) that is not a captive: one standing on the player's own
    /// Prison or Torture Chamber is taken for its prisoner, since a visible creature keeps its owner. The
    /// observation has no captive flag, so a free hero crossing those tiles is not answered until it steps off.</summary>
    public static bool IsThreat(PlayerObservation observation, in PlayerVisibleThing thing)
    {
        PlayerId self = observation.Stamp.Player;
        if (!IsEnemy(thing, self)) return false;
        PlayerTileGrid grid = observation.Tiles;
        if (!grid.Contains(thing.Tile)) return true;
        PlayerTile on = grid[thing.Tile];
        return on.Owner != self || on.Room is not (RoomTypeId.Prison or RoomTypeId.TortureChamber);
    }

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

    /// <summary>Tiles worth a dig mark: not already marked, not in <paramref name="skip"/> or
    /// <paramref name="avoid"/>, beside own floor, and either known diggable rock or not yet seen (a mark on
    /// unseen ground is a bet the game allows a human too; whether it sticks follows the human path).
    /// Without <paramref name="breach"/>, a tile beside open ground another player owns is left standing,
    /// so a closed corridor stays closed. With <paramref name="tagged"/>, marked tiles are listed too. Gold seams first when <paramref name="preferGold"/>, then nearest
    /// <paramref name="home"/>, then Y, then X.</summary>
    public static List<TilePos> Frontier(PlayerObservation observation, TilePos home, bool preferGold, ICollection<TilePos> skip,
        bool breach = true, ICollection<TilePos>? avoid = null, bool tagged = false)
    {
        PlayerTileGrid grid = observation.Tiles;
        PlayerId self = observation.Stamp.Player;
        var candidates = new List<(int Gold, long Distance, TilePos Tile)>();
        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                var at = new TilePos(x, y);
                PlayerTile tile = grid[at];
                if (!tagged && (tile.Flags & PlayerTileFlags.Tagged) != 0 || skip.Contains(at) || avoid?.Contains(at) == true) continue;
                bool diggable = !tile.IsKnown || (tile.Flags & (PlayerTileFlags.Solid | PlayerTileFlags.Diggable)) == (PlayerTileFlags.Solid | PlayerTileFlags.Diggable);
                if (!diggable || !BesideOwnFloor(grid, at, self) || !breach && BesideOthersGround(grid, at, self)) continue;
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
    /// <paramref name="tried"/>, when the panel offers it and the player can pay for the whole square.
    /// With <paramref name="grow"/>, a square touching a tile of the same room comes first, so the
    /// room is extended rather than split.</summary>
    public static PlayerAction.BuildRoom? BuildRoom(PlayerObservation observation, TilePos home, RoomTypeId room, int side, ICollection<TilePos> tried,
        bool grow = false)
    {
        if ((observation.Offers.Actions & PlayerActionKinds.BuildRoom) == 0) return null;
        PlayerRoomOffer? offer = null;
        foreach (PlayerRoomOffer candidate in observation.Offers.Rooms)
            if (candidate.Room == room) offer = candidate;
        if (offer is not PlayerRoomOffer priced || (long)priced.Cost.Value * side * side > observation.Gold.Value) return null;
        List<TilePos> squares = RoomSquares(observation, home, side);
        if (grow)
            foreach (TilePos corner in squares)
                if (!tried.Contains(corner) && Touches(observation.Tiles, corner, side, room, observation.Stamp.Player))
                    return new PlayerAction.BuildRoom(room, corner, new TilePos(corner.X + side - 1, corner.Y + side - 1));
        foreach (TilePos corner in squares)
            if (!tried.Contains(corner))
                return new PlayerAction.BuildRoom(room, corner, new TilePos(corner.X + side - 1, corner.Y + side - 1));
        return null;
    }

    /// <summary>The player's own <paramref name="room"/> tile nearest <paramref name="home"/> whose eight neighbours are
    /// the same room too (an interior tile, where a Combat Pit drop starts a fight; combat-pit.md section 9), skipping
    /// <paramref name="skip"/>; Y, then X, breaks a tie. Null without one.</summary>
    public static TilePos? InteriorRoomTile(PlayerObservation observation, RoomTypeId room, TilePos home, ICollection<TilePos>? skip = null)
    {
        PlayerTileGrid grid = observation.Tiles;
        PlayerId self = observation.Stamp.Player;
        TilePos? best = null;
        for (int y = 1; y < grid.Height - 1; y++)
            for (int x = 1; x < grid.Width - 1; x++)
            {
                var at = new TilePos(x, y);
                if (skip?.Contains(at) == true || !IsInterior(grid, at, room, self)) continue;
                if (best is not TilePos b || DistanceSquared(at, home) < DistanceSquared(b, home)) best = at;
            }
        return best;
    }

    /// <summary>Whether <paramref name="at"/> and its eight neighbours are all the player's own
    /// <paramref name="room"/>: a room's interior, the Combat Pit's floor (the hand's floor-slot test).</summary>
    public static bool IsInterior(PlayerTileGrid grid, TilePos at, RoomTypeId room, PlayerId self)
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var near = new TilePos(at.X + dx, at.Y + dy);
                if (!grid.Contains(near) || grid[near].Room != room || grid[near].Owner != self) return false;
            }
        return true;
    }

    /// <summary>Where an Imp is dropped to carry a collapsed creature at <paramref name="body"/> to its bed: the own
    /// floor tile nearest the body, within <see cref="ImpDropScanTiles"/> (Manhattan) of it, that is not the Combat
    /// Pit's interior, where the hand refuses a worker drop; then Y, then X. Null without one.</summary>
    public static TilePos? RescueDropTile(PlayerObservation observation, TilePos body)
    {
        PlayerTileGrid grid = observation.Tiles;
        PlayerId self = observation.Stamp.Player;
        TilePos? best = null;
        for (int dy = -ImpDropScanTiles; dy <= ImpDropScanTiles; dy++)
            for (int dx = -ImpDropScanTiles; dx <= ImpDropScanTiles; dx++)
            {
                var at = new TilePos(body.X + dx, body.Y + dy);
                if (Math.Abs(dx) + Math.Abs(dy) > ImpDropScanTiles || !grid.Contains(at) || !IsOwnFloor(grid[at], self)
                    || IsInterior(grid, at, RoomTypeId.CombatPit, self)) continue;
                if (best is not TilePos b || DistanceSquared(at, body) < DistanceSquared(b, body)) best = at;
            }
        return best;
    }

    /// <summary>How far (Manhattan tiles) a dropped Imp looks for work at once (ImpTaskSystem.DropScanTiles).</summary>
    public const int ImpDropScanTiles = 3;

    /// <summary>The tile of the own <paramref name="room"/> nearest <paramref name="home"/>, then Y, then X, not in
    /// <paramref name="skip"/>; null when none shows.</summary>
    public static TilePos? NearestRoomTile(PlayerObservation observation, RoomTypeId room, TilePos home, ICollection<TilePos>? skip = null)
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
                if (tile.Room != room || tile.Owner != self || !tile.IsKnown || skip?.Contains(at) == true) continue;
                long d = DistanceSquared(at, home);
                if (d < bestDistance) { bestDistance = d; best = at; }
            }
        return best;
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

    /// <summary>Where to dig out a square of <paramref name="side"/> for a room (Task 3a): every tile own floor
    /// with no room, or known diggable rock; at least one tile own floor, so it joins the dungeon; none in
    /// <paramref name="avoid"/> and, without <paramref name="breach"/>, none beside another side's open ground.
    /// Fewest rock tiles first, then nearest <paramref name="home"/>, then Y, then X; null when none.</summary>
    public static TilePos? RoomSite(PlayerObservation observation, TilePos home, int side, ICollection<TilePos> skip,
        ICollection<TilePos>? avoid, bool breach)
    {
        PlayerTileGrid grid = observation.Tiles;
        PlayerId self = observation.Stamp.Player;
        TilePos? best = null;
        (int Rock, long Distance, int Y, int X) bestKey = (int.MaxValue, long.MaxValue, int.MaxValue, int.MaxValue);
        for (int y = 0; y + side <= grid.Height; y++)
            for (int x = 0; x + side <= grid.Width; x++)
            {
                var corner = new TilePos(x, y);
                if (skip.Contains(corner)) continue;
                int rock = 0, floor = 0;
                bool ok = true;
                for (int dy = 0; ok && dy < side; dy++)
                    for (int dx = 0; ok && dx < side; dx++)
                    {
                        var at = new TilePos(x + dx, y + dy);
                        PlayerTile tile = grid[at];
                        if (avoid?.Contains(at) == true || !breach && BesideOthersGround(grid, at, self)) ok = false;
                        else if (IsOwnFloor(tile, self) && tile.Room == default) floor++;
                        else if (tile.IsKnown && (tile.Flags & (PlayerTileFlags.Solid | PlayerTileFlags.Diggable)) == (PlayerTileFlags.Solid | PlayerTileFlags.Diggable)) rock++;
                        else ok = false;
                    }
                if (!ok || floor == 0 || rock == 0) continue;
                var key = (rock, DistanceSquared(new TilePos(x + side / 2, y + side / 2), home), y, x);
                if (key.CompareTo(bestKey) < 0) { bestKey = key; best = corner; }
            }
        return best;
    }

    /// <summary>The panel button that selects <paramref name="room"/>, when offered.</summary>
    public static PlayerAction? SelectRoom(PlayerObservation observation, RoomTypeId room)
    {
        GuiButtonTarget button = GuiButtonTarget.Room(room);
        foreach (GuiButtonTarget panel in observation.Offers.Panels)
            if (panel == button) return new PlayerAction.SelectPanel(button);
        return null;
    }

    /// <summary>Whether a visible threat (<see cref="IsThreat"/>) stands within <paramref name="tiles"/> (Chebyshev) of
    /// <paramref name="at"/>. One in sight but further off, such as a guard standing in its own room across the map,
    /// does not count.</summary>
    public static bool ThreatNear(PlayerObservation observation, TilePos at, int tiles)
    {
        foreach (PlayerVisibleThing thing in observation.Things)
            if (IsThreat(observation, thing) && Math.Max(Math.Abs(thing.Tile.X - at.X), Math.Abs(thing.Tile.Y - at.Y)) <= tiles)
                return true;
        return false;
    }

    /// <summary>The visible threat (<see cref="IsThreat"/>) nearest <paramref name="home"/>, or null.</summary>
    public static PlayerVisibleThing? NearestEnemy(PlayerObservation observation, TilePos home)
    {
        PlayerVisibleThing? enemy = null;
        foreach (PlayerVisibleThing thing in observation.Things)
            if (IsThreat(observation, thing) && (enemy is null || DistanceSquared(thing.Tile, home) < DistanceSquared(enemy.Value.Tile, home)))
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

    /// <summary>How many tiles of the square at <paramref name="corner"/> do not show <paramref name="room"/>
    /// yet could take it (own floor, no other room, not marked); -1 when one of them could not, so the square
    /// cannot be finished as it stands.</summary>
    public static int MissingRoomTiles(PlayerObservation observation, TilePos corner, int side, RoomTypeId room)
    {
        PlayerTileGrid grid = observation.Tiles;
        PlayerId self = observation.Stamp.Player;
        int missing = 0;
        for (int dy = 0; dy < side; dy++)
            for (int dx = 0; dx < side; dx++)
            {
                var at = new TilePos(corner.X + dx, corner.Y + dy);
                if (!grid.Contains(at)) return -1;
                PlayerTile tile = grid[at];
                if (tile.Room == room && tile.Owner == self) continue;
                if (!IsOwnFloor(tile, self) || tile.Room != default || (tile.Flags & PlayerTileFlags.Tagged) != 0) return -1;
                missing++;
            }
        return missing;
    }

    public static long DistanceSquared(TilePos a, TilePos b)
    {
        long dx = a.X - b.X, dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    /// <summary>Whether every tile of the square at <paramref name="corner"/> is the player's own claimed floor,
    /// in no room and untagged: ready to build on.</summary>
    public static bool SquareFree(PlayerObservation observation, TilePos corner, int side)
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

    /// <summary>Whether a tile of the square at <paramref name="corner"/> has a 4-neighbour outside it showing the player's own <paramref name="room"/>.</summary>
    private static bool Touches(PlayerTileGrid grid, TilePos corner, int side, RoomTypeId room, PlayerId self)
    {
        for (int i = 0; i < side; i++)
        {
            if (Shows(corner.X + i, corner.Y - 1) || Shows(corner.X + i, corner.Y + side)
                || Shows(corner.X - 1, corner.Y + i) || Shows(corner.X + side, corner.Y + i)) return true;
        }
        return false;

        bool Shows(int x, int y) => grid.Contains(new TilePos(x, y)) && grid[new TilePos(x, y)] is var t && t.Room == room && t.Owner == self;
    }

    /// <summary>Whether a 4-neighbour is known open ground of another side: a hero or rival keeper,
    /// neither neutral nor unowned.</summary>
    private static bool BesideOthersGround(PlayerTileGrid grid, TilePos at, PlayerId self)
    {
        foreach ((int dx, int dy) in Neighbours)
        {
            var next = new TilePos(at.X + dx, at.Y + dy);
            if (!grid.Contains(next)) continue;
            PlayerTile tile = grid[next];
            if (tile.IsKnown && (tile.Flags & PlayerTileFlags.Solid) == 0
                && tile.Owner != self && tile.Owner != PlayerIds.Neutral && tile.Owner != PlayerIds.Unowned) return true;
        }
        return false;
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
