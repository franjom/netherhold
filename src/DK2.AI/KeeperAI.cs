// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.AI;

/// <summary>A computer keeper, as an input source, not part of the simulation.</summary>
/// <remarks>
/// <para>Once per tick <see cref="Think"/> returns the commands this keeper would
/// issue by hand, for the host to put in the next <see cref="InputFrame"/>. Nothing
/// in the simulation sees this type, so the world hash is untouched. Source: the
/// original's <c>ComputerPlayer.cpp</c> (docs/KEEPER_AI.md §5, §12.1).</para>
/// <para>The proactive foundation of §12.8 and the attack response (<see cref="KeeperThreatResponder"/>).
/// Not implemented: the attack planner, battle spells and Call to Arms, traps and doors, prisoners,
/// retreat, training moves, make-room-for-a-researcher, room and mood messages (§12.3, §12.4).</para>
/// <para>Deviates: randomness is the keeper's own <see cref="SimRandom"/>, not the
/// world's stream (§12.1).</para>
/// </remarks>
public sealed class KeeperAI
{
    /// <summary>Ticks before an unfinished task is abandoned (§5.9).</summary>
    public const int TaskTimeoutTicks = 1680;

    /// <summary>Ticks after tagging before every gold tag is removed (§5.9).</summary>
    public const int UntagGoldAfterTicks = 240;

    /// <summary>
    /// Temporary approximation, refuted by the binary audit: DK2 checks the shared
    /// speed timer and activity accessor, not elapsed time since the last slap.
    /// Replacement dependencies: docs/research/computer-keeper-management.md §8.
    /// </summary>
    public const int SlapCooldownTicks = 300;

    /// <summary>The gold seam is mined in an expanding square until this radius (§5.2, task 6).</summary>
    public const int MineMaxRadius = 6;

    private readonly KeeperAIParameters _p;
    private readonly SimRandom _rng;
    private readonly Func<byte, bool>? _isRoomAvailable;
    private readonly BuildArea? _area;
    private readonly byte[]? _playerActivationOrder;
    private bool _directionsFixed;

    public KeeperAI(
        byte playerId, KeeperAIParameters parameters, SimRandom rng,
        Func<byte, bool>? isRoomAvailable = null, BuildArea? buildArea = null,
        IEnumerable<byte>? playerActivationOrder = null)
    {
        PlayerId = playerId;
        _p = parameters;
        _rng = rng;
        _isRoomAvailable = isRoomAvailable;
        _area = buildArea;
        _playerActivationOrder = playerActivationOrder?.ToArray();
    }

    public byte PlayerId { get; }
    public KeeperAIState State { get; } = new();

    /// <summary>One player tick (§5.1). Returns the commands to put in the next input frame.</summary>
    public IReadOnlyList<PlayerCommand> Think(GameDatabase database, WorldState world)
        => Think(new KeeperView(database, world, PlayerId, _playerActivationOrder));

    public IReadOnlyList<PlayerCommand> Think(KeeperView view)
    {
        if (view.PlayerId != PlayerId)
            throw new ArgumentException("The observation belongs to another keeper.", nameof(view));
        var commands = new List<PlayerCommand>();
        if (view.Status is PlayerStatus.Conquered or PlayerStatus.Lost)
            return commands;
        if (view.Heart is not TilePos heart) return commands;

        BuildArea area = _area ?? BuildArea.WholeMap(view.Width, view.Height);
        if (!_directionsFixed)
        {
            (State.DefendDirection, State.OpenDirection) = KeeperDirections.AtCreation(heart, view.Width, view.Height);
            State.SearchDirection = State.OpenDirection;
            State.SearchOrigin = heart;
            _directionsFixed = true;
        }

        int budget = _p.TendancySpeed;

        // 2. The task clock, and the timeout that abandons a stuck task.
        State.TaskTimeCounter++;
        if (State.TaskTimeCounter > TaskTimeoutTicks)
        {
            State.BeginTask(KeeperTask.Decide);
            State.WallHug = false;
        }

        // 3. Gold tags come off when their timer runs out.
        UntagGold(view, commands);

        // 5. The message walk, then 6. the head event, which spends the whole tick when it runs.
        var responder = new KeeperThreatResponder(view, State, _p, _rng, commands);
        responder.HandleMessages();
        budget = responder.Dispatch(budget);

        // 11. Imps, then 13. slaps. FightGoingOn would route 11 to the unbuilt battle step.
        if (budget > 0) CreateImps(view, commands, ref budget);
        if (budget > 0) Slap(view, commands, ref budget);

        // 15. The task machine, while the budget lasts.
        int guard = 64;
        while (budget > 0 && guard-- > 0 && State.Task != KeeperTask.Stop)
            RunTask(view, area, commands, ref budget);

        return commands;
    }

