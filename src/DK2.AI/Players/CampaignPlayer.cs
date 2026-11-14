// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Players;

namespace DK2.AI.Players;

/// <summary>
/// A goal-directed campaign player that sees only <see cref="PlayerObservation"/>s (campaign player
/// plan, task 5). Each decision, within the action budget: skip an offered intro; answer enemies,
/// activate specials, build the next missing room and mark the frontier, as its
/// <see cref="PlayerStrategy"/> says (stage 3 plan, Task 1; the options' by default, changed between
/// decisions by <see cref="Apply"/>). It keeps developing (Task 2): grows outgrown rooms, builds offered
/// extra rooms, carries idle fighters to rooms it uses, and digs within its <see cref="BreachMode"/>. It
/// checks postconditions, not receipts: a build is done when its type's tiles rise, a drop when the hand
/// empties, progress when floor, rooms, creatures or gold gain. Without a gain for the stall window it untags and skips its marks and squares, and after its
/// recovery attempts reports a limitation. It reads nothing but the observation: no Lord position,
/// trigger id, hidden map data or recorded timeline. Not thread-safe.
/// </summary>
public sealed class CampaignPlayer : IPlayerController
{
    private readonly CampaignPlayerOptions _options;
    private PlayerStrategy _strategy;
    private bool _untagAll;
    private (int Width, int Height)? _map;
    private readonly Dictionary<RoomTypeId, PendingRoom> _pendingRooms = [];
    private readonly Dictionary<RoomTypeId, HashSet<TilePos>> _triedSquares = [];
    private readonly Dictionary<PlayerActionId, PlayerAction> _sent = [];
    private readonly HashSet<TilePos> _abandoned = [], _failedDrops = [];
    private long _nextActionId = 1;
    private Gains _best;
    private GameTick _gainTick;
    private int _recoveries;
    private (TilePos Tile, int Hand)? _lastDrop;
    private GameTick? _lastDropTick;
    private TilePos? _carryTo, _workDrop;
    private readonly HashSet<TilePos> _refusedWork = [];
    private GameTick? _lastWorkTick;
    private readonly HashSet<TilePos> _sightings = [], _danger = [];
    private int _dangerRadius = -1;

    /// <summary>A build sent at <paramref name="Since"/> while the player owned <paramref name="Tiles"/> of its type.</summary>
    private readonly record struct PendingRoom(TilePos Corner, GameTick Since, int Tiles);

    /// <summary>What counts as progress: each only when it rises.</summary>
    private readonly record struct Gains(int Floor, int RoomTiles, int Creatures, int Gold)
    {
        public bool Beats(in Gains best) => Floor > best.Floor || RoomTiles > best.RoomTiles || Creatures > best.Creatures || Gold > best.Gold;
        public Gains Max(in Gains other) => new(Math.Max(Floor, other.Floor), Math.Max(RoomTiles, other.RoomTiles),
            Math.Max(Creatures, other.Creatures), Math.Max(Gold, other.Gold));
    }

    public CampaignPlayer(CampaignPlayerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        options.Validate();
        _strategy = options.Strategy;
        IReadOnlyList<string> problems = _strategy.Validate();
        if (problems.Count > 0) throw new ArgumentException("The options' strategy is refused: " + string.Join("; ", problems), nameof(options));
        _best = new Gains(-1, -1, -1, -1);
    }

    public CampaignPlayerOptions Options => _options;

    /// <summary>The strategy the routines read now.</summary>
    public PlayerStrategy Strategy => _strategy;

