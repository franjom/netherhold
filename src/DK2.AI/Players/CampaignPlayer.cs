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
    /// <summary>Whether the carry under way (<see cref="_carryTo"/>, then <see cref="_workDrop"/>) is the Pit routine's
    /// or the scouting routine's (refusals kept apart), holds a worker (dropped near home, never toward an enemy),
    /// or is a scout going out.</summary>
    private bool _carryForPit, _workDropForPit, _carryWorker, _carryScout;
    /// <summary>Whether the Pit's floor held two fighters or more when the Pit routine last looked (Task 4).</summary>
    private bool _pitPaired;
    /// <summary>The scouting Imp's drop tile and when it landed (Task 4); null while none is out.</summary>
    private (TilePos Tile, GameTick Since)? _scout;
    private readonly HashSet<TilePos> _scouted = [];
    private GameTick? _lastScoutTick;
    /// <summary>Where a threat was last seen, and when: scouting keeps away from it for <see cref="ThreatMemoryTicks"/>.</summary>
    private readonly Dictionary<TilePos, GameTick> _lastThreatAt = [];
    private readonly HashSet<TilePos> _refusedWork = [];
    private GameTick? _lastWorkTick, _lastPitTick;
    /// <summary>Pit floor tiles a fighter's drop was refused on; kept apart from <see cref="_refusedWork"/> (Task 4).</summary>
    private readonly HashSet<TilePos> _refusedPit = [];
    /// <summary>Collapsed creatures' tiles an Imp was last sent to, and when.</summary>
    private readonly Dictionary<TilePos, GameTick> _rescues = [];
    private readonly HashSet<TilePos> _sightings = [], _danger = [];
    private int _dangerRadius = -1;

    /// <summary>A build of a <paramref name="Side"/> square sent at <paramref name="Since"/> while the player
    /// owned <paramref name="Tiles"/> of its type.</summary>
    private readonly record struct PendingRoom(TilePos Corner, int Side, GameTick Since, int Tiles, bool Grew = false);

    /// <summary>A square a build left short of whole, to be finished (Task 3a), and the tries left.</summary>
    private readonly record struct Repair(TilePos Corner, int Side, int Tries);

    private readonly Dictionary<RoomTypeId, Repair> _repairs = [];

    /// <summary>A square being dug out for a room (Task 3a), marked at <paramref name="Since"/>.</summary>
    private readonly record struct Site(RoomTypeId Room, TilePos Corner, int Side, GameTick Since);

    private Site? _site;
    private readonly HashSet<TilePos> _failedSites = [];
    private readonly HashSet<(RoomTypeId Room, int Side)> _settled = [];

    /// <summary>A site still not free to build on after this long is given up for the next smaller side.</summary>
    private const int SiteTicks = 2400;

    private enum SiteState { None, Marked, Waiting }
    private const int RepairTries = 3;

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
        _thoughts.Clear();
        Cover(observation);
        Believe(observation);
        if (Stalled(observation, home, actions) is PlayerDecision limited) return limited;
        if (actions.Count > 0) { why.Add("recover"); _thoughts.Add(new PlayerThought("recover", true, "stalled: untagged and skipped the marks")); }
        if (_untagAll) Think("untag", why, actions, () => Untag(observation, actions));
        bool hand = Think("hand", why, actions, () => Hand(observation, home, actions));
        Think("room", why, actions, () => Room(observation, home, actions));
        Think("trap", why, actions, () => Trap(observation, actions));
        Think("imp", why, actions, () => CreateImp(observation, home, actions));
        Think("bridge", why, actions, () => Bridge(observation, home, actions));
        bool capture = !hand && Think("capture", why, actions, () => Capture(observation, actions));
        bool bank = !hand && !capture && Think("bank", why, actions, () => Bank(observation, home, actions));
        bool assault = !hand && !capture && !bank && Think("assault", why, actions, () => Assault(observation, home, actions));
        if (!hand && !capture && !bank && !assault && !Think("work", why, actions, () => Work(observation, home, actions))
            && !Think("pit", why, actions, () => Pit(observation, home, actions)))
            Think("scout", why, actions, () => Scout(observation, home, actions));
        if (_strategy.UseSpecials)
            Think("special", why, actions, () =>
            {
                if (!Fits(actions, 1) || PlayerActionRoutines.Special(observation, _options) is not PlayerAction special) return false;
                actions.Add(special);
                Note($"activate {special}");
                return true;
            });
        Think("dig", why, actions, () => Dig(observation, home, actions));
        return actions.Count == 0
            ? new PlayerDecision([], _strategy.AdvanceTicks, "wait: nothing offered to do") { Thoughts = _thoughts, Beliefs = _beliefs }
            : Send(actions, string.Join(", ", why));
    }

    private PlayerDecision Send(List<PlayerAction> actions, string reason)
    {
        foreach (PlayerAction action in actions) _sent[new PlayerActionId(_nextActionId++)] = action;
        return new PlayerDecision(actions, _strategy.AdvanceTicks, reason) { Thoughts = _thoughts, Beliefs = _beliefs };
    }

    /// <summary>Runs one routine and records its verdict: whether it added actions, and the note it left (diagnostics:
    /// nothing reads them back). Returns the routine's own answer.</summary>
    private bool Think(string routine, List<string> why, List<PlayerAction> actions, Func<bool> run)
    {
        _note = null;
        int before = actions.Count;
        bool acted = run();
        if (acted) why.Add(routine);
        _thoughts.Add(new PlayerThought(routine, actions.Count > before, _note ?? (acted ? "acted" : "nothing to do")));
        return acted;
    }

    /// <summary>Leaves the running routine's note for the decision's thoughts; the last note of a routine wins.</summary>
    private void Note(string note) => _note = note;

    private string? _note;
    private readonly List<PlayerThought> _thoughts = [];
    private readonly List<PlayerBelief> _beliefs = [];

    /// <summary>Tiles in reach of an enemy trap in view (A2), and the tiles no drop goes to: those plus the ones a drop
    /// already failed on.</summary>
    private readonly HashSet<TilePos> _covered = [], _noDrop = [], _digSkip = [];

    /// <summary>Rebuilds <see cref="_covered"/> from the enemy traps in view and <see cref="_noDrop"/>. The finder measures
    /// real positions (traps.md section 5), so a tile counts when any of it may lie within the reach: its centre within
    /// reach plus half a tile. Line of sight, which also limits the finder, is not modelled: the cover is the larger.</summary>
    private void Cover(PlayerObservation observation)
    {
        _covered.Clear();
        foreach (PlayerVisibleTrap trap in observation.EnemyTraps)
        {
            if (_strategy.AvoidTraps == 0) break;
            int r = trap.ReachTiles;
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    if (InReach(dx, dy, r)) _covered.Add(new TilePos(trap.Tile.X + dx, trap.Tile.Y + dy));
        }
        RefreshNoDrop();
    }

    /// <summary>Whether a tile <paramref name="dx"/>, <paramref name="dy"/> from a trap may lie within its reach
    /// <paramref name="r"/>: centre distance at most r plus half a tile (4·(dx²+dy²) ≤ (2r+1)²).</summary>
    private static bool InReach(int dx, int dy, int r) => 4L * (dx * dx + dy * dy) <= (2L * r + 1) * (2L * r + 1);

    private void RefreshNoDrop()
    {
        _noDrop.Clear();
        _noDrop.UnionWith(_failedDrops);
        _noDrop.UnionWith(_covered);
    }

    /// <summary>The numbers this decision rests on, as the observation shows them (diagnostics).</summary>
    private void Believe(PlayerObservation observation)
    {
        _beliefs.Clear();
        int imps = 0, fighters = 0, hurt = 0, unhappy = 0, angry = 0, held = 0;
        foreach (PlayerCreatureSummary creature in observation.Creatures)
        {
            if (creature.InHand) held++;
            if (_options.IsWorker(creature.Type)) imps++;
            else fighters++;
            if (creature.FlowerKnown && creature.Petals <= 2) hurt++;
            if (creature.Mood == PlayerCreatureMood.Unhappy) unhappy++;
            else if (creature.Mood == PlayerCreatureMood.Angry) angry++;
        }
        int threats = 0, enemies = 0;
        foreach (PlayerVisibleThing thing in observation.Things)
            if (thing.Kind == PlayerThingKind.Creature && thing.Owner != observation.Stamp.Player)
            {
                enemies++;
                if (PlayerActionRoutines.IsThreat(observation, thing)) threats++;
            }
        Add("gold", observation.Gold.Value);
        Add("mana", observation.Mana.Value);
        Add("mana_net_per_s", (long)observation.ManaIncome.Value - observation.ManaUpkeep.Value);
        Add("imps", imps);
        Add("fighters", fighters);
        Add("hurt", hurt);
        Add("unhappy", unhappy);
        Add("angry", angry);
        Add("in_hand", held);
        Add("enemies_seen", enemies);
        Add("threats_seen", threats);
        Add("marks", Outstanding(observation));
        Add("own_traps", PlayerTrapRoutines.OwnTraps(observation.Tiles));
        Add("enemy_traps_seen", observation.EnemyTraps.Count);
        Add("tiles_in_trap_reach", _covered.Count);
        Add("lair_tiles", OwnedTiles(observation, RoomTypeId.Lair));
        Add("hatchery_tiles", OwnedTiles(observation, RoomTypeId.Hatchery));
        Add("treasury_tiles", OwnedTiles(observation, RoomTypeId.Treasury));
        Add("pending_rooms", _pendingRooms.Count);

        void Add(string name, long value) => _beliefs.Add(new PlayerBelief(name, value));
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
            if (action is PlayerAction.CreateImpAt imp && receipt.Status is PlayerReceiptStatus.Refused) _refusedImpTiles.Add(imp.Tile);
            // Only a refusal about the place bans the tile; a stale view or a spent budget says nothing about it.
            if (action is PlayerAction.PlaceTrap trap && receipt.Status is PlayerReceiptStatus.Refused
                && receipt.Reason is PlayerRefusalReason.NotOffered or PlayerRefusalReason.OutOfBounds) _refusedTrapTiles.Add(trap.Tile);
            if (receipt.Status != PlayerReceiptStatus.Queued) _sent.Remove(receipt.Id);
        }
        var done = new List<RoomTypeId>();
        var building = new List<KeyValuePair<RoomTypeId, PendingRoom>>();
        foreach ((RoomTypeId room, PendingRoom pending) in _pendingRooms)
        {
            int owned = OwnedTiles(observation, room);
            bool stale = observation.Stamp.Tick.Value - pending.Since.Value >= _options.PendingRoomTicks;
            if (_options.Knowledge.Room(room) is not { CapsCreatures: true })
            {
                // An area room: done when the type owns more tiles than when it was sent (this square or another).
                if (owned > pending.Tiles) done.Add(room);
                else if (stale) { Tried(room).Add(pending.Corner); done.Add(room); }
            }
            // A room that draws creatures works by its furniture, which needs the whole square (Task 3a):
            // pending while DK2's wave lays it; one that stops short is finished later.
            else if (PlayerActionRoutines.RoomCovers(observation, pending.Corner, pending.Side, room)) { done.Add(room); _repairs.Remove(room); }
            else if (owned > pending.Tiles) building.Add(new(room, pending with { Tiles = owned, Since = observation.Stamp.Tick, Grew = true }));
            else if (stale)
            {
                done.Add(room);
                if (!pending.Grew) Tried(room).Add(pending.Corner);
                else _repairs[room] = new Repair(pending.Corner, pending.Side,
                    _repairs.TryGetValue(room, out Repair before) && before.Corner == pending.Corner ? before.Tries : RepairTries);
            }
        }
        foreach (RoomTypeId room in done) _pendingRooms.Remove(room);
        foreach ((RoomTypeId room, PendingRoom pending) in building) _pendingRooms[room] = pending;
        foreach (PlayerVisibleThing thing in observation.Things)
        {
            if (!PlayerActionRoutines.IsThreat(observation, thing)) continue;
            _lastThreatAt[thing.Tile] = observation.Stamp.Tick;
            if (_sightings.Add(thing.Tile) && _dangerRadius >= 0) AddClearance(thing.Tile, _dangerRadius);
        }
    }

    /// <summary>A held item goes down (a fighter in its formation place against the attack, else near home), skipping
    /// tiles a drop already failed on; with an empty hand and an attack, a defender is lifted.</summary>
    private bool Hand(PlayerObservation observation, TilePos home, List<PlayerAction> actions)
    {
        if (_lastDrop is (TilePos tile, int hand) && observation.HandCount >= hand)
        {
            _failedDrops.Add(tile);
            _noDrop.Add(tile);
            if (_workDrop == tile) (_workDropForPit ? _refusedPit : _refusedWork).Add(tile);
        }
        _lastDrop = null;
        _workDrop = null;
        if (observation.HandCount == 0)
        {
            _failedDrops.Clear();
            RefreshNoDrop();
            // A lift that never landed leaves nothing held: its carry plan must not steer the next one.
            _carryTo = null;
            _carryForPit = _carryWorker = _carryScout = _carryRetreat = false;
            _carryStyle = null;
            _assaultDrop = null;
            _captureDrop = null;
            _rallyDrop = null;
            _rallyDropping = false;
        }
        if (!Fits(actions, 1)) return false;
        // Engage answers an attack on any of the dungeon (an enemy within EnemyNearTiles of own ground), the one nearest
        // the Heart first; HoldHome only one near home; Off none. With none, anything held goes down near home.
        PlayerVisibleThing? enemy = _strategy.Fight == FightMode.Off ? null
            : PlayerActionRoutines.NearestAttacker(observation, home, _strategy.EnemyNearTiles, _covered);
        if (_strategy.Fight == FightMode.HoldHome && enemy is PlayerVisibleThing far
            && PlayerActionRoutines.DistanceSquared(far.Tile, home) > (long)_strategy.HomeGuardTiles * _strategy.HomeGuardTiles) enemy = null;
        if (Rally(observation, home, enemy, actions) is bool rallied) return rallied;
        if (observation.HandCount > 0 && _captureDrop is TilePos body)
        {
            // An Imp lifted to capture goes down on the body (R16): a dropped Imp looks at its landing ring first, where a
            // capture outranks reinforcing, gold and objects (0x0046D810 in the drop scan's chain, imp-task-priority.md 4.1).
            _captureDrop = null;
            if ((observation.Offers.Actions & PlayerActionKinds.DropAt) != 0 && !_failedDrops.Contains(body))
            {
                actions.Add(new PlayerAction.DropAt(body));
                Note($"drop an Imp on the body at {body}");
                _lastDrop = (body, observation.HandCount);
                return true;
            }
        }
        if (observation.HandCount > 0 && _assaultDrop is TilePos assaultAt)
        {
            // An assault party goes down together beside its trap, inside its reach (A2): one drop per item held.
            if ((observation.Offers.Actions & PlayerActionKinds.DropAt) == 0 || _failedDrops.Contains(assaultAt)) _assaultDrop = null;
            else
            {
                int released = 0;
                while (released < observation.HandCount && Fits(actions, 1)) { actions.Add(new PlayerAction.DropAt(assaultAt)); released++; }
                Note($"drop an assault party of {released} at {assaultAt}");
                _lastDrop = (assaultAt, observation.HandCount);
                _lastDropTick = observation.Stamp.Tick;
                if (released >= observation.HandCount) _assaultDrop = null;
                return true;
            }
        }
        if (observation.HandCount > 0)
        {
            bool worker = _carryWorker;
            _carryWorker = false;
            bool scout = _carryScout;
            _carryScout = false;
            // A creature lifted to work goes to its room unless an enemy near home, or near that room, needs it, or a
            // drop there failed. An enemy merely in sight further off does not turn it back (Q843).
            TilePos? work = _carryTo is TilePos room && !(_carryForPit ? _refusedPit : _refusedWork).Contains(room)
                && !PlayerActionRoutines.ThreatNear(observation, home, _strategy.EnemyNearTiles)
                && !PlayerActionRoutines.ThreatNear(observation, room, _strategy.EnemyNearTiles) ? room : null;
            TilePos? carriedTo = _carryTo;
            _carryTo = null;
            _workDrop = work;
            _workDropForPit = _carryForPit;
            _carryForPit = false;
            FightStyle style = _carryStyle ?? FightStyle.Blocker;
            _carryStyle = null;
            bool retreat = _carryRetreat;
            _carryRetreat = false;
            // A hurt defender taken out of a fight goes to its Hatchery (or home) whatever the danger there.
            if (retreat) work = carriedTo ?? PlayerActionRoutines.NearestOwnFloor(observation, home, _noDrop);
            // A held worker goes down near home, never toward an enemy (an Imp is no fighter); a fighter takes its place
            // in the formation against the attack (defence plan D3).
            TilePos? fight = !worker && !retreat && enemy is PlayerVisibleThing foe ? FormationDrop(observation, foe.Tile, home, style) : null;
            if ((observation.Offers.Actions & PlayerActionKinds.DropAt) == 0
                || (work ?? fight ?? PlayerActionRoutines.NearestOwnFloor(observation, home, _noDrop)) is not TilePos drop)
            {
                Note($"holding {observation.HandCount}: no drop offered or no own floor free");
                return false;
            }
            if (scout && work is TilePos sent) { _scout = (sent, observation.Stamp.Tick); _scouted.Add(sent); }
            Note(retreat ? $"drop a hurt defender at {drop}" : work is not null ? $"drop at {drop} to work there"
                : fight is not null && enemy is PlayerVisibleThing at ? $"drop a {style} at {drop} against the enemy at {at.Tile}" : $"drop at {drop} near home");
            actions.Add(new PlayerAction.DropAt(drop));
            _lastDrop = (drop, observation.HandCount);
            _lastDropTick = observation.Stamp.Tick;
            return true;
        }
        if (enemy is not PlayerVisibleThing target) { Note("no attack to answer"); return false; }
        if (_lastDropTick is GameTick dropped && observation.Stamp.Tick.Value - dropped.Value < _options.DropCooldownTicks)
        {
            Note($"attack at {target.Tile}; drop cooling down");
            return false;
        }
        // A defender down to two petals at the fight is healed, else taken to a Hatchery (defence plan D3).
        int attackers = 0, defenders = 0;
        PlayerVisibleThing? hurt = null;
        var flowers = new Dictionary<PlayerThingHandle, PlayerCreatureSummary>();
        foreach (PlayerCreatureSummary creature in observation.Creatures)
            if (creature.Handle is PlayerThingHandle seen && creature.FlowerKnown) flowers[seen] = creature;
        long reach = (long)(_options.EngagedTiles + 1) * (_options.EngagedTiles + 1);
        foreach (PlayerVisibleThing thing in observation.Things)
        {
            if (PlayerActionRoutines.DistanceSquared(thing.Tile, target.Tile) > reach) continue;
            if (PlayerActionRoutines.IsThreat(observation, thing)) attackers++;
            else if (thing.Kind == PlayerThingKind.Creature && thing.Owner == observation.Stamp.Player && thing.CreatureType is CreatureTypeId type
                && !_options.IsWorker(type) && _options.StyleOf(type) != FightStyle.NonFighter)
            {
                // Only a fighter able to fight counts toward outnumbering the attackers.
                bool known = flowers.TryGetValue(thing.Handle, out PlayerCreatureSummary flower);
                if (known && flower.Petals == 2) hurt ??= thing;
                else if (!known || flower.Petals > 2) defenders++;
            }
        }
        if (hurt is PlayerVisibleThing low && HealOffered(observation))
        {
            actions.Add(new PlayerAction.CastSpell(HealSpell, Target: low.Handle));
            Note($"heal a defender at {low.Tile} ({defenders} defenders, {attackers} attackers)");
            return true;
        }
        if (hurt is PlayerVisibleThing tired && Owns(observation, RoomTypeId.Hatchery) && Lift(observation, tired, RoomTypeId.Hatchery, home, actions))
        {
            _carryRetreat = true;
            Note($"lift a hurt defender at {tired.Tile} out of the fight");
            return true;
        }
        // Enough already: the defenders at the fight outnumber the attackers.
        if (defenders > attackers) { Note($"attack at {target.Tile}: {defenders} defenders outnumber {attackers} attackers"); return false; }
        if (_holdDefenders) { Note($"siege at {target.Tile}: gathering a party, none goes alone"); return false; }
        if (PlayerActionRoutines.Defender(observation, _options, target.Tile, _options.EngagedTiles, home,
                style => FormationDrop(observation, target.Tile, home, style)) is not PlayerVisibleThing fighter)
        {
            Note($"attack at {target.Tile}: {defenders} defenders, {attackers} attackers, no fighter free to lift");
            return false;
        }
        Note($"lift a {fighter.CreatureType} at {fighter.Tile} against the attack at {target.Tile} ({defenders} defenders, {attackers} attackers)");
        actions.Add(new PlayerAction.PickUp(fighter.Handle));
        _carryStyle = fighter.CreatureType is CreatureTypeId kind ? _options.StyleOf(kind) : FightStyle.Blocker;
        return true;
    }

    /// <summary>The own floor a lifted defender of <paramref name="style"/> goes down on against an attacker at
    /// <paramref name="enemy"/>: the floor nearest its formation point. A Flanker or Blitzer whose floor is not truly
    /// behind the enemy (within two tiles of the point, and further from home than the enemy: the hand drops only on own
    /// ground) goes beside the Blockers instead.</summary>
    private TilePos? FormationDrop(PlayerObservation observation, TilePos enemy, TilePos home, FightStyle style)
    {
        TilePos ideal = PlayerActionRoutines.FormationPoint(enemy, home, style);
        TilePos? at = PlayerActionRoutines.NearestOwnFloor(observation, ideal, _noDrop);
        if (style is FightStyle.Flanker or FightStyle.Blitzer && (at is not TilePos behind || Chebyshev(behind, ideal) > 2
            || PlayerActionRoutines.DistanceSquared(behind, home) <= PlayerActionRoutines.DistanceSquared(enemy, home)))
            at = PlayerActionRoutines.NearestOwnFloor(observation, PlayerActionRoutines.FormationPoint(enemy, home, FightStyle.Blocker), _noDrop);
        return at;
    }

    /// <summary>The fighting style of the defender being carried to an attack, for its formation drop.</summary>
    private FightStyle? _carryStyle;

    /// <summary>Whether the creature carried is a hurt defender taken out of a fight.</summary>
    private bool _carryRetreat;

    /// <summary>Unless a build is pending, the first of: a missing listed room, a listed room its
    /// creatures or gold have outgrown (grown beside itself), an offered extra room not owned (rebuilt if
    /// lost); growth and extras wait while a missing listed room is offered but unaffordable;
    /// pressed then built when the panel offers it and the player can pay. One not yet offered or
    /// affordable waits and the next may go.</summary>
    private bool Room(PlayerObservation observation, TilePos home, List<PlayerAction> actions)
    {
        AgeSite(observation);
        CountArrivals(observation);
        if (_pendingRooms.Count > 0) { Note("waiting for " + string.Join(", ", _pendingRooms.Keys) + " to be laid"); return false; }
        if (!Fits(actions, 2)) { Note("no room left in this decision's actions"); return false; }
        if (_repairs.Count > 0 && Finish(observation, actions)) { NoteMore("finish a short square"); return true; }
        bool waiting = false;
        var saving = new List<RoomTypeId>();
        foreach (RoomTypeId room in _strategy.Rooms)
        {
            if (Owns(observation, room)) continue;
            if (Build(observation, home, room, grow: false, actions)) return true;
            if (Unaffordable(observation, room)) { waiting = true; saving.Add(room); }
        }
        // A listed room the panel offers but the player cannot yet pay for keeps the gold for itself.
        if (waiting) { Note($"saving gold for {string.Join(", ", saving)} (gold {observation.Gold.Value})"); return false; }
        // Short of the gold budget (E2): payday first, so nothing grows and no extra room is bought.
        if (Short(observation)) { Note($"keeping {GoldNeed(observation)} gold for payday (gold {observation.Gold.Value})"); return false; }
        long keep = _strategy.GoldBudget == 1 ? GoldNeed(observation) : 0;
        foreach (RoomTypeId room in _strategy.Rooms)
        {
            _hungerAsked = false;
            if (Outgrown(observation, room) && (Build(observation, home, room, grow: true, actions, keep)
                // A room that cannot grow beside itself gets a second one (E3, owner R12: "expand it, create a second one").
                || _strategy.SecondRooms == 1 && room != RoomTypeId.Treasury && Build(observation, home, room, grow: false, actions, keep)))
            {
                if (_hungerAsked) _hungerGrowth = observation.Stamp.Tick;
                return true;
            }
        }
        foreach (RoomTypeId room in _strategy.ExtraRooms)
            if (!_strategy.Rooms.Contains(room) && !Owns(observation, room) && Build(observation, home, room, grow: false, actions, keep)) return true;
        if (Recruit(observation, home, actions, keep)) return true;
        _note ??= "every listed room owned and none outgrown";
        return false;
    }

    /// <summary>Ticks without a new non-worker, and between two recruiting builds, before recruiting builds (5 game minutes).</summary>
    private const int RecruitWaitTicks = 1200;

    /// <summary>A room recruiting no longer grows once it holds a 7x7's tiles (the largest side the knowledge measures); the
    /// last growth may pass it by one square.</summary>
    private const int RecruitMaxTiles = 49;

    /// <summary>Recruiting adds wages: it builds only while the gold pays this many paydays at the current bill.</summary>
    private const int RecruitPaydays = 6;

    /// <summary>Ticks after a recruiting pass that built nothing before the next looks again.</summary>
    private const int RecruitRetryTicks = 200;

    private int _seenNonWorkers;
    private int? _placedNonWorkers;
    private GameTick _lastArrival;
    private GameTick? _lastRecruit;
    private GameTick? _recruitTried;

    /// <summary>Notes when the non-workers last grew in number (an arrival through the Portal, a conversion, a Skeleton),
    /// and the creatures the level placed, which no portal brought: the number at the first look, fewer whenever the
    /// non-workers fall below it (a placed creature can no longer be there).</summary>
    private void CountArrivals(PlayerObservation observation)
    {
        int nonWorkers = NonWorkers(observation);
        _placedNonWorkers = Math.Min(_placedNonWorkers ?? nonWorkers, nonWorkers);
        if (nonWorkers > _seenNonWorkers) _lastArrival = observation.Stamp.Tick;
        _seenNonWorkers = nonWorkers;
    }

    /// <summary>Recruiting (<see cref="PlayerStrategy.RecruitGold"/>): with a Portal owned, no new non-worker for
    /// <see cref="RecruitWaitTicks"/> and the gold over the payday budget, the offered room whose attraction caps creatures
    /// (the knowledge's <see cref="RoomFacts.CapsCreatures"/>) with the fewest own tiles is built, or grown beside itself
    /// (a second one when it cannot); among equals the cheaper per tile first. The player cannot see which creatures the
    /// Portal holds back, only that none come; so every such room is grown in turn. Creatures cost wages and the gold may
    /// have no income behind it (Level 5, 2026-11-17: an army of 15 drained 94k to nothing), so it builds only while the
    /// gold pays <see cref="RecruitPaydays"/> paydays at the current bill. A conversion or a Skeleton counts as an arrival
    /// too (the net count of non-workers), which only delays it.</summary>
    private bool Recruit(PlayerObservation observation, TilePos home, List<PlayerAction> actions, long keep)
    {
        if (_strategy.RecruitGold <= 0 || Summary(observation, RoomTypeId.Portal) is not PlayerRoomSummary portal) return false;
        // The portals' cap: at it nobody comes whatever the rooms draw (owner: 15, and 5 for each more portal). Only
        // Portal arrivals count toward it, and no screen shows how many are held (owner, 2026-11-17; schema 9), so the
        // player estimates them: its non-workers less those the level placed. A conversion or a Skeleton counts as an
        // arrival here (recruiting stops early by those), and a placed creature's death as an arrival's until the
        // non-workers fall below the placed (it may build a room the full Portal cannot use).
        int held = NonWorkers(observation) - (_placedNonWorkers ?? 0);
        if (portal.Capacity > 0 && held >= portal.Capacity) { Note($"the portals hold their {portal.Capacity} creatures (about {held} came)"); return false; }
        long tick = observation.Stamp.Tick.Value;
        long gold = observation.Gold.Value;
        if (gold - keep < _strategy.RecruitGold || gold < (long)observation.WageBill.Value * RecruitPaydays
            || tick - _lastArrival.Value < RecruitWaitTicks
            || _lastRecruit is GameTick last && tick - last.Value < RecruitWaitTicks
            || _recruitTried is GameTick tried && tick - tried.Value < RecruitRetryTicks) return false;
        _recruitTried = observation.Stamp.Tick;
        var candidates = new List<(RoomTypeId Room, int Tiles, int Cost)>();
        foreach (PlayerRoomOffer offer in observation.Offers.Rooms)
        {
            if (_options.Knowledge.Room(offer.Room) is not { CapsCreatures: true }) continue;
            int tiles = OwnedTiles(observation, offer.Room);
            if (tiles < RecruitMaxTiles) candidates.Add((offer.Room, tiles, offer.Cost.Value));
        }
        foreach ((RoomTypeId room, int tiles, _) in candidates.OrderBy(c => c.Tiles).ThenBy(c => c.Cost).ThenBy(c => c.Room))
        {
            bool built = tiles == 0
                ? Build(observation, home, room, grow: false, actions, keep)
                : Build(observation, home, room, grow: true, actions, keep)
                  || _strategy.SecondRooms == 1 && Build(observation, home, room, grow: false, actions, keep);
            if (!built) continue;
            _lastRecruit = observation.Stamp.Tick;
            NoteMore($"recruiting: no new creature for {tick - _lastArrival.Value} ticks, {room} had {tiles} tiles");
            return true;
        }
        return false;
    }

    /// <summary>Adds to the running routine's note.</summary>
    private void NoteMore(string more) => _note = _note is null ? more : _note + "; " + more;

    /// <summary>Whether a missing listed room is offered but costs more than the gold: priced at the side
    /// <see cref="Build"/> waits for (its target, while a free square of it exists), else its smallest.</summary>
    private bool Unaffordable(PlayerObservation observation, RoomTypeId room)
    {
        int target = TargetSide(room);
        int side = target > SmallestSide(room)
            && PlayerActionRoutines.RoomSquares(observation, PlayerActionRoutines.Home(observation), target).Exists(c => !Tried(room).Contains(c))
            ? target : SmallestSide(room);
        foreach (PlayerRoomOffer offer in observation.Offers.Rooms)
            if (offer.Room == room) return (long)offer.Cost.Value * side * side > observation.Gold.Value;
        return false;
    }

    /// <summary>The side a new room of this type is built at (Task 3a): the strategy's square, raised to the
    /// smallest side at which the room does anything (a Temple draws nothing below 5x5) and to the side
    /// its attraction and floor-slot targets need; never above the largest side the knowledge measured.</summary>
    private int TargetSide(RoomTypeId room)
    {
        int side = SmallestSide(room);
        if (_options.Knowledge.Room(room) is not RoomFacts facts) return side;
        if (_strategy.AttractionTargets.TryGetValue(room, out int target))
            side = Math.Max(side, facts.SideFor(target) ?? facts.Sizes[^1].Side);
        if (_strategy.SlotTargets.TryGetValue(room, out int slots))
            side = Math.Max(side, facts.SideForSlots(slots) ?? facts.Sizes[^1].Side);
        return side;
    }

    /// <summary>The smallest side a room of this type is built at: the strategy's square, or the smallest at
    /// which the room does anything when that is larger.</summary>
    private int SmallestSide(RoomTypeId room) =>
        Math.Max(_strategy.RoomSide, _options.Knowledge.Room(room)?.SmallestUsefulSide ?? 0);

    /// <summary>Builds <paramref name="room"/> at its target side, or, only with no free square that big, the
    /// largest smaller side down to its smallest useful one; a free square it cannot yet pay for is waited
    /// for, not swapped for a smaller room. Growth uses the smallest side.</summary>
    private bool Build(PlayerObservation observation, TilePos home, RoomTypeId room, bool grow, List<PlayerAction> actions, long keep = 0)
    {
        int largest = grow ? SmallestSide(room) : TargetSide(room);
        for (int side = largest; side >= SmallestSide(room); side--)
        {
            if (PlayerActionRoutines.BuildRoom(observation, home, room, side, Tried(room), grow) is not PlayerAction.BuildRoom build)
            {
                if (PlayerActionRoutines.RoomSquares(observation, home, side).Exists(c => !Tried(room).Contains(c))) return false;
                // No free square this big for a room that must be larger than the plain square (a Library for
                // its Warlocks, a Temple): dig one out at the target side, and wait for it rather than settle.
                if (!grow && side == largest && (_strategy.Sites == RoomSites.All || _strategy.Sites == RoomSites.Oversized && side > _strategy.RoomSide))
                    switch (DigSite(observation, home, room, side, actions))
                    {
                        case SiteState.Marked: return true;
                        case SiteState.Waiting: return false;
                    }
                continue;
            }
            // Growth and extra rooms leave the gold budget standing after they are paid for (E2).
            if (keep > 0 && Price(observation, room) is long price && observation.Gold.Value - price * side * side < keep)
            {
                NoteMore($"{room} {side}x{side} would leave less than {keep} gold");
                return false;
            }
            if (_site is Site built && built.Room == room) _site = null;
            if (PlayerActionRoutines.SelectRoom(observation, room) is PlayerAction press) actions.Add(press);
            actions.Add(build);
            NoteMore($"{(grow ? "grow" : "build")} {room} {side}x{side} at {build.From}");
            _pendingRooms[room] = new PendingRoom(build.From, side, observation.Stamp.Tick, OwnedTiles(observation, room));
            return true;
        }
        return false;
    }

    /// <summary>Gives up an open site that has had its time, or whose room the strategy no longer builds or
    /// the player already owns, so one forgotten site cannot hold every later one back. A site dug out in time
    /// whose room still waits for gold is closed without settling: the free square stays for the build.</summary>
    private void AgeSite(PlayerObservation observation)
    {
        if (_site is not Site open) return;
        bool wanted = (_strategy.Rooms.Contains(open.Room) || _strategy.ExtraRooms.Contains(open.Room)) && !Owns(observation, open.Room);
        if (wanted && observation.Stamp.Tick.Value - open.Since.Value < SiteTicks) return;
        if (wanted && PlayerActionRoutines.SquareFree(observation, open.Corner, open.Side)) { _site = null; return; }
        _failedSites.Add(open.Corner);
        if (wanted) _settled.Add((open.Room, open.Side));   // not dug out in time: that room settles for less
        _site = null;
    }

    /// <summary>The site for <paramref name="room"/> (Task 3a): waiting while its marks are dug, given up after
    /// <see cref="SiteTicks"/>; otherwise, with digging on and no other site open, the square of
    /// <paramref name="side"/> needing the least digging is marked in one drag. One site at a time.</summary>
    private SiteState DigSite(PlayerObservation observation, TilePos home, RoomTypeId room, int side, List<PlayerAction> actions)
    {
        if (_site is Site open)
        {
            // This room's site is being dug, or another room's is (one at a time): wait rather than settle
            // for a smaller room for good. AgeSite gives a site up once it has had its time.
            return SiteState.Waiting;
        }
        if (_settled.Contains((room, side))) return SiteState.None;
        if (_strategy.Dig == DigMode.Off || (observation.Offers.Actions & PlayerActionKinds.TagArea) == 0 || !Fits(actions, 1)
            || !observation.Offers.Rooms.Any(o => o.Room == room)) return SiteState.None;
        bool breach = _strategy.Breach == BreachMode.Always;
        if (!breach) EnsureClearance();
        if (PlayerActionRoutines.RoomSite(observation, home, side, _failedSites, breach ? null : _danger, breach) is not TilePos corner)
            return SiteState.None;
        actions.Add(new PlayerAction.TagArea(corner, new TilePos(corner.X + side - 1, corner.Y + side - 1)));
        _site = new Site(room, corner, side, observation.Stamp.Tick);
        return SiteState.Marked;
    }

    /// <summary>Finishes a square a build left short (Task 3a): the same rectangle again, when the panel
    /// offers the room and the player can pay for the missing tiles; given up after a few tries or once
    /// the square holds anything but the room's own tiles and free own floor.</summary>
    private bool Finish(PlayerObservation observation, List<PlayerAction> actions)
    {
        foreach (RoomTypeId room in _repairs.Keys.OrderBy(r => r).ToList())
        {
            Repair repair = _repairs[room];
            int missing = PlayerActionRoutines.MissingRoomTiles(observation, repair.Corner, repair.Side, room);
            if (missing <= 0 || repair.Tries <= 0) { _repairs.Remove(room); continue; }
            PlayerRoomOffer? offer = null;
            foreach (PlayerRoomOffer candidate in observation.Offers.Rooms) if (candidate.Room == room) offer = candidate;
            if ((observation.Offers.Actions & PlayerActionKinds.BuildRoom) == 0 || offer is not PlayerRoomOffer priced
                || (long)priced.Cost.Value * missing > observation.Gold.Value) continue;
            if (PlayerActionRoutines.SelectRoom(observation, room) is PlayerAction press) actions.Add(press);
            actions.Add(new PlayerAction.BuildRoom(room, repair.Corner, new TilePos(repair.Corner.X + repair.Side - 1, repair.Corner.Y + repair.Side - 1)));
            _repairs[room] = repair with { Tries = repair.Tries - 1 };
            _pendingRooms[room] = new PendingRoom(repair.Corner, repair.Side, observation.Stamp.Tick, OwnedTiles(observation, room), Grew: true);
            return true;
        }
        return false;
    }

    /// <summary>A Lair or Hatchery with fewer tiles than the own non-workers need, or a Treasury once
    /// gold nears what the heart and treasuries hold (only when the options know the capacities).</summary>
    private bool Outgrown(PlayerObservation observation, RoomTypeId room)
    {
        int tiles = OwnedTiles(observation, room);
        if (tiles == 0) return false;
        if (_strategy.TileTargets.TryGetValue(room, out int target) && tiles < target) return true;
        switch (room)
        {
            case RoomTypeId.Lair:
                // The beds the Lair's hover shows (E3); without that fill, its tiles per non-worker.
                if (_strategy.FreeBeds > 0 && Summary(observation, RoomTypeId.Lair) is { Capacity: > 0 } lair)
                    return Grow(lair.Capacity - NonWorkers(observation) < _strategy.FreeBeds,
                        $"Lair {lair.Capacity} beds for {NonWorkers(observation)} non-workers");
                return Grow(tiles < (long)NonWorkers(observation) * _strategy.LairTilesPerCreature,
                    $"Lair {tiles} tiles for {NonWorkers(observation)} non-workers");
            case RoomTypeId.Hatchery:
                int hungry = 0;
                foreach (PlayerCreatureSummary creature in observation.Creatures) if (creature.Hungry && !_options.IsWorker(creature.Type)) hungry++;
                int nonWorkers = NonWorkers(observation);
                // Hunger grows it only while it is small for the army: a new Hatchery's chickens take time to hatch, and the
                // chicken stays on the flowers meanwhile (Level 5, 2026-11-16: 86 Hatchery tiles for four fighters, no gold left).
                if (_strategy.HungryPercent < 100 && tiles < HatcheryTilesPerCreature * nonWorkers
                    && hungry >= Math.Max(2, nonWorkers * _strategy.HungryPercent / 100)
                    && !(_hungerGrowth is GameTick grew && observation.Stamp.Tick.Value - grew.Value < HungerGrowthTicks))
                {
                    _hungerAsked = true;
                    return Grow(true, $"{hungry} of {nonWorkers} non-workers hungry with {tiles} Hatchery tiles");
                }
                return Grow((long)tiles * _strategy.CreaturesPerHatcheryTile < nonWorkers,
                    $"Hatchery {tiles} tiles for {nonWorkers} non-workers");
            case RoomTypeId.Treasury:
                if (_options.GoldPerTreasuryTile is not int perTile) return false;
                long capacity = (long)tiles * perTile + (Owns(observation, RoomTypeId.DungeonHeart) ? _options.HeartGold ?? 0 : 0);
                return Grow((long)observation.Gold.Value * 100 >= capacity * _strategy.TreasuryFullPercent,
                    $"Treasury holds {observation.Gold.Value} of {capacity}");
            default:
                return false;
        }

        bool Grow(bool outgrown, string why)
        {
            if (outgrown) NoteMore("outgrown: " + why);
            return outgrown;
        }
    }

    /// <summary>The bank (E2): wages are collected from a Treasury holding gold (PaydaySystem: a creature walks to a paying
    /// room with gold; Level 5, 2026-11-16: the Treasury ran dry while gold sat in the Heart, and 22-25 creatures a run left
    /// unpaid). With the hand free and no enemy near home, while the Treasury's own fill (its hover) is below the gold
    /// budget and more gold lies elsewhere, a store pile not on a Treasury tile is lifted and carried to the Treasury, as
    /// the owner did on Level 6a (two gold pick-ups). Off with the gold budget.</summary>
    private bool Bank(PlayerObservation observation, TilePos home, List<PlayerAction> actions)
    {
        if (_strategy.GoldBudget == 0 || observation.HandCount > 0 || !Fits(actions, 1)
            || (observation.Offers.Actions & PlayerActionKinds.PickUp) == 0
            || Summary(observation, RoomTypeId.Treasury) is not PlayerRoomSummary treasury) return false;
        // Only what wages need (the bill itself, not the margin), and only with room for a whole pile (a Heart tile holds
        // up to BankPileGold; what does not fit lands as loose gold beside the store).
        long need = observation.WageBill.Value;
        if (treasury.Used >= need || observation.Gold.Value <= treasury.Used || treasury.Capacity - treasury.Used < BankPileGold) return false;
        if (_lastBank is GameTick banked && observation.Stamp.Tick.Value - banked.Value < BankGapTicks) return false;
        if (PlayerActionRoutines.ThreatNear(observation, home, _strategy.EnemyNearTiles)) { Note("Treasury short, but an enemy is near home"); return false; }
        PlayerTileGrid grid = observation.Tiles;
        PlayerVisibleThing? pile = null;
        foreach (PlayerVisibleThing thing in observation.Things)
            if (thing.Kind == PlayerThingKind.StoreGold && (thing.Actions & PlayerThingActions.PickUp) != 0 && grid.Contains(thing.Tile)
                && grid[thing.Tile].Room != RoomTypeId.Treasury
                && (pile is null || PlayerActionRoutines.DistanceSquared(thing.Tile, home) < PlayerActionRoutines.DistanceSquared(pile.Value.Tile, home)))
                pile = thing;
        if (pile is not PlayerVisibleThing gold
            || PlayerActionRoutines.NearestRoomTile(observation, RoomTypeId.Treasury, home, _refusedWork) is not TilePos to)
        {
            Note($"Treasury holds {treasury.Used} of {need} needed; no gold pile outside it to carry");
            return false;
        }
        // The drop goes to the Treasury only with no enemy near it (Hand's rule); else the gold would land as loose gold.
        if (PlayerActionRoutines.ThreatNear(observation, to, _strategy.EnemyNearTiles)) { Note("Treasury short, but an enemy is near it"); return false; }
        actions.Add(new PlayerAction.PickUp(gold.Handle));
        _lastBank = observation.Stamp.Tick;
        _carryTo = to;
        _carryWorker = true;   // goes down at the Treasury, never toward an enemy
        Note($"Treasury holds {treasury.Used} of {need} needed: carry the gold at {gold.Tile} to {to}");
        return true;
    }

    /// <summary>The bridge routine (economy and assault plan E2: income comes from new ground; Level 5, 2026-11-16: no tile left
    /// to mark after tick ~6000, so no gold came in and the army left unpaid). Once nothing is left to dig and no mark stands,
    /// the shortest straight span of water (a Wooden Bridge) or lava (a Stone Bridge), at most <see cref="MaxBridgeTiles"/>,
    /// from own floor to new ground (diggable rock, unseen ground or floor not the player's) is built in one drag, nearest
    /// home first, when the panel offers that bridge and the gold budget stands after it. A dragged span spreads across the
    /// hazard from its end that touches own floor, and a built bridge is own floor for the next (bridge-placement.md sections
    /// 1-2).</summary>
    private bool Bridge(PlayerObservation observation, TilePos home, List<PlayerAction> actions)
    {
        if (_strategy.Dig == DigMode.Off || _pendingRooms.Count > 0 || !Fits(actions, 2)
            || (observation.Offers.Actions & PlayerActionKinds.BuildRoom) == 0) return false;
        if (Outstanding(observation) > 0 || Frontier(observation, home, preferGold: false).Count > 0) return false;
        PlayerTileGrid grid = observation.Tiles;
        PlayerId self = observation.Stamp.Player;
        long keep = _strategy.GoldBudget == 1 ? GoldNeed(observation) : 0;
        (TilePos From, TilePos To, RoomTypeId Room, long Score)? best = null;
        ReadOnlySpan<(int X, int Y)> directions = [(1, 0), (-1, 0), (0, 1), (0, -1)];
        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                var floor = new TilePos(x, y);
                if (!PlayerActionRoutines.IsOwnFloor(grid[floor], self)) continue;
                foreach ((int dx, int dy) in directions)
                {
                    var first = new TilePos(x + dx, y + dy);
                    if (!grid.Contains(first)) continue;
                    PlayerTileFlags hazard = grid[first].Flags & (PlayerTileFlags.Water | PlayerTileFlags.Lava);
                    if (hazard is not (PlayerTileFlags.Water or PlayerTileFlags.Lava) || grid[first].Room != default) continue;
                    int k = 0;
                    var at = first;
                    while (grid.Contains(at) && (grid[at].Flags & PlayerTileFlags.Known) != 0 && (grid[at].Flags & hazard) != 0
                           && grid[at].Room == default && k <= MaxBridgeTiles)
                    {
                        k++;
                        at = new TilePos(at.X + dx, at.Y + dy);
                    }
                    if (k == 0 || k > MaxBridgeTiles || !grid.Contains(at)) continue;
                    PlayerTile end = grid[at];
                    bool fresh = (end.Flags & PlayerTileFlags.Known) == 0
                        || (end.Flags & (PlayerTileFlags.Water | PlayerTileFlags.Lava)) == 0
                           && ((end.Flags & PlayerTileFlags.Diggable) != 0 || (end.Flags & PlayerTileFlags.Solid) == 0 && end.Owner != self);
                    if (!fresh || _covered.Contains(at)) continue;
                    RoomTypeId bridge = hazard == PlayerTileFlags.Water ? RoomTypeId.WoodenBridge : RoomTypeId.StoneBridge;
                    if (Tried(bridge).Contains(first) || Price(observation, bridge) is not long price
                        || observation.Gold.Value - price * k < keep) continue;
                    var last = new TilePos(first.X + dx * (k - 1), first.Y + dy * (k - 1));
                    long score = (long)k * 1000 + PlayerActionRoutines.DistanceSquared(floor, home);
                    if (best is not { } b || score < b.Score) best = (first, last, bridge, score);
                }
            }
        if (best is not { } span) { Note("nothing left to dig and no span to bridge"); return false; }
        if (PlayerActionRoutines.SelectRoom(observation, span.Room) is PlayerAction press) actions.Add(press);
        actions.Add(new PlayerAction.BuildRoom(span.Room, span.From, span.To));
        _pendingRooms[span.Room] = new PendingRoom(span.From, 1, observation.Stamp.Tick, OwnedTiles(observation, span.Room));
        Tried(span.Room).Add(span.From);
        Note($"nothing left to dig: {span.Room} from {span.From} to {span.To}");
        return true;
    }

    /// <summary>The longest span of water or lava the bridge routine builds in one drag.</summary>
    private const int MaxBridgeTiles = 8;

    private GameTick? _lastBank;

    /// <summary>The most gold one store pile outside a Treasury holds (a Heart tile, MaxGoldPerDungeonHeartTile 1000).</summary>
    private const int BankPileGold = 1000;

    /// <summary>Ticks between two bank carries (10 game seconds): the hand is left free for other work in between.</summary>
    private const int BankGapTicks = 40;

    /// <summary>The gold the budget keeps (E2): the coming payday's wage bill plus the strategy's margin.</summary>
    private long GoldNeed(PlayerObservation observation) => (long)observation.WageBill.Value * (100 + _strategy.GoldMarginPercent) / 100;

    /// <summary>Whether gold is below the budget (never while no wages are due).</summary>
    private bool Short(PlayerObservation observation) =>
        _strategy.GoldBudget == 1 && observation.WageBill.Value > 0 && observation.Gold.Value < GoldNeed(observation);

    /// <summary>The rooms the Work routine fills: the strategy's, or, with none named, an owned Training Room once gold is at
    /// least <see cref="PlayerStrategy.TrainGold"/> and three wage bills (E2).</summary>
    private IReadOnlyList<RoomTypeId> UsedRooms(PlayerObservation observation)
    {
        if (_strategy.UseRooms.Count > 0) return _strategy.UseRooms;
        return _strategy.TrainGold > 0 && Owns(observation, RoomTypeId.TrainingRoom)
            && observation.Gold.Value >= Math.Max(_strategy.TrainGold, 3L * observation.WageBill.Value) ? [RoomTypeId.TrainingRoom] : [];
    }

    /// <summary>The panel's per-tile price of <paramref name="room"/>, or null when it is not offered.</summary>
    private static long? Price(PlayerObservation observation, RoomTypeId room)
    {
        foreach (PlayerRoomOffer offer in observation.Offers.Rooms) if (offer.Room == room) return offer.Cost.Value;
        return null;
    }

    private static PlayerRoomSummary? Summary(PlayerObservation observation, RoomTypeId room)
    {
        foreach (PlayerRoomSummary summary in observation.Rooms) if (summary.Type == room) return summary;
        return null;
    }

    /// <summary>An attack holding its ground (A4): where it was last answered, since when, when last seen, the most own
    /// fighters seen meanwhile, and whether it became a siege (latched until the attack is gone).</summary>
    private sealed class Stand(TilePos at, GameTick since, int fighters)
    {
        public TilePos Origin { get; } = at;
        public TilePos At { get; set; } = at;
        public GameTick Since { get; } = since;
        public GameTick Seen { get; set; } = since;
        public int Peak { get; set; } = fighters;
        public bool Siege { get; set; }
    }

    private Stand? _stand;

    /// <summary>Where a rally party being gathered goes down, and how many it wants, or null.</summary>
    private TilePos? _rallyDrop;
    private int _rallyWant;
    private bool _rallyDropping;

    /// <summary>Ticks an attack must hold within <see cref="StandTiles"/> of where it was first answered before it can be a
    /// siege (5 game minutes), and how far it may move meanwhile.</summary>
    private const int SiegeTicks = 1200, StandTiles = 5;

    /// <summary>Own fighters lost below the stand's peak that make it a siege.</summary>
    private const int SiegeLosses = 4;

    /// <summary>An attack unseen this long is gone (100 game seconds).</summary>
    private const int StandForgetTicks = 400;

    /// <summary>The whole-army push (A4). Tracks the attack the hand answers; once one has held within
    /// <see cref="StandTiles"/> for <see cref="SiegeTicks"/> while own fighters fell <see cref="SiegeLosses"/> below their
    /// peak, it is a siege: no fighter goes to it alone. The player waits until the party (<see cref="PlayerStrategy.RallyParty"/>,
    /// or two thirds of its fighters, at least four) is fit and free, lifts it over as many decisions as the action budget
    /// needs, and drops it all at once on the Blockers' place. Null when the routine leaves the hand to the usual answer;
    /// otherwise whether it acted.</summary>
    private bool? Rally(PlayerObservation observation, TilePos home, PlayerVisibleThing? enemy, List<PlayerAction> actions)
    {
        _holdDefenders = false;
        if (_strategy.RallyParty <= 0) return null;
        GameTick now = observation.Stamp.Tick;
        int fighters = 0;
        foreach (PlayerCreatureSummary creature in observation.Creatures)
            if (!_options.IsWorker(creature.Type) && _options.StyleOf(creature.Type) != FightStyle.NonFighter) fighters++;
        if (enemy is PlayerVisibleThing foe)
        {
            if (_stand is not Stand stand || Chebyshev(stand.At, foe.Tile) > StandTiles) _stand = new Stand(foe.Tile, now, fighters);
            else
            {
                // The stand follows its attack as it paces (Level 4 seed 3: the Lord walks x 21-27 on row 4); only a jump of
                // more than StandTiles between sightings is a new attack.
                stand.At = foe.Tile;
                stand.Seen = now;
                // A steady advance is no stand: once it is more than three times StandTiles from where it began, start again.
                if (Chebyshev(stand.Origin, foe.Tile) > 3 * StandTiles) _stand = new Stand(foe.Tile, now, fighters);
                stand.Peak = Math.Max(stand.Peak, fighters);
                // Never at home: a raid on the dungeon is always answered (defence plan D2); a siege is a stand far out.
                if (!stand.Siege && now.Value - stand.Since.Value >= SiegeTicks && fighters <= stand.Peak - SiegeLosses
                    && PlayerActionRoutines.DistanceSquared(foe.Tile, home) > 4L * _strategy.HomeGuardTiles * _strategy.HomeGuardTiles) stand.Siege = true;
            }
        }
        else if (_stand is Stand old && now.Value - old.Seen.Value > StandForgetTicks) _stand = null;
        if (_stand is not { Siege: true } siege || enemy is not PlayerVisibleThing target)
        {
            _rallyDrop = null;
            _rallyDropping = false;
            return null;
        }
        // The party's tile, taken afresh each decision from where the attack stands now, past tiles a drop failed on.
        if (FormationDrop(observation, target.Tile, home, FightStyle.Blocker) is not TilePos drop)
        {
            Note($"siege at {siege.At}: no own floor to drop on");
            _holdDefenders = true;
            return null;
        }

        if (observation.HandCount > 0 && _rallyDrop is not null)
        {
            // Still gathering: lift more while the party is short, fighters are free and the gathering is young; else all
            // go down together, over as many decisions as the action budget needs, never lifting again once one has.
            List<PlayerVisibleThing> more = _rallyDropping || now.Value - _rallyLiftTick.Value > RallyGatherTicks
                ? [] : FreeFighters(observation, home, drop);
            if (more.Count > 0 && observation.HandCount < _rallyWant && (observation.Offers.Actions & PlayerActionKinds.PickUp) != 0)
            {
                int lifted = 0;
                for (int i = 0; i < more.Count && observation.HandCount + lifted < _rallyWant && Fits(actions, 1); i++, lifted++)
                    actions.Add(new PlayerAction.PickUp(more[i].Handle));
                Note($"siege at {siege.At}: gathering, {observation.HandCount + lifted} of {_rallyWant} in hand");
                _rallyDrop = drop;
                return true;
            }
            if ((observation.Offers.Actions & PlayerActionKinds.DropAt) == 0) { Note("siege: no drop offered"); return false; }
            int released = 0;
            while (released < observation.HandCount && Fits(actions, 1)) { actions.Add(new PlayerAction.DropAt(drop)); released++; }
            Note($"siege at {siege.At}: drop a party of {released} at {drop} against the attack at {target.Tile}");
            _lastDrop = (drop, observation.HandCount);
            _lastDropTick = now;
            _rallyDropping = released < observation.HandCount;
            if (!_rallyDropping) { _rallyDrop = null; _rallyDone = now; }
            siege.Peak = fighters;   // a new reckoning from the push on
            return true;
        }
        if (observation.HandCount > 0) return null;   // something else is held: the usual drop takes it

        // The hand is free. No fighter goes to the siege alone, but the usual path still heals a hurt defender and takes
        // one out of the fight (it only stops short of lifting one to send in).
        _holdDefenders = true;
        if (_rallyDone is GameTick done && now.Value - done.Value < RallyCooldownTicks)
        {
            Note($"siege at {siege.At}: the last party went in lately");
            return null;
        }
        int want = Math.Max(4, Math.Min(_strategy.RallyParty, fighters * 2 / 3));
        List<PlayerVisibleThing> free = FreeFighters(observation, home, drop);
        if (free.Count < want || (observation.Offers.Actions & PlayerActionKinds.PickUp) == 0)
        {
            Note($"siege at {siege.At}: gathering, {free.Count} of {want} fighters fit and free; none goes alone");
            return null;
        }
        int taken = 0;
        for (int i = 0; i < want && Fits(actions, 1); i++, taken++) actions.Add(new PlayerAction.PickUp(free[i].Handle));
        _rallyDrop = drop;
        _rallyWant = want;
        _rallyDropping = false;
        _rallyLiftTick = now;
        Note($"siege at {siege.At}: lift {taken} of a party of {want} (drop at {drop})");
        return true;
    }

    /// <summary>Own fighters a rally may lift: fit (the flower shows five petals or more), liftable, not in a fight (no threat
    /// within the engaged distance) and not already at the drop (within the engaged distance of it), nearest home first.</summary>
    private List<PlayerVisibleThing> FreeFighters(PlayerObservation observation, TilePos home, TilePos drop)
    {
        var petals = new Dictionary<PlayerThingHandle, int>();
        foreach (PlayerCreatureSummary creature in observation.Creatures)
            if (creature.Handle is PlayerThingHandle seen && creature.FlowerKnown) petals[seen] = creature.Petals;
        long near = (long)_options.EngagedTiles * _options.EngagedTiles;
        var free = new List<PlayerVisibleThing>();
        foreach (PlayerVisibleThing thing in observation.Things)
            if (thing.Kind == PlayerThingKind.Creature && thing.Owner == observation.Stamp.Player && thing.CreatureType is CreatureTypeId type
                && !_options.IsWorker(type) && _options.StyleOf(type) != FightStyle.NonFighter && (thing.Actions & PlayerThingActions.PickUp) != 0
                && petals.TryGetValue(thing.Handle, out int shown) && shown >= 5
                && PlayerActionRoutines.DistanceSquared(thing.Tile, drop) > near
                && !PlayerActionRoutines.ThreatNear(observation, thing.Tile, _options.EngagedTiles)) free.Add(thing);
        free.Sort((a, b) => PlayerActionRoutines.DistanceSquared(a.Tile, home).CompareTo(PlayerActionRoutines.DistanceSquared(b.Tile, home)));
        return free;
    }

    /// <summary>Whether a siege holds the usual one-at-a-time lift back this decision.</summary>
    private bool _holdDefenders;
    private GameTick _rallyLiftTick;
    private GameTick? _rallyDone;

    /// <summary>Ticks a rally may spend gathering in the hand before what it holds goes down (10 game seconds).</summary>
    private const int RallyGatherTicks = 40;

    /// <summary>Ticks after a party went in before the next is gathered (100 game seconds): it fights first.</summary>
    private const int RallyCooldownTicks = 400;

    /// <summary>Where the Imp lifted to capture last decision goes down (the body's tile), or null.</summary>
    private TilePos? _captureDrop;

    /// <summary>When an Imp was last sent to each body (by its tile).</summary>
    private readonly Dictionary<TilePos, GameTick> _captureSent = [];

    /// <summary>Ticks an Imp sent to a body is given before another is sent (15 game seconds of the 60 s rescue window).</summary>
    private const int CaptureRetryTicks = 60;

    /// <summary>The capture routine (R16, Level 5 plan step 2; 2026-11-17): an Imp choosing its own work takes a body only
    /// once no marked rock, claimable floor or loose object is left anywhere (the idle search table at 0x00698658), so the
    /// player drops one on it. With a Prison owned and the hand free, the enemy collapsed on own ground nearest an Imp that
    /// can be lifted, with no standing enemy within two tiles of it, gets that Imp: lifted now, dropped on the body next
    /// decision.</summary>
    private bool Capture(PlayerObservation observation, List<PlayerAction> actions)
    {
        if (!Owns(observation, RoomTypeId.Prison)) return false;
        if (observation.HandCount > 0 || (observation.Offers.Actions & (PlayerActionKinds.PickUp | PlayerActionKinds.DropAt)) == 0) return false;
        PlayerTileGrid grid = observation.Tiles;
        PlayerId self = observation.Stamp.Player;
        long now = observation.Stamp.Tick.Value;
        PlayerVisibleThing? imp = null;
        TilePos target = default;
        long best = long.MaxValue;
        foreach (PlayerVisibleThing body in observation.Things)
        {
            if (body.Kind != PlayerThingKind.Creature || !PlayerActionRoutines.IsEnemy(body, self) || !PlayerActionRoutines.IsDown(body)
                || !grid.Contains(body.Tile) || grid[body.Tile].Owner != self || _failedDrops.Contains(body.Tile)
                // A prisoner dying in an own cell has nowhere to go (0x0046D810 takes no body with a room).
                || grid[body.Tile].Room is RoomTypeId.Prison or RoomTypeId.TortureChamber
                // An own Imp on the body's tile carries it or was dropped there: lifting it would drop the body.
                || ImpOn(body.Tile)
                || _captureSent.TryGetValue(body.Tile, out GameTick sent) && now - sent.Value < CaptureRetryTicks
                || PlayerActionRoutines.ThreatNear(observation, body.Tile, 2)) continue;
            foreach (PlayerVisibleThing worker in observation.Things)
            {
                if (worker.Kind != PlayerThingKind.Creature || worker.Owner != self || worker.CreatureType is not CreatureTypeId type
                    || !_options.IsWorker(type) || (worker.Actions & PlayerThingActions.PickUp) == 0) continue;
                long distance = PlayerActionRoutines.DistanceSquared(worker.Tile, body.Tile);
                if (distance < best) { best = distance; imp = worker; target = body.Tile; }
            }
        }
        if (imp is not PlayerVisibleThing lifted) { Note("no body to capture"); return false; }
        if (!Fits(actions, 1)) return false;
        actions.Add(new PlayerAction.PickUp(lifted.Handle));
        _captureDrop = target;
        _captureSent[target] = observation.Stamp.Tick;
        Note($"lift the Imp at {lifted.Tile} to capture the body at {target}");
        return true;

        bool ImpOn(TilePos at)
        {
            foreach (PlayerVisibleThing other in observation.Things)
                if (other.Kind == PlayerThingKind.Creature && other.Owner == self && other.Tile == at
                    && other.CreatureType is CreatureTypeId type && _options.IsWorker(type)) return true;
            return false;
        }
    }

    /// <summary>Where the assault party lifted last decision goes down, or null.</summary>
    private TilePos? _assaultDrop;

    /// <summary>When a party was last sent at each enemy trap (by its tile).</summary>
    private readonly Dictionary<TilePos, GameTick> _assaulted = [];

    /// <summary>How near (tiles, Chebyshev) a trap the party lands and counts as there.</summary>
    private const int AssaultTiles = 2;

    /// <summary>Ticks a party is given at a trap before another is sent to it (100 game seconds).</summary>
    private const int AssaultRetryTicks = 400;

    /// <summary>The trap assault (A2): with the hand free and no attack answered, the enemy trap in view whose reach covers own
    /// floor or an own dig mark, nearest home, gets <see cref="PlayerStrategy.AssaultParty"/> fighters (never workers, five
    /// petals or more), lifted together in one decision and dropped together the next on the own floor nearest the trap. Own
    /// creatures attack a revealed trap by themselves (traps.md section 11; owner: "yes if they can"). It waits until the
    /// whole party can go rather than sending fighters one by one, and stops once the party stands beside the trap.</summary>
    private bool Assault(PlayerObservation observation, TilePos home, List<PlayerAction> actions)
    {
        if (_strategy.AssaultParty <= 0 || observation.EnemyTraps.Count == 0) return false;
        if (observation.HandCount > 0 || (observation.Offers.Actions & PlayerActionKinds.PickUp) == 0) return false;
        // Defence first: no party goes while an enemy is near home (as Work waits).
        if (PlayerActionRoutines.ThreatNear(observation, home, _strategy.EnemyNearTiles)) { Note("enemy near home: defence first"); return false; }
        PlayerTileGrid grid = observation.Tiles;
        PlayerId self = observation.Stamp.Player;
        PlayerVisibleTrap? target = null;
        long now = observation.Stamp.Tick.Value;
        foreach (PlayerVisibleTrap trap in observation.EnemyTraps)
        {
            // A party sent lately is given its time before another goes to the same trap.
            if (_assaulted.TryGetValue(trap.Tile, out GameTick sent) && now - sent.Value < AssaultRetryTicks || !Blocks(trap)) continue;
            if (target is not PlayerVisibleTrap best
                || PlayerActionRoutines.DistanceSquared(trap.Tile, home) < PlayerActionRoutines.DistanceSquared(best.Tile, home)) target = trap;
        }
        if (target is not PlayerVisibleTrap aim) { Note("no enemy trap covers own ground or a mark"); return false; }
        // The party must land beside the trap to fight it: own floor within AssaultTiles, else the Imps claim nearer first.
        if (PlayerActionRoutines.NearestOwnFloor(observation, aim.Tile, _failedDrops) is not TilePos drop
            || Chebyshev(drop, aim.Tile) > AssaultTiles)
        {
            Note($"no own floor within {AssaultTiles} tiles of the trap at {aim.Tile}");
            return false;
        }
        var petals = new Dictionary<PlayerThingHandle, int>();
        foreach (PlayerCreatureSummary creature in observation.Creatures)
            if (creature.Handle is PlayerThingHandle seen && creature.FlowerKnown) petals[seen] = creature.Petals;
        int there = 0;
        var free = new List<PlayerVisibleThing>();
        foreach (PlayerVisibleThing thing in observation.Things)
        {
            if (thing.Kind != PlayerThingKind.Creature || thing.Owner != self || thing.CreatureType is not CreatureTypeId type
                || _options.IsWorker(type) || _options.StyleOf(type) == FightStyle.NonFighter) continue;
            if (Chebyshev(thing.Tile, aim.Tile) <= AssaultTiles) { there++; continue; }
            // Not one in a fight (an enemy within the engaged distance), nor one whose flower does not show it fit.
            if ((thing.Actions & PlayerThingActions.PickUp) != 0 && petals.TryGetValue(thing.Handle, out int shown) && shown >= 5
                && !PlayerActionRoutines.ThreatNear(observation, thing.Tile, _options.EngagedTiles)) free.Add(thing);
        }
        if (there >= _strategy.AssaultParty) { Note($"{there} fighters at the trap at {aim.Tile}"); return false; }
        // The whole rest of the party goes in this decision or none does: a partial party would walk in alone.
        int want = _strategy.AssaultParty - there;
        if (free.Count < want || _options.MaxActionsPerDecision - actions.Count < want)
        {
            Note($"trap at {aim.Tile}: {free.Count} fighters free, {want} wanted, {_options.MaxActionsPerDecision - actions.Count} actions left");
            return false;
        }
        free.Sort((a, b) => PlayerActionRoutines.DistanceSquared(a.Tile, drop).CompareTo(PlayerActionRoutines.DistanceSquared(b.Tile, drop)));
        for (int i = 0; i < want; i++) actions.Add(new PlayerAction.PickUp(free[i].Handle));
        _assaultDrop = drop;
        _assaulted[aim.Tile] = observation.Stamp.Tick;
        Note($"lift {want} fighters against the {aim.Type} trap at {aim.Tile} (drop at {drop}, {there} there)");
        return true;

        // A trap is in the way when its reach covers own floor or an own dig mark.
        bool Blocks(PlayerVisibleTrap trap)
        {
            int r = trap.ReachTiles;
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    var at = new TilePos(trap.Tile.X + dx, trap.Tile.Y + dy);
                    if (!InReach(dx, dy, r) || !grid.Contains(at)) continue;
                    PlayerTile tile = grid[at];
                    if ((tile.Flags & PlayerTileFlags.Tagged) != 0 || PlayerActionRoutines.IsOwnFloor(tile, self)) return true;
                }
            return false;
        }
    }

    /// <summary>The most Hatchery tiles per non-worker that hunger alone may grow it to.</summary>
    private const int HatcheryTilesPerCreature = 3;

    /// <summary>Ticks after a growth for hunger before hunger may grow the Hatchery again (5 game minutes): its new chickens
    /// hatch and feed first.</summary>
    private const int HungerGrowthTicks = 1200;

    private GameTick? _hungerGrowth;

    /// <summary>Whether this decision's Hatchery growth was asked for by hunger (its cooldown starts once it is queued).</summary>
    private bool _hungerAsked;

    private int NonWorkers(PlayerObservation observation)
    {
        int count = 0;
        foreach (PlayerCreatureSummary creature in observation.Creatures) if (!_options.IsWorker(creature.Type)) count++;
        return count;
    }

    /// <summary>With the hand free, no enemy within <see cref="PlayerStrategy.EnemyNearTiles"/> of home, gold to spare and the interval passed: lifts an
    /// idle own non-worker (one standing on floor that is no room, nearest home first) toward the first used
    /// room with fewer creatures on it than its tiles allow; the drop follows next decision, on the room's
    /// tile nearest home that has not refused one.</summary>
    private bool Work(PlayerObservation observation, TilePos home, List<PlayerAction> actions)
    {
        IReadOnlyList<RoomTypeId> use = UsedRooms(observation);
        if (use.Count == 0 || observation.HandCount > 0 || !Fits(actions, 1)
            || (observation.Offers.Actions & PlayerActionKinds.PickUp) == 0
            || observation.Gold.Value < _strategy.UseRoomMinGold || Short(observation)
            || _lastWorkTick is GameTick last && observation.Stamp.Tick.Value - last.Value < _strategy.UseRoomEveryTicks
            || PlayerActionRoutines.ThreatNear(observation, home, _strategy.EnemyNearTiles)) return false;
        PlayerTileGrid grid = observation.Tiles;
        PlayerId self = observation.Stamp.Player;
        // A creature at the Training Room's cap gains nothing there (owner: "only up to level 4 in the training room").
        var capped = new HashSet<PlayerThingHandle>();
        foreach (PlayerCreatureSummary creature in observation.Creatures)
            if (creature.Handle is PlayerThingHandle seen && creature.FlowerKnown && creature.Level >= _options.TrainingMaxLevel) capped.Add(seen);
        foreach (RoomTypeId room in use)
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
                    && !(room == RoomTypeId.TrainingRoom && capped.Contains(thing.Handle))
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
            // Never into a room an enemy stands near, however far it is from home.
            if (PlayerActionRoutines.ThreatNear(observation, target, _strategy.EnemyNearTiles)) continue;
            actions.Add(new PlayerAction.PickUp(lifted.Handle));
            Note($"lift a {lifted.CreatureType} at {lifted.Tile} to the {room} ({working} working on {tiles} tiles)");
            _carryTo = target;
            _lastWorkTick = observation.Stamp.Tick;
            return true;
        }
        return false;
    }

    /// <summary>The Combat Pit routine (stage 3 plan, Task 4; owner direction, 2026-11-14 and 2026-11-15): with the
    /// hand free, no enemy within <see cref="PlayerStrategy.EnemyNearTiles"/> of home or the Pit and the interval passed, the first that can act of: a creature collapsed on the
    /// Pit (one petal) gets an own Imp dropped beside it, once per <see cref="RescueTicks"/> and only with a Lair to
    /// carry it to, so the Imp takes it to its bed (<c>CarryUnconscious</c>, CarryRules); a fighter on the Pit's
    /// floor at two petals (its last quarter, owner-confirmed) is healed when Heal is offered, affordable and
    /// recharged, else lifted to a Hatchery to eat; a fighter on the floor at the level cap is lifted to its Lair when
    /// unhappy or angry, else to a Hatchery when hurt (four petals or fewer), else to its Lair; then, below
    /// <see cref="PlayerStrategy.PitFighters"/> on the floor, a lone fighter left there (its opponent gone, or none to
    /// come) is lifted to its Lair before it grows unhappy (owner, 2026-11-15); then, when two can pair, an idle (on
    /// plain floor, or watching from the Pit's rim: a spectator is no fighter) healthy (five petals or more), happy
    /// (owner, 2026-11-15) non-worker below the cap is lifted onto the floor (a drop there starts it fighting,
    /// combat-pit.md section 9). The drop follows next decision, as <see cref="Work"/>'s does.</summary>
    private bool Pit(PlayerObservation observation, TilePos home, List<PlayerAction> actions)
    {
        if (_strategy.PitFighters <= 0 || observation.HandCount > 0 || !Fits(actions, 1)
            || OwnedTiles(observation, RoomTypeId.CombatPit) == 0
            || PlayerActionRoutines.ThreatNear(observation, home, _strategy.EnemyNearTiles)
            || PlayerActionRoutines.NearestRoomTile(observation, RoomTypeId.CombatPit, home) is TilePos pitTile
               && PlayerActionRoutines.ThreatNear(observation, pitTile, _strategy.EnemyNearTiles)) return false;
        long now = observation.Stamp.Tick.Value;
        // A rescue does not wait for the interval: a collapsed creature dies within 60 s (CreatureDyingStateDurationSeconds).
        bool paced = _lastPitTick is GameTick last && now - last.Value < _strategy.UseRoomEveryTicks;
        PlayerTileGrid grid = observation.Tiles;
        PlayerId self = observation.Stamp.Player;
        var flowers = new Dictionary<PlayerThingHandle, PlayerCreatureSummary>();
        foreach (PlayerCreatureSummary creature in observation.Creatures)
            if (creature.Handle is PlayerThingHandle seen && creature.FlowerKnown) flowers[seen] = creature;
        int fighting = 0;
        PlayerVisibleThing? downed = null, hurt = null, done = null, recruit = null, alone = null;
        PlayerCreatureSummary doneFlower = default, recruitFlower = default;
        int recruits = 0;
        var workers = new List<PlayerVisibleThing>();
        var workerTiles = new HashSet<TilePos>();
        var bodies = new List<(PlayerVisibleThing Thing, bool Floor)>();
        foreach (PlayerVisibleThing thing in observation.Things)
        {
            if (thing.Kind != PlayerThingKind.Creature || thing.Owner != self || thing.CreatureType is not CreatureTypeId type || !grid.Contains(thing.Tile)) continue;
            if (_options.IsWorker(type))
            {
                if ((thing.Actions & PlayerThingActions.PickUp) != 0) workers.Add(thing);
                workerTiles.Add(thing.Tile);
                continue;
            }
            if (!flowers.TryGetValue(thing.Handle, out PlayerCreatureSummary flower)) continue;
            PlayerTile on = grid[thing.Tile];
            bool floor = on.Room == RoomTypeId.CombatPit && PlayerActionRoutines.IsInterior(grid, thing.Tile, RoomTypeId.CombatPit, self);
            if (on.Room == RoomTypeId.CombatPit && flower.Petals <= 1)
            {
                bodies.Add((thing, floor));
                continue;
            }
            if (floor)
            {
                fighting++;
                alone = thing;
                if (flower.Petals == 2) hurt ??= thing;
                else if (flower.Level >= _options.CombatPitMaxLevel && done is null) { done = thing; doneFlower = flower; }
                continue;
            }
            // Idle on plain floor, or watching from the rim (job 26, combat-pit-spectators.md).
            if ((on.Room == default || on.Room == RoomTypeId.CombatPit) && (thing.Actions & PlayerThingActions.PickUp) != 0
                && flower.Level < _options.CombatPitMaxLevel && flower.Petals >= 5 && flower.Mood == PlayerCreatureMood.Happy)
            {
                recruits++;
                if (recruit is null || flower.Level < recruitFlower.Level) { recruit = thing; recruitFlower = flower; }
            }
        }
        bool picks = (observation.Offers.Actions & PlayerActionKinds.PickUp) != 0;
        // A lone fighter on the floor is a victor whose opponent is gone, or one nobody will come to fight: left there it
        // grows unhappy (owner, 2026-11-15), so it goes to its Lair, unless a partner is on its way.
        // A beaten fighter sinks into the floor and rises on the rim (state 217, combat-pit.md): the Imp goes to it there.
        // Each floor tile's body is timed from when it was first seen there; one fetched after RiseTicks on the floor
        // all the same. A body on an own worker's tile is being carried already.
        var stillSinking = new Dictionary<TilePos, GameTick>();
        foreach ((PlayerVisibleThing body, bool floor) in bodies)
        {
            if (workerTiles.Contains(body.Tile)) continue;
            if (!floor) { downed ??= body; continue; }
            GameTick first = _sinking.TryGetValue(body.Tile, out (GameTick First, GameTick Seen) seen) && now - seen.Seen.Value <= SinkSeenTicks
                ? seen.First : observation.Stamp.Tick;
            stillSinking[body.Tile] = first;
        }
        _sinking.Clear();
        foreach (KeyValuePair<TilePos, GameTick> body in stillSinking)
        {
            _sinking[body.Key] = (body.Value, observation.Stamp.Tick);
            if (downed is null && now - body.Value.Value >= RiseTicks)
                foreach ((PlayerVisibleThing thing, bool floor) in bodies)
                    if (floor && thing.Tile == body.Key) { downed = thing; break; }
        }
        // A pair seen fighting on any decision is remembered until the routine next runs: a fight may begin and end
        // inside its interval.
        if (fighting >= 2) _pitPaired = true;
        // The liftable own Imp nearest the body carries it.
        PlayerVisibleThing? carrier = null;
        if (downed is PlayerVisibleThing target)
            foreach (PlayerVisibleThing worker in workers)
                if (carrier is not PlayerVisibleThing b || PlayerActionRoutines.DistanceSquared(worker.Tile, target.Tile)
                    < PlayerActionRoutines.DistanceSquared(b.Tile, target.Tile)) carrier = worker;
        if (downed is PlayerVisibleThing body2 && carrier is PlayerVisibleThing imp && picks && Owns(observation, RoomTypeId.Lair)
            && !(_lastRescueTick is GameTick rescued && now - rescued.Value < RescueGapTicks)
            && !(_rescues.TryGetValue(body2.Tile, out GameTick sent) && now - sent.Value < RescueTicks)
            && PlayerActionRoutines.RescueDropTile(observation, body2.Tile) is TilePos beside)
        {
            actions.Add(new PlayerAction.PickUp(imp.Handle));
            _carryTo = beside;
            _carryForPit = true;
            _carryWorker = true;
            _rescues[body2.Tile] = observation.Stamp.Tick;
            _lastRescueTick = observation.Stamp.Tick;
            return true;
        }
        if (paced) return false;
        bool paired = _pitPaired;
        _pitPaired = fighting >= 2;
        bool lone = fighting == 1 && (paired || recruits == 0 || fighting >= _strategy.PitFighters);
        if (hurt is PlayerVisibleThing low && HealOffered(observation)) actions.Add(new PlayerAction.CastSpell(HealSpell, Target: low.Handle));
        else if (hurt is PlayerVisibleThing tired && Lift(observation, tired, RoomTypeId.Hatchery, home, actions)) { }
        else if (done is PlayerVisibleThing capped && Lift(observation, capped,
                     doneFlower.Mood != PlayerCreatureMood.Happy ? RoomTypeId.Lair : doneFlower.Petals <= 4 ? RoomTypeId.Hatchery : RoomTypeId.Lair,
                     home, actions)) { }
        else if (lone && alone is PlayerVisibleThing victor && Lift(observation, victor, RoomTypeId.Lair, home, actions)) _pitPaired = false;
        else if (recruit is PlayerVisibleThing fighter && fighting < _strategy.PitFighters && fighting + recruits >= 2 && picks
            && PlayerActionRoutines.InteriorRoomTile(observation, RoomTypeId.CombatPit, home, _refusedPit) is TilePos pit)
        {
            actions.Add(new PlayerAction.PickUp(fighter.Handle));
            _carryTo = pit;
            _carryForPit = true;
        }
        else return false;
        _lastPitTick = observation.Stamp.Tick;
        return true;
    }

    /// <summary>The Imp scouting routine (stage 3 plan, Task 4; owner direction, 2026-11-14): one own Imp at a time is
    /// dropped on neutral, roomless dry floor at the edge of what the player knows (where the hand lets a worker down,
    /// 0x00450910), lets its sight reveal the ground beyond (FogOfWarSystem) for <see cref="ScoutDwellTicks"/>, then is
    /// lifted again and taken to the next such tile, or home. It must not die: a target within
    /// <see cref="ScoutDangerTiles"/> of an enemy seen or sighted is never chosen, and the Imp is lifted, and taken
    /// home, on the next decision this routine gets (one that no earlier routine used) once an enemy comes that near
    /// it or it loses a petal. It reuses <see cref="PlayerStrategy.UseRoomEveryTicks"/> as the pause between scouting
    /// trips. What it reveals feeds the existing plans (gold
    /// to dig, rooms to site). Off unless <see cref="PlayerStrategy.Scouts"/> is 1.</summary>
    private bool Scout(PlayerObservation observation, TilePos home, List<PlayerAction> actions)
    {
        if (_strategy.Scouts <= 0 || observation.HandCount > 0 || !Fits(actions, 1)
            || (observation.Offers.Actions & PlayerActionKinds.PickUp) == 0) return false;
        long now = observation.Stamp.Tick.Value;
        if (_scout is (TilePos at, GameTick since))
        {
            // The scout is the own worker nearest where it was last seen, within a decision's walk of it: a dropped Imp
            // may take a job beside it (ImpTaskSystem's drop scan), and it is followed there.
            PlayerVisibleThing? imp = null;
            foreach (PlayerVisibleThing thing in observation.Things)
                if (thing.Kind == PlayerThingKind.Creature && thing.Owner == observation.Stamp.Player && thing.CreatureType is CreatureTypeId type
                    && _options.IsWorker(type) && Chebyshev(thing.Tile, at) <= ScoutFollowTiles
                    && (imp is null || Chebyshev(thing.Tile, at) < Chebyshev(imp.Value.Tile, at))) imp = thing;
            if (imp is not PlayerVisibleThing scout)
            {
                // Lost (lifted, or out of sight): stop watching for it.
                _scout = null;
                return false;
            }
            _scout = (scout.Tile, since);
            bool hurt = false;
            foreach (PlayerCreatureSummary creature in observation.Creatures)
                if (creature.Handle == scout.Handle && creature.FlowerKnown && creature.Petals < 6) hurt = true;
            bool danger = hurt || EnemyWithin(observation, scout.Tile, ScoutDangerTiles);
            if (!danger && now - since.Value < ScoutDwellTicks) return false;
            if ((scout.Actions & PlayerThingActions.PickUp) == 0) return false;
            actions.Add(new PlayerAction.PickUp(scout.Handle));
            _scout = null;
            _carryWorker = true;
            if (!danger && ScoutTarget(observation, home) is TilePos next) { _carryTo = next; _carryForPit = true; _carryScout = true; }
            _lastScoutTick = observation.Stamp.Tick;
            return true;
        }
        if (_lastScoutTick is GameTick last && now - last.Value < _strategy.UseRoomEveryTicks
            || PlayerActionRoutines.ThreatNear(observation, home, _strategy.EnemyNearTiles)) return false;
        if (ScoutTarget(observation, home) is not TilePos target)
        {
            _lastScoutTick = observation.Stamp.Tick;          // none now: look again after the interval, not every decision
            return false;
        }
        var petals = new Dictionary<PlayerThingHandle, int>();
        foreach (PlayerCreatureSummary creature in observation.Creatures)
            if (creature.Handle is PlayerThingHandle seen && creature.FlowerKnown) petals[seen] = creature.Petals;
        PlayerVisibleThing? sent = null;
        int workers = 0;
        foreach (PlayerVisibleThing thing in observation.Things)
        {
            if (thing.Kind != PlayerThingKind.Creature || thing.Owner != observation.Stamp.Player || thing.CreatureType is not CreatureTypeId worker
                || !_options.IsWorker(worker)) continue;
            workers++;
            // Only an unhurt Imp goes: one already scratched would be lifted home again at once.
            if ((thing.Actions & PlayerThingActions.PickUp) == 0 || petals.GetValueOrDefault(thing.Handle, 6) < 6) continue;
            if (sent is null || PlayerActionRoutines.DistanceSquared(thing.Tile, target) < PlayerActionRoutines.DistanceSquared(sent.Value.Tile, target))
                sent = thing;
        }
        if (workers < MinImpsToScout || sent is not PlayerVisibleThing chosen)
        {
            _lastScoutTick = observation.Stamp.Tick;          // no Imp to spare now: look again after the interval
            return false;
        }
        actions.Add(new PlayerAction.PickUp(chosen.Handle));
        _carryTo = target;
        _carryForPit = true;
        _carryWorker = true;
        _carryScout = true;
        _lastScoutTick = observation.Stamp.Tick;
        return true;
    }

    /// <summary>The next scouting tile: known, dry, not solid, in no room, and nobody's (unclaimed ground such as the dirt
    /// path, whose terrain is not ownable so the view shows no owner, though a loaded map gives it to the neutral player;
    /// or ownable ground the neutral player holds: the hand also lets a worker down on own and allied ground,
    /// 0x00450910), beside a tile the player does not know, not scouted before, and more than
    /// <see cref="ScoutDangerTiles"/> from an enemy seen or sighted; nearest home, then Y, then X. Null without one.</summary>
    private TilePos? ScoutTarget(PlayerObservation observation, TilePos home)
    {
        PlayerTileGrid grid = observation.Tiles;
        TilePos? best = null;
        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                var at = new TilePos(x, y);
                PlayerTile tile = grid[at];
                if (!tile.IsKnown || (tile.Flags & (PlayerTileFlags.Solid | PlayerTileFlags.Water | PlayerTileFlags.Lava)) != 0
                    || tile.Room != default || tile.Owner.Value is not (0 or PlayerIds.Neutral) || _scouted.Contains(at)) continue;
                bool edge = false;
                for (int dy = -1; dy <= 1 && !edge; dy++)
                    for (int dx = -1; dx <= 1 && !edge; dx++)
                    {
                        var near = new TilePos(x + dx, y + dy);
                        edge = grid.Contains(near) && !grid[near].IsKnown;
                    }
                if (!edge || EnemyWithin(observation, at, ScoutDangerTiles) || NearOthersGround(grid, at, observation.Stamp.Player)) continue;
                if (best is not TilePos b || PlayerActionRoutines.DistanceSquared(at, home) < PlayerActionRoutines.DistanceSquared(b, home)) best = at;
            }
        return best;
    }

    /// <summary>Whether an enemy in sight now, or seen within <see cref="ThreatMemoryTicks"/>, stands within
    /// <paramref name="tiles"/> of <paramref name="at"/>.</summary>
    private bool EnemyWithin(PlayerObservation observation, TilePos at, int tiles)
    {
        foreach (PlayerVisibleThing thing in observation.Things)
            if (PlayerActionRoutines.IsThreat(observation, thing) && Chebyshev(thing.Tile, at) <= tiles) return true;
        long now = observation.Stamp.Tick.Value;
        foreach ((TilePos seen, GameTick when) in _lastThreatAt)
            if (now - when.Value < ThreatMemoryTicks && Chebyshev(seen, at) <= tiles) return true;
        return false;
    }

    /// <summary>Whether known ground of another keeper or the heroes lies within <see cref="ScoutDangerTiles"/> of <paramref name="at"/>.</summary>
    private static bool NearOthersGround(PlayerTileGrid grid, TilePos at, PlayerId self)
    {
        for (int dy = -ScoutDangerTiles; dy <= ScoutDangerTiles; dy++)
            for (int dx = -ScoutDangerTiles; dx <= ScoutDangerTiles; dx++)
            {
                var near = new TilePos(at.X + dx, at.Y + dy);
                if (!grid.Contains(near)) continue;
                PlayerTile tile = grid[near];
                if (tile.IsKnown && tile.Owner != self && tile.Owner.Value is not (0 or PlayerIds.Neutral)) return true;
            }
        return false;
    }

    /// <summary>How long (ticks) a threat seen keeps scouting away from where it stood: 5 game minutes.</summary>
    private const int ThreatMemoryTicks = 1200;

    /// <summary>How far (tiles) from where it was last seen a scouting Imp is still recognised and followed.</summary>
    private const int ScoutFollowTiles = 3;

    /// <summary>The fewest own Imps the keeper must have before one is taken from work to scout.</summary>
    private const int MinImpsToScout = 3;

    private static int Chebyshev(TilePos a, TilePos b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    /// <summary>The Imp creation routine, rule R8 (owner direction, 2026-11-15; the plan has the reasons). One Create Imp
    /// per cast gap, on own floor nearest the dig mark nearest home, while: no fewer own Imps than the free ones
    /// (<see cref="CampaignPlayerOptions.FreeImps"/>: one is on its way) but fewer than the target (the larger of
    /// <see cref="PlayerStrategy.MinImps"/> and one per <see cref="MarkedTilesPerImp"/> marks, at most
    /// <see cref="PlayerStrategy.MaxImps"/>); the target is <see cref="PlayerStrategy.MinImps"/> while Create Imp waits
    /// for its upgrade (not upgraded, an own creature in an own Library within <see cref="ResearchMemoryTicks"/>, for at
    /// most <see cref="UpgradeWaitTicks"/>) unless the marks want twice the Imps there are; the price leaves
    /// <see cref="PlayerStrategy.ManaReserve"/>; it is at most <see cref="PlayerStrategy.ImpPriceSeconds"/> of net income;
    /// no enemy in sight within <see cref="ImpSafeTiles"/>. On by default; off when <see cref="PlayerStrategy.CreateImps"/> is 0.</summary>
    private bool CreateImp(PlayerObservation observation, TilePos home, List<PlayerAction> actions)
    {
        if (_strategy.CreateImps <= 0) return false;
        long now = observation.Stamp.Tick.Value;
        NoteResearch(observation);
        if (!Fits(actions, 1) || (observation.Offers.Actions & PlayerActionKinds.CreateImpAt) == 0
            || observation.Offers.CreateImpCost is not ManaAmount price
            || _lastImpTick is GameTick last && now - last.Value < ImpCastGapTicks) return false;
        bool upgraded = false;
        foreach (PlayerSpellOffer spell in observation.Offers.Spells)
            if (spell.Spell == CreateImpSpell)
            {
                if (!spell.Ready || !spell.Affordable) return false;
                upgraded = spell.Upgraded;
            }

        int imps = 0;
        foreach (PlayerCreatureSummary creature in observation.Creatures) if (_options.IsWorker(creature.Type)) imps++;
        // Below the free minimum a free Imp is on its way: wait for it (owner, 2026-11-15).
        if (imps < _options.FreeImps) { Note($"{imps} Imps: a free one is coming"); return false; }
        if (imps >= _strategy.MaxImps) { Note($"{imps} Imps: at the limit"); return false; }
        if ((long)observation.Mana.Value - price.Value < _strategy.ManaReserve) { Note($"price {price.Value} would break the mana reserve"); return false; }
        long net = (long)observation.ManaIncome.Value - observation.ManaUpkeep.Value;
        if (net <= 0 || price.Value > net * _strategy.ImpPriceSeconds) { Note($"price {price.Value} too dear for net mana {net}/s"); return false; }
        PlayerTileGrid grid = observation.Tiles;
        int marked = 0;
        TilePos? nearestMark = null;
        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                var at = new TilePos(x, y);
                if ((grid[at].Flags & PlayerTileFlags.Tagged) == 0) continue;
                marked++;
                if (nearestMark is not TilePos m || PlayerActionRoutines.DistanceSquared(at, home) < PlayerActionRoutines.DistanceSquared(m, home))
                    nearestMark = at;
            }
        int need = (marked + MarkedTilesPerImp - 1) / MarkedTilesPerImp;
        bool waiting = !upgraded && _lastResearchAt is GameTick seen && now - seen.Value < ResearchMemoryTicks;
        // The Library upgrades only once nothing is left to research (0x004C3000): the wait is bounded, never endless.
        if (!waiting) _upgradeWaitSince = null;
        else if ((_upgradeWaitSince ??= observation.Stamp.Tick) is GameTick began && now - began.Value >= UpgradeWaitTicks) waiting = false;
        int target = waiting && need < 2 * Math.Max(imps, 1) ? _strategy.MinImps : Math.Max(_strategy.MinImps, need);
        if (imps >= Math.Min(target, _strategy.MaxImps)) { Note($"{imps} Imps meet the target {target} ({marked} marks)"); return false; }

        if (PlayerActionRoutines.NearestOwnFloor(observation, nearestMark ?? home, _refusedImpTiles) is not TilePos tile) return false;
        // Under attack the mana is for spells: an Imp born beside an enemy dies, its price falls back, and the next is
        // bought into the same fight (Level 6a, 2026-11-15: 50 casts in the last 900 ticks of a lost keeper).
        foreach (PlayerVisibleThing thing in observation.Things)
            if (PlayerActionRoutines.IsThreat(observation, thing) && Chebyshev(thing.Tile, tile) <= ImpSafeTiles)
            {
                Note($"enemy near the birth tile {tile}");
                return false;
            }
        Note($"create an Imp at {tile} ({imps} of target {target})");
        actions.Add(new PlayerAction.CreateImpAt(tile));
        _lastImpTick = observation.Stamp.Tick;
        return true;
    }

    /// <summary>The trap routine (defence plan D4): one trap blueprint per <see cref="TrapGapTicks"/> on the outermost
    /// free site (<see cref="PlayerTrapRoutines.Sites"/>) on the routes from where attackers come in (hero gates and
    /// portals, enemy ground, every enemy ever seen) to the Heart, while fewer than <see cref="PlayerStrategy.MaxTraps"/>
    /// own traps stand, keeping <see cref="PlayerStrategy.UseRoomMinGold"/> for rooms. The Workshop panel greys traps
    /// without a Workshop (<see cref="PlayerWorkshopOffer.NeedsWorkshop"/>), so with none it waits.</summary>
    private bool Trap(PlayerObservation observation, List<PlayerAction> actions)
    {
        if (_strategy.MaxTraps <= 0 || !Fits(actions, 1) || (observation.Offers.Actions & PlayerActionKinds.PlaceTrap) == 0) return false;
        long now = observation.Stamp.Tick.Value;
        if (_lastTrapTick is GameTick last && now - last.Value < TrapGapTicks) return false;
        // A placement accepted but never shown on its tile was refused by the world (blueprint-placement.md): drop the site.
        if (_placedTrap is (TilePos sent, GameTick when) && now - when.Value >= TrapGapTicks)
        {
            if (observation.Tiles.Contains(sent) && (observation.Tiles[sent].Flags & PlayerTileFlags.OwnTrap) == 0) _refusedTrapTiles.Add(sent);
            _placedTrap = null;
        }
        // A room bought in this decision spends the same gold the trap would be weighed against.
        foreach (PlayerAction queued in actions) if (queued is PlayerAction.BuildRoom) return false;
        if (Short(observation)) { Note($"keeping {GoldNeed(observation)} gold for payday"); return false; }
        bool buyable = false;
        foreach (PlayerWorkshopOffer offer in observation.Offers.Workshop)
            if (offer.Kind == PlayerWorkshopKind.Trap && !offer.NeedsWorkshop && offer.Affordable
                && (long)observation.Gold.Value - offer.Cost.Value >= _strategy.UseRoomMinGold) buyable = true;
        if (!buyable) { Note($"no trap affordable while keeping {_strategy.UseRoomMinGold} gold (or no Workshop)"); return false; }
        _lastTrapTick = observation.Stamp.Tick;   // weighed now: look again after the gap, placed or not
        PlayerTileGrid grid = observation.Tiles;
        if (PlayerTrapRoutines.OwnTraps(grid) >= _strategy.MaxTraps) { Note($"at the trap limit {_strategy.MaxTraps}"); return false; }
        PlayerId self = observation.Stamp.Player;
        var entries = new List<TilePos>(_sightings);
        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                var at = new TilePos(x, y);
                PlayerTile tile = grid[at];
                if (PlayerTrapRoutines.IsEntryRoom(tile, self) || PlayerTrapRoutines.IsEnemyGround(tile, self)) entries.Add(at);
            }
        if (entries.Count == 0) { Note("no entry seen to guard"); return false; }
        entries.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));   // _sightings is a set: fix the order
        foreach (TilePos site in PlayerTrapRoutines.Sites(observation, entries, TrapSpacingTiles, _refusedTrapTiles))
        {
            // A blueprint under an enemy's feet is only carried into the fight.
            if (PlayerActionRoutines.ThreatNear(observation, site, 1)) continue;
            if (PlayerTrapRoutines.Choose(observation, site, _strategy.UseRoomMinGold) is not PlayerWorkshopOffer trap) continue;
            actions.Add(new PlayerAction.PlaceTrap(trap.Id, site));
            _placedTrap = (site, observation.Stamp.Tick);
            Note($"place {trap.Name} at {site} ({PlayerTrapRoutines.SiteKind(observation.Tiles, site)})");
            return true;
        }
        Note($"no free trap site on the routes from {entries.Count} entries");
        return false;
    }

    private GameTick? _lastTrapTick;
    private (TilePos Tile, GameTick Since)? _placedTrap;
    /// <summary>Tiles a trap placement was refused at: not chosen again.</summary>
    private readonly HashSet<TilePos> _refusedTrapTiles = [];

    /// <summary>Ticks between two trap placements (5 s): the blueprint shows on its tile before the next is weighed.</summary>
    private const int TrapGapTicks = 20;

    /// <summary>Own traps, doors and new sites stand at least this many tiles (Chebyshev) apart (defence plan D4, as DK2's
    /// own keeper spaces them).</summary>
    private const int TrapSpacingTiles = 3;

    /// <summary>Remembers when an own non-worker last stood in an own Library: research is going on.</summary>
    private void NoteResearch(PlayerObservation observation)
    {
        PlayerTileGrid grid = observation.Tiles;
        foreach (PlayerCreatureSummary creature in observation.Creatures)
            if (creature.Tile is TilePos at && !_options.IsWorker(creature.Type) && grid.Contains(at)
                && grid[at].Room == RoomTypeId.Library && grid[at].Owner == observation.Stamp.Player)
            {
                _lastResearchAt = observation.Stamp.Tick;
                return;
            }
    }

    private GameTick? _lastImpTick, _lastResearchAt, _upgradeWaitSince;
    /// <summary>Tiles a Create Imp cast was refused at (the spell's place gates): not chosen again.</summary>
    private readonly HashSet<TilePos> _refusedImpTiles = [];

    /// <summary>The longest (ticks) Imp buying waits for Create Imp's upgrade: 20 game minutes.</summary>
    private const int UpgradeWaitTicks = 4800;

    /// <summary>Create Imp's spell id on the Spell panel and in the cast packets.</summary>
    private const byte CreateImpSpell = 4;

    /// <summary>Ticks between two Create Imp casts: the new Imp shows, and the price rises, before the next is weighed.</summary>
    private const int ImpCastGapTicks = 8;

    /// <summary>No Imp is created with an enemy in sight this near (tiles) its birth tile.</summary>
    private const int ImpSafeTiles = 10;

    /// <summary>R8's dig need: one Imp per this many marked tiles.</summary>
    private const int MarkedTilesPerImp = 3;

    /// <summary>How long (ticks) after a creature was last seen in the Library research still counts as going on: 5 game minutes.</summary>
    private const int ResearchMemoryTicks = 1200;

    /// <summary>How long (ticks) a scouting Imp stays to look before it is lifted again: 2 game seconds. The fog
    /// reveals its sight on the first tick it stands (FogOfWarSystem runs every tick), and the owner recalls an Imp
    /// left on dirt path standing a second or two before it walks back to own ground.</summary>
    private const int ScoutDwellTicks = 8;

    /// <summary>How near (tiles) an enemy may come to a scouting Imp, or its target, before it is kept away or lifted.</summary>
    private const int ScoutDangerTiles = 8;

    /// <summary>Each Pit floor tile a beaten fighter lies on: when it was first and last seen there.</summary>
    private readonly Dictionary<TilePos, (GameTick First, GameTick Seen)> _sinking = [];
    private GameTick? _lastRescueTick;

    /// <summary>How long (ticks) a beaten fighter may lie on the Pit's floor before an Imp is sent to it there. An estimate:
    /// state 217 (0x004599D0, combat-pit.md) waits ten turns, then sinks and rises 0x200 a turn; the collapse before it
    /// is unread, so 40 leaves room.</summary>
    private const int RiseTicks = 40;

    /// <summary>A floor body unseen for longer than this (ticks) is timed afresh when seen again.</summary>
    private const int SinkSeenTicks = 12;

    /// <summary>Ticks between two rescues: a carried body moves tile by tile and must not draw a second Imp.</summary>
    private const int RescueGapTicks = 40;

    /// <summary>How long (ticks) after sending an Imp to a collapsed creature another is not sent to the same tile.</summary>
    private const int RescueTicks = 400;

    /// <summary>Heal's spell id, as the Spell panel and the cast packets carry it (definition data, KEEPERSPELLS).</summary>
    private const byte HealSpell = 10;

    /// <summary>Heal on the Spell panel, payable and recharged (Task 4's offers).</summary>
    private static bool HealOffered(PlayerObservation observation)
    {
        if ((observation.Offers.Actions & PlayerActionKinds.CastSpell) == 0) return false;
        foreach (PlayerSpellOffer spell in observation.Offers.Spells)
            if (spell.Spell == HealSpell) return spell.OnCreature && spell.Affordable && spell.Ready;
        return false;
    }

    /// <summary>Lifts <paramref name="creature"/> toward <paramref name="room"/>'s tile nearest home, or toward
    /// home's floor without one; false when the hand cannot lift it.</summary>
    private bool Lift(PlayerObservation observation, PlayerVisibleThing creature, RoomTypeId room, TilePos home, List<PlayerAction> actions)
    {
        if ((creature.Actions & PlayerThingActions.PickUp) == 0 || (observation.Offers.Actions & PlayerActionKinds.PickUp) == 0) return false;
        actions.Add(new PlayerAction.PickUp(creature.Handle));
        _carryTo = PlayerActionRoutines.NearestRoomTile(observation, room, home, _refusedWork);
        return true;
    }

    /// <summary>Dig marks up to the outstanding bound, skipping abandoned tiles.</summary>
    private bool Dig(PlayerObservation observation, TilePos home, List<PlayerAction> actions)
    {
        if (_strategy.Dig == DigMode.Off || (observation.Offers.Actions & PlayerActionKinds.TagArea) == 0) return false;
        // A room site's marks are its own (Task 3a): they do not hold the frontier back.
        int room = _strategy.MaxOutstandingTags - Outstanding(observation) + SiteMarks(observation);
        int added = 0, gold = 0, free = room;
        TilePos from = _strategy.Dig == DigMode.Toward && _strategy.DigTarget is TilePos target ? target : home;
        PlayerTileGrid grid = observation.Tiles;
        // A room to take (R66) comes first: the frontier nearest it, held-back ground included, since a room another
        // player owns stands on its ground (Level 5's hero Prison was owned only at tick 4044, after the raids).
        int took = 0;
        TilePos? take = TakeTarget(observation, home);
        if (take is TilePos toward)
            foreach (TilePos tile in PlayerActionRoutines.Frontier(observation, toward, preferGold: false, _abandoned, breach: true, avoid: _covered))
            {
                if (took >= TakeMarks || room-- <= 0 || !Fits(actions, 1)) break;
                actions.Add(new PlayerAction.TagArea(tile, tile));
                added++;
                took++;
                if ((grid[tile].Flags & PlayerTileFlags.GoldSeam) != 0) gold++;
            }
        foreach (TilePos tile in Frontier(observation, from, preferGold: _strategy.Dig == DigMode.GoldFirst))
        {
            if (room <= 0 || !Fits(actions, 1)) break;
            if (actions.Contains(new PlayerAction.TagArea(tile, tile))) continue;
            room--;
            actions.Add(new PlayerAction.TagArea(tile, tile));
            added++;
            if ((grid[tile].Flags & PlayerTileFlags.GoldSeam) != 0) gold++;
        }
        string takeNote = took > 0 ? $", {took} toward the room to take at {take}" : "";
        Note(added > 0 ? $"mark {added} tiles ({gold} gold{takeNote}), {_strategy.Dig}"
            : free <= 0 ? $"{Outstanding(observation)} marks outstanding, at the limit" : "no frontier tile to mark");
        return added > 0;
    }

    /// <summary>Marks a decision gives the way to a room to take (R66), the rest going to the ordinary frontier.</summary>
    private const int TakeMarks = 4;

    /// <summary>The room tile to take (R66) nearest home: a tile of a <see cref="PlayerStrategy.TakeRooms"/> room in view
    /// that is not the player's, or a <see cref="PlayerStrategy.TakeAt"/> tile not yet seen as the player's own; none once
    /// open floor joins it to own floor (the Imps claim their way to a room they can walk to). Null without one.</summary>
    private TilePos? TakeTarget(PlayerObservation observation, TilePos home)
    {
        PlayerTileGrid grid = observation.Tiles;
        PlayerId self = observation.Stamp.Player;
        if (_strategy.TakeAt.Count == 0 && _strategy.TakeRooms.Count == 0) return null;
        HashSet<TilePos> reached = WalkableFromOwnFloor(grid, self);
        TilePos? best = null;
        long bestDistance = long.MaxValue;
        foreach (TilePos at in _strategy.TakeAt) Consider(at);
        if (_strategy.TakeRooms.Count > 0)
            for (int y = 0; y < grid.Height; y++)
                for (int x = 0; x < grid.Width; x++)
                {
                    var at = new TilePos(x, y);
                    PlayerTile tile = grid[at];
                    if (tile.Room != default && tile.Owner != self && _strategy.TakeRooms.Contains(tile.Room)) Consider(at);
                }
        return best;

        void Consider(TilePos at)
        {
            if (!grid.Contains(at)) return;
            PlayerTile tile = grid[at];
            if (tile.IsKnown && tile.Owner == self || reached.Contains(at) || PlayerActionRoutines.BesideOwnFloor(grid, at, self)) return;
            long distance = PlayerActionRoutines.DistanceSquared(at, home);
            if (distance < bestDistance) { best = at; bestDistance = distance; }
        }
    }

    /// <summary>Known open tiles (not solid, not water or lava) joined to own floor by such tiles, own floor included.</summary>
    private static HashSet<TilePos> WalkableFromOwnFloor(PlayerTileGrid grid, PlayerId self)
    {
        var reached = new HashSet<TilePos>();
        var queue = new Queue<TilePos>();
        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                var at = new TilePos(x, y);
                if (Open(grid[at]) && grid[at].Owner == self && reached.Add(at)) queue.Enqueue(at);
            }
        while (queue.Count > 0)
        {
            TilePos at = queue.Dequeue();
            foreach (TilePos next in (ReadOnlySpan<TilePos>)[new(at.X + 1, at.Y), new(at.X - 1, at.Y), new(at.X, at.Y + 1), new(at.X, at.Y - 1)])
                if (grid.Contains(next) && Open(grid[next]) && reached.Add(next)) queue.Enqueue(next);
        }
        return reached;

        static bool Open(PlayerTile tile) => tile.IsKnown
            && (tile.Flags & (PlayerTileFlags.Solid | PlayerTileFlags.Water | PlayerTileFlags.Lava)) == 0;
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
            RefreshNoDrop();
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
        // A tile in an enemy trap's reach is marked only once nothing else is left (A2): the Imps that dig and claim it
        // are shot at, so the dungeon grows elsewhere first, and the assault clears the trap once its reach touches a mark.
        ICollection<TilePos> skip = _abandoned;
        if (_covered.Count > 0)
        {
            _digSkip.Clear();
            _digSkip.UnionWith(_abandoned);
            _digSkip.UnionWith(_covered);
            skip = _digSkip;
        }
        if (_strategy.Breach == BreachMode.Always)
        {
            List<TilePos> clear = PlayerActionRoutines.Frontier(observation, from, preferGold, skip);
            return clear.Count > 0 || _covered.Count == 0 ? clear : PlayerActionRoutines.Frontier(observation, from, preferGold, _abandoned);
        }
        EnsureClearance();
        List<TilePos> safe = PlayerActionRoutines.Frontier(observation, from, preferGold, skip, breach: false, avoid: _danger, tagged: true);
        // Develop first: the breach waits until no safe tile is left, marked or not.
        if (safe.Count == 0 && _strategy.Breach == BreachMode.WhenDeveloped) return PlayerActionRoutines.Frontier(observation, from, preferGold, _abandoned);
        safe.RemoveAll(t => (observation.Tiles[t].Flags & PlayerTileFlags.Tagged) != 0);
        return safe;
    }

    /// <summary>Rebuilds the tiles near enemy sightings when the strategy's clearance changed.</summary>
    private void EnsureClearance()
    {
        if (_dangerRadius == _strategy.EnemyClearanceTiles) return;
        _danger.Clear();
        _dangerRadius = _strategy.EnemyClearanceTiles;
        foreach (TilePos seen in _sightings) AddClearance(seen, _dangerRadius);
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

    /// <summary>The open room site's tiles that still carry a dig mark.</summary>
    private int SiteMarks(PlayerObservation observation)
    {
        if (_site is not Site site) return 0;
        int marks = 0;
        for (int dy = 0; dy < site.Side; dy++)
            for (int dx = 0; dx < site.Side; dx++)
            {
                var at = new TilePos(site.Corner.X + dx, site.Corner.Y + dy);
                if (observation.Tiles.Contains(at) && (observation.Tiles[at].Flags & PlayerTileFlags.Tagged) != 0) marks++;
            }
        return marks;
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