    // ----------------------------------------------------------------- housekeeping

    private void UntagGold(KeeperView view, List<PlayerCommand> commands)
    {
        if (State.UntagGoldCounter <= 0) return;
        if (--State.UntagGoldCounter > 0) return;

        var tiles = new List<TilePos>();
        for (int i = 0; i < view.TileCount; i++)
        {
            TilePos p = view.FromIndex(i);
            if (view.IsTaggedByMe(p) && view.IsGoldRock(p)) tiles.Add(p);
        }
        if (tiles.Count > 0) commands.Add(new TagTilesCommand(PlayerId, tiles, false));
    }

    private void CreateImps(KeeperView view, List<PlayerCommand> commands, ref int budget)
    {
        if (view.Availability(5, 4) != 3) return;
        int imps = view.ImpCount;
        int perTiles = Math.Max(1, (int)_p.TilesPerImpRatio);
        if (imps >= _p.MaximumImps || imps >= view.OwnedTiles / perTiles) return;
        commands.Add(new CreateImpCommand(PlayerId));
        budget--;
    }

    private void Slap(KeeperView view, List<PlayerCommand> commands, ref int budget)
    {
        long now = view.Tick.Value;
        foreach (KeeperCreatureView c in view.Creatures)
        {
            if (budget <= 0) return;
            if (c.OwnerPlayerId != PlayerId || c.Activity != CreatureActivity.Working) continue;
            if (!c.CanSlap) continue;
            CreatureDef def = view.Database.CreatureById(c.CreatureTypeId);
            if (c.Health * 2 <= CreatureLevelRules.MaxHealth(view.Database, def, c.Level)) continue;
            if (c.LastSlapTick != 0 && now - c.LastSlapTick < SlapCooldownTicks) continue;
            if (_rng.NextInt(100) >= 1) continue;
            commands.Add(new SlapCreatureCommand(PlayerId, c.Id));
            budget--;
        }
    }

    // ----------------------------------------------------------------- the task machine

    private void RunTask(KeeperView view, BuildArea area, List<PlayerCommand> commands, ref int budget)
    {
        switch (State.Task)
        {
            case KeeperTask.Decide:
                budget--;
                if (!PlanRoom(view, area) && WantsGold(view)) StartGold(view);
                break;

            case KeeperTask.FindSite:
                budget--;
                FindSite(view, area);
                break;

            case KeeperTask.DigRectangle:
                budget--;
                DigRectangle(view, commands);
                break;

            case KeeperTask.PlaceRoom:
                PlaceRoom(view, commands, ref budget);
                break;

            case KeeperTask.Done:
                budget--;
                State.BeginTask(KeeperTask.Decide);
                if (WantsGold(view)) StartGold(view);
                break;

            case KeeperTask.RouteToGold:
                budget--;
                RouteToGold(view);
                break;

            case KeeperTask.MineSeam:
                budget--;
                MineSeam(view, commands);
                break;

            case KeeperTask.DigPath:
                budget--;
                DigPath(view, area, commands);
                break;

            default:
                budget = 0;
                break;
        }
    }