    /// <summary>Makes <paramref name="next"/> the strategy from the next decision on (campaign bot stage 3
    /// plan, Task 1). Refused, leaving the current one, with every problem named, when it does not
    /// validate (against the map's size once an observation has been seen). A pending room no longer
    /// listed (in its rooms or extra rooms) is given up; with digging turned off, the standing marks are taken back over the next
    /// decisions, within the action bound. Call it between decisions, never during one.</summary>
    public void Apply(PlayerStrategy next)
    {
        if (next is null) throw new ArgumentNullException(nameof(next));
        IReadOnlyList<string> problems = next.Validate(_map?.Width, _map?.Height);
        if (problems.Count > 0) throw new ArgumentException("The strategy is refused: " + string.Join("; ", problems), nameof(next));
        foreach (RoomTypeId room in _pendingRooms.Keys.Where(r => !next.Rooms.Contains(r) && !next.ExtraRooms.Contains(r)).ToList())
            _pendingRooms.Remove(room);
        if (next.Dig == DigMode.Off && _strategy.Dig != DigMode.Off) _untagAll = true;
        if (next.Dig != DigMode.Off) _untagAll = false;
        _strategy = next;
    }

    public PlayerDecision Decide(PlayerObservation observation)
    {
        if (observation is null) throw new ArgumentNullException(nameof(observation));
        if (observation.Stamp.RunId != _options.RunId || observation.Stamp.Player != _options.Player)
            throw new ArgumentException("The observation belongs to another run or player.", nameof(observation));

        _map = (observation.Tiles.Width, observation.Tiles.Height);
        Learn(observation);
        if (observation.Phase == PlayerPhase.Ended) return PlayerDecision.Wait(_strategy.AdvanceTicks, "await verdict: the game has ended");
        if (observation.Phase == PlayerPhase.Intro)
            return (observation.Offers.Actions & PlayerActionKinds.SkipIntro) != 0
                ? Send([new PlayerAction.SkipIntro()], "skip the intro") : PlayerDecision.Wait(1, "intro playing");

        TilePos home = PlayerActionRoutines.Home(observation);
        var actions = new List<PlayerAction>(_options.MaxActionsPerDecision);
        var why = new List<string>(4);
        if (Stalled(observation, home, actions) is PlayerDecision limited) return limited;
        if (actions.Count > 0) why.Add("recover");
        if (_untagAll && Untag(observation, actions)) why.Add("untag");
        bool hand = Hand(observation, home, actions);
        if (hand) why.Add("hand");
        if (Room(observation, home, actions)) why.Add("room");
        if (!hand && Work(observation, home, actions)) why.Add("work");
        if (_strategy.UseSpecials && Fits(actions, 1) && PlayerActionRoutines.Special(observation, _options) is PlayerAction special) { actions.Add(special); why.Add("special"); }
        if (Dig(observation, home, actions)) why.Add("dig");
        return actions.Count == 0
            ? PlayerDecision.Wait(_strategy.AdvanceTicks, "wait: nothing offered to do")
            : Send(actions, string.Join(", ", why));
    }

    private PlayerDecision Send(List<PlayerAction> actions, string reason)
    {
        foreach (PlayerAction action in actions) _sent[new PlayerActionId(_nextActionId++)] = action;
        return new PlayerDecision(actions, _strategy.AdvanceTicks, reason);
    }

    /// <summary>Reads receipts for builds refused because of their square, and releases rooms the
    /// observation shows covering their squares.</summary>
    private void Learn(PlayerObservation observation)
    {
        foreach (PlayerReceipt receipt in observation.Receipts)
        {
            if (!_sent.TryGetValue(receipt.Id, out PlayerAction? action)) continue;
            if (action is PlayerAction.BuildRoom build && receipt.Status is PlayerReceiptStatus.Refused or PlayerReceiptStatus.Cancelled
                && _pendingRooms.Remove(build.Room) && receipt.Reason is PlayerRefusalReason.NotOffered or PlayerRefusalReason.OutOfBounds)
                Tried(build.Room).Add(build.From);
            if (receipt.Status != PlayerReceiptStatus.Queued) _sent.Remove(receipt.Id);
        }
        var done = new List<RoomTypeId>();
        foreach (KeyValuePair<RoomTypeId, PendingRoom> pending in _pendingRooms)
        {
            // Done when the type owns more tiles than when it was sent (this square or another).
            if (OwnedTiles(observation, pending.Key) > pending.Value.Tiles) done.Add(pending.Key);
            else if (observation.Stamp.Tick.Value - pending.Value.Since.Value >= _options.PendingRoomTicks)
            {
                Tried(pending.Key).Add(pending.Value.Corner);
                done.Add(pending.Key);
            }
        }
        foreach (RoomTypeId room in done) _pendingRooms.Remove(room);
        foreach (PlayerVisibleThing thing in observation.Things)
            if (PlayerActionRoutines.IsThreat(observation, thing) && _sightings.Add(thing.Tile) && _dangerRadius >= 0)
                AddClearance(thing.Tile, _dangerRadius);
    }

