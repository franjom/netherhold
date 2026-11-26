// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Players;

namespace DK2.AI.Players;

/// <summary>
/// Trap sites on the routes attackers take to the Heart (campaign bot defence plan, Task D4). Pure routines over
/// one <see cref="PlayerObservation"/>: bot policy, not a claim about DK2's own keepers. Orders are deterministic.
/// </summary>
public static class PlayerTrapRoutines
{
    /// <summary>Open floor running straight on from a site that makes it a long corridor for a Boulder: the site and
    /// four more, five in all (DK2's own keeper uses the same four, <c>KeeperTrapDoorStep.LongCorridorTiles</c>).</summary>
    public const int LongCorridorTiles = 4;

    /// <summary>Ground an attacker may walk: known, not solid, not lava.</summary>
    public static bool IsWalkable(in PlayerTile tile) =>
        tile.IsKnown && (tile.Flags & (PlayerTileFlags.Solid | PlayerTileFlags.Lava)) == 0;

    /// <summary>Whether attackers come in here: a hero gate or hero portal, or a Portal a rival keeper owns (an unclaimed
    /// one is only a map resource to claim, owner 2026-11-15).</summary>
    public static bool IsEntryRoom(in PlayerTile tile, PlayerId self) => tile.Room switch
    {
        RoomTypeId.HeroGateFinal or RoomTypeId.HeroGateTile or RoomTypeId.HeroGate2By2 or RoomTypeId.HeroGateFrontEnd
            or RoomTypeId.HeroGate3By1 or RoomTypeId.HeroPortal => true,
        RoomTypeId.Portal => IsEnemyGround(tile, self),
        _ => false,
    };

    /// <summary>Another keeper's or the heroes' claimed ground.</summary>
    public static bool IsEnemyGround(in PlayerTile tile, PlayerId self) =>
        tile.IsKnown && tile.Owner != self && tile.Owner != PlayerIds.Neutral && tile.Owner != PlayerIds.Unowned;

    /// <summary>A corridor tile: open floor, not on the map's border, with solid ground on two opposite sides, neither
    /// side marked for digging (a marked wall will open). Modelled on DK2's own keeper test (<c>004FFA50</c>), which
    /// tries the left and right pair alone and uses the navigation solid test; the bot tries either pair.</summary>
    public static bool IsCorridor(PlayerTileGrid grid, TilePos at)
    {
        if (!grid.Contains(at) || !IsWalkable(grid[at])) return false;
        if (at.X == 0 || at.Y == 0 || at.X == grid.Width - 1 || at.Y == grid.Height - 1) return false;
        return (Solid(grid, at.X - 1, at.Y) && Solid(grid, at.X + 1, at.Y)) || (Solid(grid, at.X, at.Y - 1) && Solid(grid, at.X, at.Y + 1));
    }

    /// <summary>Whether corridor runs straight on from <paramref name="at"/> for <see cref="LongCorridorTiles"/> tiles
    /// in one direction: where a Boulder rolls far.</summary>
    public static bool IsLongCorridor(PlayerTileGrid grid, TilePos at)
    {
        foreach ((int dx, int dy) in Sides)
        {
            int run = 0;
            while (run < LongCorridorTiles && IsCorridor(grid, new TilePos(at.X + dx * (run + 1), at.Y + dy * (run + 1)))) run++;
            if (run == LongCorridorTiles) return true;
        }
        return false;
    }

    /// <summary>
    /// The trap sites on the attack routes, the outermost (farthest from the Heart along the route) first. A route is
    /// the shortest walkable path from an entry in <paramref name="entries"/> to the own Dungeon Heart. A site is the
    /// player's own plain floor (no room, no own trap, no door), a corridor (<see cref="IsCorridor"/>), not in
    /// <paramref name="skip"/>, and fewer than <paramref name="spacing"/> tiles (Chebyshev) from no own trap, door
    /// or earlier site. Empty when the Heart is not seen.
    /// </summary>
    public static List<TilePos> Sites(PlayerObservation observation, IEnumerable<TilePos> entries, int spacing, ICollection<TilePos> skip)
    {
        if (observation is null) throw new ArgumentNullException(nameof(observation));
        if (entries is null) throw new ArgumentNullException(nameof(entries));
        if (skip is null) throw new ArgumentNullException(nameof(skip));
        PlayerTileGrid grid = observation.Tiles;
        PlayerId self = observation.Stamp.Player;
        int width = grid.Width;
        int[] distance = new int[width * grid.Height];
        Array.Fill(distance, -1);
        var queue = new Queue<TilePos>();
        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < width; x++)
            {
                var at = new TilePos(x, y);
                if (!IsWalkable(grid[at]) || !NextToHeart(grid, at, self)) continue;
                distance[y * width + x] = 0;
                queue.Enqueue(at);
            }
        if (queue.Count == 0) return [];
        while (queue.Count > 0)
        {
            TilePos at = queue.Dequeue();
            int next = distance[at.Y * width + at.X] + 1;
            foreach ((int dx, int dy) in Sides)
            {
                var to = new TilePos(at.X + dx, at.Y + dy);
                if (!grid.Contains(to) || distance[to.Y * width + to.X] >= 0 || !IsWalkable(grid[to])) continue;
                distance[to.Y * width + to.X] = next;
                queue.Enqueue(to);
            }
        }