    // ----------------------------------------------------------------- rooms

    /// <summary>The room planner (§5.5). True when a task was started.</summary>
    private bool PlanRoom(KeeperView view, BuildArea area)
    {
        if (!State.NextRoomToBuildSet)
        {
            State.CurrentRoomToBuild = 0; // 0x004FC690 clears this before the chooser.
            byte? chosen = RoomPlanner.ChooseRoom(
                view, _p, _isRoomAvailable ?? (id => view.Availability(1, id) == 3),
                State.CurrentRoomToBuild);
            if (chosen is null)
            {
                State.NextRoomToBuild = (byte)RoomTypeId.Portal;
                State.AllRoomsBuilt = true;
            }
            else
            {
                State.AllRoomsBuilt = false;
                State.NextRoomToBuild = chosen.Value;
            }
            State.NextRoomToBuildSet = true;
        }

        byte room = State.NextRoomToBuild;

        // A neutral room of that type within reach is claimed by digging to it.
        KeeperRoomView? neutral = RoomPlanner.ClaimableNeutralRoom(view, _p, room, area, out TilePos origin);
        if (neutral is not null)
        {
            TilePos target = KeeperView.Centre(neutral);
            State.DigRoomAtCurrent = origin;
            State.DigRoomAt = target;
            State.WallHug = false;
            State.CurrentRoomToBuild = room;
            State.NextRoomToBuildSet = false;
            State.BeginTask(KeeperTask.DigPath, KeeperTask.Done);
            return true;
        }

        if (room == (byte)RoomTypeId.Portal || room == (byte)RoomTypeId.HeroPortal)
        {
            State.NextRoomToBuildSet = false;
            return false;
        }

        RoomDef def = view.Database.RoomById(room);
        if (RoomRules.CostPerTile(def) * _p.DefaultRoomSize > view.Gold)
            return StartGold(view);

        State.CurrentRoomToBuild = room;
        State.NextRoomToBuildSet = false;
        State.SearchDirection = RoomPlanner.DirectionFor(room, State, _rng);
        State.SearchOrigin = RoomPlanner.Origin(view, _p, room, _rng) ?? view.Heart!.Value;
        State.DirectionsTried = 0;
        State.BeginTask(KeeperTask.FindSite);
        return true;
    }

    private void FindSite(KeeperView view, BuildArea area)
    {
        RoomSite? site = SiteFinder.Find(view, State.SearchOrigin, State.SearchDirection,
            _p.DefaultRoomSize, _p.TilesLeftBetweenRooms & 0xF, _p.Openness, area);
        if (site is RoomSite found)
        {
            State.RectangleOrigin = found.Origin;
            State.RectangleWidth = found.Width;
            State.RectangleHeight = found.Height;
            State.BeginTask(KeeperTask.DigRectangle);
            return;
        }

        State.SearchDirection = (State.SearchDirection + 1) & 3;
        if (++State.DirectionsTried < 4) return;

        State.DirectionsTried = 0;
        KeeperRoomView? reseed = _rng.NextInt(100) < 95 ? RoomPlanner.RandomOwnRoom(view, _rng, avoidBridges: false) : null;
        if (reseed is not null)
        {
            State.SearchOrigin = KeeperView.Centre(reseed);
            return;
        }
        State.BeginTask(KeeperTask.Decide);
    }

    private RoomSite Rectangle => new(State.RectangleOrigin, State.RectangleWidth, State.RectangleHeight);