    /// <summary>A held item goes down (near the nearest enemy, else near home), skipping tiles a
    /// drop already failed on; with an empty hand and an enemy, a free fighter is lifted.</summary>
    private bool Hand(PlayerObservation observation, TilePos home, List<PlayerAction> actions)
    {
        if (_lastDrop is (TilePos tile, int hand) && observation.HandCount >= hand)
        {
            _failedDrops.Add(tile);
            if (_workDrop == tile) _refusedWork.Add(tile);
        }
        _lastDrop = null;
        _workDrop = null;
        if (observation.HandCount == 0) _failedDrops.Clear();
        if (!Fits(actions, 1)) return false;
        // Engage answers the nearest enemy, HoldHome only one near home, Off none; with none, anything
        // held goes down near home.
        PlayerVisibleThing? enemy = _strategy.Fight == FightMode.Off ? null : PlayerActionRoutines.NearestEnemy(observation, home);
        if (_strategy.Fight == FightMode.HoldHome && enemy is PlayerVisibleThing far
            && PlayerActionRoutines.DistanceSquared(far.Tile, home) > (long)_strategy.HomeGuardTiles * _strategy.HomeGuardTiles) enemy = null;
        if (observation.HandCount > 0)
        {
            // A creature lifted to work goes to its room unless an enemy needs it or a drop there failed.
            TilePos? work = enemy is null && _carryTo is TilePos room && !_refusedWork.Contains(room) ? room : null;
            _carryTo = null;
            _workDrop = work;
            if ((observation.Offers.Actions & PlayerActionKinds.DropAt) == 0
                || (work ?? PlayerActionRoutines.NearestOwnFloor(observation, enemy?.Tile ?? home, _failedDrops)) is not TilePos drop) return false;
            actions.Add(new PlayerAction.DropAt(drop));
            _lastDrop = (drop, observation.HandCount);
            _lastDropTick = observation.Stamp.Tick;
            return true;
        }
        // A fighter already near the enemy, or already on the floor a drop would choose, stays put.
        if (enemy is not PlayerVisibleThing target
            || _lastDropTick is GameTick dropped && observation.Stamp.Tick.Value - dropped.Value < _options.DropCooldownTicks
            || PlayerActionRoutines.NearestOwnFloor(observation, target.Tile, _failedDrops) is not TilePos placed
            || PlayerActionRoutines.Fighter(observation, _options, target.Tile, placed, _options.EngagedTiles, home) is not PlayerVisibleThing fighter) return false;
        actions.Add(new PlayerAction.PickUp(fighter.Handle));
        return true;
    }

    /// <summary>Unless a build is pending, the first of: a missing listed room, a listed room its
    /// creatures or gold have outgrown (grown beside itself), an offered extra room not owned (rebuilt if
    /// lost); growth and extras wait while a missing listed room is offered but unaffordable;
    /// pressed then built when the panel offers it and the player can pay. One not yet offered or
    /// affordable waits and the next may go.</summary>
    private bool Room(PlayerObservation observation, TilePos home, List<PlayerAction> actions)
    {
        if (_pendingRooms.Count > 0 || !Fits(actions, 2)) return false;
        bool waiting = false;
        foreach (RoomTypeId room in _strategy.Rooms)
        {
            if (Owns(observation, room)) continue;
            if (Build(observation, home, room, grow: false, actions)) return true;
            waiting |= Unaffordable(observation, room);
        }
        // A listed room the panel offers but the player cannot yet pay for keeps the gold for itself.
        if (waiting) return false;
        foreach (RoomTypeId room in _strategy.Rooms)
            if (Outgrown(observation, room) && Build(observation, home, room, grow: true, actions)) return true;
        foreach (RoomTypeId room in _strategy.ExtraRooms)
            if (!_strategy.Rooms.Contains(room) && !Owns(observation, room) && Build(observation, home, room, grow: false, actions)) return true;
        return false;
    }