        // Each entry's route walks downhill to the Heart; a route that meets an earlier one follows it from there.
        bool[] onRoute = new bool[distance.Length];
        foreach (TilePos entry in entries)
        {
            if (!grid.Contains(entry)) continue;
            TilePos at = entry;
            while (distance[at.Y * width + at.X] >= 0 && !onRoute[at.Y * width + at.X])
            {
                onRoute[at.Y * width + at.X] = true;
                int here = distance[at.Y * width + at.X];
                if (here == 0) break;
                foreach ((int dx, int dy) in Sides)
                {
                    var to = new TilePos(at.X + dx, at.Y + dy);
                    if (grid.Contains(to) && distance[to.Y * width + to.X] == here - 1) { at = to; break; }
                }
            }
        }

        var blockers = new List<TilePos>();
        var candidates = new List<TilePos>();
        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < width; x++)
            {
                var at = new TilePos(x, y);
                PlayerTile tile = grid[at];
                if ((tile.Flags & PlayerTileFlags.OwnTrap) != 0) { blockers.Add(at); continue; }
                if (!onRoute[y * width + x] || tile.Owner != self || tile.Room != default || !IsWalkable(tile)
                    || (tile.Flags & PlayerTileFlags.Water) != 0 || skip.Contains(at) || !IsCorridor(grid, at)) continue;
                candidates.Add(at);
            }
        foreach (PlayerVisibleThing thing in observation.Things)
            if (thing.Kind == PlayerThingKind.Door) blockers.Add(thing.Tile);
        candidates.Sort((a, b) =>
        {
            int c = distance[b.Y * width + b.X].CompareTo(distance[a.Y * width + a.X]);
            return c != 0 ? c : a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X);
        });
        var sites = new List<TilePos>();
        foreach (TilePos at in candidates)
        {
            if (Near(blockers, at, spacing) || Near(sites, at, spacing)) continue;
            sites.Add(at);
        }
        return sites;
    }

    /// <summary>The trap for <paramref name="site"/> by the kind of place it is (<see cref="SiteKind"/>), after the
    /// owner's play (2026-11-15, Q851): Sentry, Spike, Freeze and Fireburst traps on choke points, Fear traps in narrow
    /// corridors, the Boulder where the corridor runs long and straight. Any other trap the player can buy keeping
    /// <paramref name="goldKept"/> is the fallback, a Boulder on a short corridor too ("let it decide"). Within a rank the
    /// dearer goes first, then the lower id. Null when none can be bought.</summary>
    public static PlayerWorkshopOffer? Choose(PlayerObservation observation, TilePos site, long goldKept)
    {
        if (observation is null) throw new ArgumentNullException(nameof(observation));
        TrapSite kind = SiteKind(observation.Tiles, site);
        PlayerWorkshopOffer? best = null;
        int bestRank = int.MaxValue;
        foreach (PlayerWorkshopOffer offer in observation.Offers.Workshop)
        {
            if (offer.Kind != PlayerWorkshopKind.Trap || offer.NeedsWorkshop || !offer.Affordable) continue;
            if (observation.Gold.Value - offer.Cost.Value < goldKept) continue;
            int rank = Rank(kind, RoleOf(offer));
            if (best is not PlayerWorkshopOffer b || rank < bestRank
                || (rank == bestRank && (offer.Cost.Value > b.Cost.Value || (offer.Cost.Value == b.Cost.Value && offer.Id < b.Id))))
            {
                best = offer;
                bestRank = rank;
            }
        }
        return best;
    }

    /// <summary>What a corridor site is: a long straight run (<see cref="IsLongCorridor"/>), a choke point (it opens
    /// onto floor that is no corridor: a room, a hall), or a narrow corridor.</summary>
    public static TrapSite SiteKind(PlayerTileGrid grid, TilePos at)
    {
        if (grid is null) throw new ArgumentNullException(nameof(grid));
        if (IsLongCorridor(grid, at)) return TrapSite.LongCorridor;
        foreach ((int dx, int dy) in Sides)
        {
            var to = new TilePos(at.X + dx, at.Y + dy);
            if (grid.Contains(to) && IsWalkable(grid[to]) && !IsCorridor(grid, to)) return TrapSite.ChokePoint;
        }
        return TrapSite.NarrowCorridor;
    }

    /// <summary>A trap's role, from its name on the Workshop panel (what the player reads).</summary>
    public static TrapRole RoleOf(in PlayerWorkshopOffer offer)
    {
        string name = offer.Name ?? "";
        if (name.IndexOf("Boulder", StringComparison.OrdinalIgnoreCase) >= 0) return TrapRole.Boulder;
        if (name.IndexOf("Fear", StringComparison.OrdinalIgnoreCase) >= 0) return TrapRole.Fear;
        foreach (string strike in StrikeTraps)
            if (name.IndexOf(strike, StringComparison.OrdinalIgnoreCase) >= 0) return TrapRole.Strike;
        return TrapRole.Other;
    }

    private static readonly string[] StrikeTraps = ["Sentry", "Spike", "Freeze", "Fireburst"];

    private static int Rank(TrapSite site, TrapRole role) => (site, role) switch
    {
        (TrapSite.LongCorridor, TrapRole.Boulder) => 0,
        (TrapSite.LongCorridor, TrapRole.Fear) => 1,
        (TrapSite.LongCorridor, TrapRole.Strike) => 2,
        (TrapSite.ChokePoint, TrapRole.Strike) => 0,
        (TrapSite.ChokePoint, TrapRole.Fear) => 1,
        (TrapSite.NarrowCorridor, TrapRole.Fear) => 0,
        (TrapSite.NarrowCorridor, TrapRole.Strike) => 1,
        _ => 3,
    };

    /// <summary>The player's own traps and trap blueprints in view.</summary>
    public static int OwnTraps(PlayerTileGrid grid)
    {
        if (grid is null) throw new ArgumentNullException(nameof(grid));
        int count = 0;
        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
                if ((grid[new TilePos(x, y)].Flags & PlayerTileFlags.OwnTrap) != 0) count++;
        return count;
    }

    private static bool NextToHeart(PlayerTileGrid grid, TilePos at, PlayerId self)
    {
        if (IsHeart(grid[at], self)) return true;
        foreach ((int dx, int dy) in Sides)
        {
            var to = new TilePos(at.X + dx, at.Y + dy);
            if (grid.Contains(to) && IsHeart(grid[to], self)) return true;
        }
        return false;
    }

    private static bool IsHeart(in PlayerTile tile, PlayerId self) => tile.Room == RoomTypeId.DungeonHeart && tile.Owner == self;

    private static bool Solid(PlayerTileGrid grid, int x, int y)
    {
        var at = new TilePos(x, y);
        return grid.Contains(at) && (grid[at].Flags & (PlayerTileFlags.Solid | PlayerTileFlags.Tagged)) == PlayerTileFlags.Solid;
    }

    private static bool Near(List<TilePos> tiles, TilePos at, int spacing)
    {
        foreach (TilePos t in tiles)
            if (Math.Max(Math.Abs(t.X - at.X), Math.Abs(t.Y - at.Y)) < spacing) return true;
        return false;
    }

    private static readonly (int Dx, int Dy)[] Sides = [(0, -1), (1, 0), (0, 1), (-1, 0)];
}

/// <summary>The kind of place a trap site is (<see cref="PlayerTrapRoutines.SiteKind"/>).</summary>
public enum TrapSite : byte { NarrowCorridor, ChokePoint, LongCorridor }

/// <summary>What a trap is for, after the owner's play (Q851): strike traps (Sentry, Spike, Freeze, Fireburst) on choke
/// points, Fear in narrow corridors, the Boulder on long straight runs; any other is a fallback.</summary>
public enum TrapRole : byte { Other, Strike, Fear, Boulder }