    private void DigRectangle(KeeperView view, List<PlayerCommand> commands)
    {
        RoomSite rect = Rectangle;
        var toTag = new List<TilePos>();
        foreach (TilePos t in rect.Tiles())
            if (view.InBounds(t) && view.Terrain(t).IsTaggable && !view.IsTaggedByMe(t)) toTag.Add(t);
        if (toTag.Count > 0)
        {
            commands.Add(new TagTilesCommand(PlayerId, toTag, true));
            State.UntagGoldCounter = UntagGoldAfterTicks;
        }

        // Is the site joined to the dungeon once the tags are dug? If not, dig a
        // corridor from the nearest reachable tile first, then come back here.
        HashSet<int> reach = view.ReachableFromHeart(throughMyTags: true);
        bool connected = false;
        foreach (TilePos t in rect.Tiles())
        {
            if (reach.Contains(view.IndexOf(t))) { connected = true; break; }
            foreach ((int dx, int dy) in KeeperDirections.Clockwise)
            {
                var n = new TilePos(t.X + dx, t.Y + dy);
                if (view.InBounds(n) && reach.Contains(view.IndexOf(n))) { connected = true; break; }
            }
            if (connected) break;
        }

        if (connected || toTag.Count == 0 && reach.Count == 0)
        {
            State.BeginTask(KeeperTask.PlaceRoom);
            return;
        }

        var centre = new TilePos(rect.Origin.X + (rect.Width / 2), rect.Origin.Y + (rect.Height / 2));
        TilePos start = Nearest(view, reach, centre) ?? view.Heart!.Value;
        TilePos end = rect.Tiles().OrderBy(t => KeeperView.DistanceSquared(t, start)).First();
        State.JoinPathStart = start;
        State.DigRoomAtCurrent = start;
        State.DigRoomAt = end;
        State.WallHug = false;
        State.BeginTask(KeeperTask.DigPath, KeeperTask.DigRectangle);
    }

    private void PlaceRoom(KeeperView view, List<PlayerCommand> commands, ref int budget)
    {
        RoomDef def = view.Database.RoomById(State.CurrentRoomToBuild);
        var build = new List<TilePos>();
        var retag = new List<TilePos>();
        bool allDone = true;

        foreach (TilePos t in Rectangle.Tiles())
        {
            if (!view.InBounds(t)) continue;
            KeeperTileView tile = view.Tile(t);
            if (tile.TerrainId == def.TerrainId && tile.OwnerPlayerId == PlayerId) continue;
            allDone = false;
            if (budget - build.Count - retag.Count <= 0) break;

            if (view.CanBuild(t, def) == RoomPlacement.Allowed)
                build.Add(t);
            else if (view.Terrain(t).IsTaggable && !view.IsTaggedByMe(t))
                retag.Add(t);
        }

        if (allDone)
        {
            budget--;
            State.BeginTask(KeeperTask.Done);
            return;
        }
        foreach (TilePos tile in build)
            commands.Add(new BuildRoomCommand(PlayerId, def.RoomId, [tile]));
        if (retag.Count > 0)
        {
            commands.Add(new TagTilesCommand(PlayerId, retag, true));
            State.UntagGoldCounter = UntagGoldAfterTicks;
        }
        int spent = build.Count + retag.Count;
        // Nothing to do yet means the imps have not finished: wait for the next tick.
        budget = spent == 0 ? 0 : budget - spent;
    }

    // ----------------------------------------------------------------- gold

    private bool WantsGold(KeeperView view) =>
        view.Gold < _p.TendancyMoney
        && State.Task is not (KeeperTask.RouteToGold or KeeperTask.MineSeam)
        && State.NextTask != KeeperTask.MineSeam;

    /// <summary>The gold step (§5.5): pick a known deposit, or scan for one. True when a task started.</summary>
    private bool StartGold(KeeperView view)
    {
        if (State.GoldDeposits.Count == 0)
        {
            State.GoldDeposits.AddRange(GoldScanner.Scan(view));
            State.CurrentGoldArea = 0;
        }
        if (State.GoldDeposits.Count == 0) return false; // the original casts Create Gold here; no such command yet
        State.CurrentGoldArea %= State.GoldDeposits.Count;
        State.BeginTask(KeeperTask.RouteToGold);
        return true;
    }