    private bool Unaffordable(PlayerObservation observation, RoomTypeId room)
    {
        foreach (PlayerRoomOffer offer in observation.Offers.Rooms)
            if (offer.Room == room) return (long)offer.Cost.Value * _strategy.RoomSide * _strategy.RoomSide > observation.Gold.Value;
        return false;
    }

    private bool Build(PlayerObservation observation, TilePos home, RoomTypeId room, bool grow, List<PlayerAction> actions)
    {
        if (PlayerActionRoutines.BuildRoom(observation, home, room, _strategy.RoomSide, Tried(room), grow) is not PlayerAction.BuildRoom build) return false;
        if (PlayerActionRoutines.SelectRoom(observation, room) is PlayerAction press) actions.Add(press);
        actions.Add(build);
        _pendingRooms[room] = new PendingRoom(build.From, observation.Stamp.Tick, OwnedTiles(observation, room));
        return true;
    }

    /// <summary>A Lair or Hatchery with fewer tiles than the own non-workers need, or a Treasury once
    /// gold nears what the heart and treasuries hold (only when the options know the capacities).</summary>
    private bool Outgrown(PlayerObservation observation, RoomTypeId room)
    {
        int tiles = OwnedTiles(observation, room);
        if (tiles == 0) return false;
        switch (room)
        {
            case RoomTypeId.Lair:
                return tiles < (long)NonWorkers(observation) * _strategy.LairTilesPerCreature;
            case RoomTypeId.Hatchery:
                return (long)tiles * _strategy.CreaturesPerHatcheryTile < NonWorkers(observation);
            case RoomTypeId.Treasury:
                if (_options.GoldPerTreasuryTile is not int perTile) return false;
                long capacity = (long)tiles * perTile + (Owns(observation, RoomTypeId.DungeonHeart) ? _options.HeartGold ?? 0 : 0);
                return (long)observation.Gold.Value * 100 >= capacity * _strategy.TreasuryFullPercent;
            default:
                return false;
        }
    }

    private int NonWorkers(PlayerObservation observation)
    {
        int count = 0;
        foreach (PlayerCreatureSummary creature in observation.Creatures) if (!_options.IsWorker(creature.Type)) count++;
        return count;
    }

    /// <summary>With the hand free, no enemy in sight, gold to spare and the interval passed: lifts an
    /// idle own non-worker (one standing on floor that is no room, nearest home first) toward the first used
    /// room with fewer creatures on it than its tiles allow; the drop follows next decision, on the room's
    /// tile nearest home that has not refused one.</summary>
    private bool Work(PlayerObservation observation, TilePos home, List<PlayerAction> actions)
    {
        if (_strategy.UseRooms.Count == 0 || observation.HandCount > 0 || !Fits(actions, 1)
            || (observation.Offers.Actions & PlayerActionKinds.PickUp) == 0
            || observation.Gold.Value < _strategy.UseRoomMinGold
            || _lastWorkTick is GameTick last && observation.Stamp.Tick.Value - last.Value < _strategy.UseRoomEveryTicks
            || PlayerActionRoutines.NearestEnemy(observation, home) is not null) return false;
        PlayerTileGrid grid = observation.Tiles;
        PlayerId self = observation.Stamp.Player;
        foreach (RoomTypeId room in _strategy.UseRooms)
        {
            int tiles = OwnedTiles(observation, room);
            if (tiles == 0) continue;
            int working = 0;
            PlayerVisibleThing? idle = null;
            foreach (PlayerVisibleThing thing in observation.Things)
            {
                if (thing.Kind != PlayerThingKind.Creature || thing.Owner != self || thing.CreatureType is not CreatureTypeId type
                    || _options.IsWorker(type) || !grid.Contains(thing.Tile)) continue;
                PlayerTile on = grid[thing.Tile];
                if (on.Room == room) working++;
                else if (on.Room == default && (thing.Actions & PlayerThingActions.PickUp) != 0
                    && (idle is null || PlayerActionRoutines.DistanceSquared(thing.Tile, home) < PlayerActionRoutines.DistanceSquared(idle.Value.Tile, home)))
                    idle = thing;
            }
            if ((long)working * _strategy.TilesPerWorkingCreature >= tiles) continue;
            if (idle is not PlayerVisibleThing lifted) return false;
            if (PlayerActionRoutines.NearestRoomTile(observation, room, home, _refusedWork) is not TilePos target)
            {
                // Every tile has refused a drop once; refusals may pass, so they are forgotten and tried again later.
                if (PlayerActionRoutines.NearestRoomTile(observation, room, home) is not null) _refusedWork.Clear();
                continue;
            }
            actions.Add(new PlayerAction.PickUp(lifted.Handle));
            _carryTo = target;
            _lastWorkTick = observation.Stamp.Tick;
            return true;
        }
        return false;
    }