    private void RouteToGold(KeeperView view)
    {
        if (State.GoldDeposits.Count == 0)
        {
            State.BeginTask(KeeperTask.Decide);
            return;
        }
        GoldDeposit deposit = State.GoldDeposits[State.CurrentGoldArea % State.GoldDeposits.Count];
        KeeperRoomView? from = view.NearestOwnRoom(deposit.Centre);
        State.DigRoomAtCurrent = from is null ? view.Heart!.Value : KeeperView.Centre(from);
        State.DigRoomAt = deposit.Centre;
        State.WallHug = false;
        State.MineRadius = 0;
        State.BeginTask(KeeperTask.DigPath, KeeperTask.MineSeam);
    }

    private void MineSeam(KeeperView view, List<PlayerCommand> commands)
    {
        if (State.GoldDeposits.Count == 0)
        {
            State.BeginTask(KeeperTask.Decide);
            return;
        }
        TilePos c = State.GoldDeposits[State.CurrentGoldArea % State.GoldDeposits.Count].Centre;
        int r = State.MineRadius;
        var tiles = new List<TilePos>();
        for (int y = c.Y - r; y <= c.Y + r; y++)
            for (int x = c.X - r; x <= c.X + r; x++)
            {
                if (Math.Max(Math.Abs(x - c.X), Math.Abs(y - c.Y)) != r) continue;
                var t = new TilePos(x, y);
                if (view.IsGoldRock(t) && view.Terrain(t).IsTaggable && !view.IsTaggedByMe(t)) tiles.Add(t);
            }
        if (tiles.Count > 0)
        {
            commands.Add(new TagTilesCommand(PlayerId, tiles, true));
            State.UntagGoldCounter = UntagGoldAfterTicks;
        }

        if (++State.MineRadius > MineMaxRadius)
        {
            State.MineRadius = 0;
            State.CurrentGoldArea = (State.CurrentGoldArea + 1) % State.GoldDeposits.Count;
            State.GoldDeposits.Clear(); // rescanned next time, as the original does at each gold step
            State.BeginTask(KeeperTask.Decide);
        }
    }

    // ----------------------------------------------------------------- paths

    private void DigPath(KeeperView view, BuildArea area, List<PlayerCommand> commands)
    {
        if (!area.Contains(State.DigRoomAtCurrent))
        {
            State.BeginTask(KeeperTask.Done);
            return;
        }

        // Once the target is joined to the dungeon (through tags), the path is done.
        if (view.ReachableFromHeart(throughMyTags: true).Contains(view.IndexOf(State.DigRoomAt)))
        {
            State.WallHug = false;
            State.BeginTask(State.NextTask);
            return;
        }

        WalkStep step = LineWalker.Step(view, State);
        switch (step.Outcome)
        {
            case WalkOutcome.Dig:
                if (view.Terrain(step.Tile).IsTaggable && !view.IsTaggedByMe(step.Tile))
                {
                    commands.Add(new TagTilesCommand(PlayerId, [step.Tile], true));
                    State.UntagGoldCounter = UntagGoldAfterTicks;
                }
                break;
            case WalkOutcome.Arrived:
                State.WallHug = false;
                State.BeginTask(State.NextTask);
                break;
            case WalkOutcome.Moved:
                break;
            case WalkOutcome.Hazard: // Bridge planning is missing; BuildRoomCommand already supports it.
            case WalkOutcome.Failed:
                State.WallHug = false;
                State.BeginTask(KeeperTask.Done);
                break;
        }
    }

    private static TilePos? Nearest(KeeperView view, HashSet<int> indices, TilePos to)
    {
        TilePos? best = null;
        int bestDist = int.MaxValue;
        foreach (int i in indices)
        {
            TilePos p = view.FromIndex(i);
            if (!view.IsPassable(p)) continue;
            int d = KeeperView.DistanceSquared(p, to);
            if (d < bestDist)
            {
                bestDist = d;
                best = p;
            }
        }
        return best;
    }
}