    /// <summary>Dig marks up to the outstanding bound, skipping abandoned tiles.</summary>
    private bool Dig(PlayerObservation observation, TilePos home, List<PlayerAction> actions)
    {
        if (_strategy.Dig == DigMode.Off || (observation.Offers.Actions & PlayerActionKinds.TagArea) == 0) return false;
        int room = _strategy.MaxOutstandingTags - Outstanding(observation);
        int added = 0;
        TilePos from = _strategy.Dig == DigMode.Toward && _strategy.DigTarget is TilePos target ? target : home;
        foreach (TilePos tile in Frontier(observation, from, preferGold: _strategy.Dig == DigMode.GoldFirst))
        {
            if (room-- <= 0 || !Fits(actions, 1)) break;
            actions.Add(new PlayerAction.TagArea(tile, tile));
            added++;
        }
        return added > 0;
    }

    /// <summary>Without a gain for the stall window while work is pending: give up pending squares,
    /// untag and skip the standing marks and the frontier's head, and count a recovery; a gain resets
    /// all of it. Past the recovery attempts, a limitation naming what the player saw.</summary>
    private PlayerDecision? Stalled(PlayerObservation observation, TilePos home, List<PlayerAction> actions)
    {
        Gains now = Measure(observation);
        if (now.Beats(_best))
        {
            _best = now.Max(_best);
            _gainTick = observation.Stamp.Tick;
            _recoveries = 0;
            _abandoned.Clear();
            _failedDrops.Clear();
            return null;
        }
        if (observation.Stamp.Tick.Value - _gainTick.Value < _options.StallWindowTicks) return null;
        // With digging off the frontier is no pending work: an idle, non-digging player is not stalled.
        List<TilePos> frontier = _strategy.Dig == DigMode.Off ? [] : Frontier(observation, home, preferGold: false);
        int marks = Outstanding(observation);
        if (_pendingRooms.Count == 0 && marks == 0 && frontier.Count == 0) return null;

        if (_recoveries >= _options.RecoveryAttempts)
            return PlayerDecision.Limited($"no gain for {_options.StallWindowTicks} ticks after {_recoveries} recoveries: " +
                $"pending rooms {_pendingRooms.Count}, marks {marks}, frontier {frontier.Count}, hand {observation.HandCount}, gold {observation.Gold.Value}");
        _recoveries++;
        foreach (KeyValuePair<RoomTypeId, PendingRoom> pending in _pendingRooms) Tried(pending.Key).Add(pending.Value.Corner);
        _pendingRooms.Clear();
        PlayerTileGrid grid = observation.Tiles;
        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                var at = new TilePos(x, y);
                if ((grid[at].Flags & PlayerTileFlags.Tagged) == 0) continue;
                _abandoned.Add(at);
                if (Fits(actions, 1)) actions.Add(new PlayerAction.TagArea(at, at, Untag: true));
            }
        for (int i = 0; i < frontier.Count && i < _strategy.MaxOutstandingTags; i++) _abandoned.Add(frontier[i]);
        _gainTick = observation.Stamp.Tick;
        return null;
    }

    /// <summary>Takes back the player's standing marks, within the action bound and skipping any this
    /// decision already untags; done only when the observation shows none.</summary>
    private bool Untag(PlayerObservation observation, List<PlayerAction> actions)
    {
        if ((observation.Offers.Actions & PlayerActionKinds.TagArea) == 0) return false;
        PlayerTileGrid grid = observation.Tiles;
        bool standing = false;
        int added = 0;
        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                var at = new TilePos(x, y);
                if ((grid[at].Flags & PlayerTileFlags.Tagged) == 0) continue;
                standing = true;
                var untag = new PlayerAction.TagArea(at, at, Untag: true);
                if (!Fits(actions, 1) || actions.Contains(untag)) continue;
                actions.Add(untag);
                added++;
            }
        if (!standing) _untagAll = false;
        return added > 0;
    }

    /// <summary>The frontier as the strategy's <see cref="BreachMode"/> bounds it: held back, nothing beside
    /// others' open ground or within the clearance of a tile an enemy was seen on; under
    /// <see cref="BreachMode.WhenDeveloped"/>, the whole frontier once no bounded tile is left, marked or not.</summary>
    private List<TilePos> Frontier(PlayerObservation observation, TilePos from, bool preferGold)
    {
        if (_strategy.Breach == BreachMode.Always) return PlayerActionRoutines.Frontier(observation, from, preferGold, _abandoned);
        if (_dangerRadius != _strategy.EnemyClearanceTiles)
        {
            _danger.Clear();
            _dangerRadius = _strategy.EnemyClearanceTiles;
            foreach (TilePos seen in _sightings) AddClearance(seen, _dangerRadius);
        }
        List<TilePos> safe = PlayerActionRoutines.Frontier(observation, from, preferGold, _abandoned, breach: false, avoid: _danger, tagged: true);
        // Develop first: the breach waits until no safe tile is left, marked or not.
        if (safe.Count == 0 && _strategy.Breach == BreachMode.WhenDeveloped) return PlayerActionRoutines.Frontier(observation, from, preferGold, _abandoned);
        safe.RemoveAll(t => (observation.Tiles[t].Flags & PlayerTileFlags.Tagged) != 0);
        return safe;
    }

    private void AddClearance(TilePos seen, int r)
    {
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
                if (dx * dx + dy * dy <= r * r) _danger.Add(new TilePos(seen.X + dx, seen.Y + dy));
    }

    private static Gains Measure(PlayerObservation observation)
    {
        PlayerTileGrid grid = observation.Tiles;
        PlayerId self = observation.Stamp.Player;
        int floor = 0;
        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
                if (grid[new TilePos(x, y)] is { IsKnown: true } tile && tile.Owner == self) floor++;
        int rooms = 0;
        foreach (PlayerRoomSummary room in observation.Rooms) rooms += room.TileCount;
        return new Gains(floor, rooms, observation.Creatures.Count, observation.Gold.Value);
    }

    private static int Outstanding(PlayerObservation observation)
    {
        PlayerTileGrid grid = observation.Tiles;
        int marks = 0;
        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
                if ((grid[new TilePos(x, y)].Flags & PlayerTileFlags.Tagged) != 0) marks++;
        return marks;
    }

    private static bool Owns(PlayerObservation observation, RoomTypeId room) => OwnedTiles(observation, room) > 0;

    private static int OwnedTiles(PlayerObservation observation, RoomTypeId room)
    {
        int tiles = 0;
        foreach (PlayerRoomSummary owned in observation.Rooms) if (owned.Type == room) tiles += owned.TileCount;
        return tiles;
    }

    private HashSet<TilePos> Tried(RoomTypeId room)
    {
        if (!_triedSquares.TryGetValue(room, out HashSet<TilePos>? tried)) _triedSquares[room] = tried = [];
        return tried;
    }

    private bool Fits(List<PlayerAction> actions, int more) => actions.Count + more <= _options.MaxActionsPerDecision;
}
