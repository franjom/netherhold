// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using System.Text;
using DK2.AI.Players;
using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Combat;
using DK2.Simulation.Players;

namespace DK2.Harness;

/// <summary>One ground-truth sample of the controlled player's dungeon, read from the world (privileged).</summary>
public sealed record PlayerMetricSample(int Tick, int Gold, int Mana, int Fighters, int Imps, int Hungry, int Unhappy, int Angry,
    int LairBeds, int LairUsed, int HatcheryTiles, int Chickens, int TreasuryTiles, int WageBill, int TicksToPayday,
    int EnemyCreatures, int EnemyTraps, int OwnTraps, int Losses, int Kills);

/// <summary>An own creature that died or left: what it was, where, and what was near (privileged). <paramref name="Source"/>
/// is the player that last changed its health; <paramref name="TrapTiles"/> and <paramref name="EnemyTiles"/> the
/// Chebyshev distance to the nearest enemy trap and enemy creature (-1: none).</summary>
public sealed record PlayerCreatureLoss(int Tick, int Id, string Type, int Level, TilePos Tile, string Cause, int Source,
    int TrapTiles, string? TrapType, int EnemyTiles, string Grievance = "");

/// <summary>
/// The host's privileged diagnostics of a player run: what was really there, apart from what the controller saw.
/// Samples the controlled player's dungeon every <see cref="SampleEveryTicks"/>, records every own creature lost with
/// what was near it, and every enemy trap's firing count. It only reads the world: no hash, random stream or command
/// is touched. The tables go beside the run's other evidence (<see cref="Write"/>).
/// </summary>
public sealed class PlayerRunDiagnostics
{
    public const string MetricsFile = "player-metrics.tsv", LossesFile = "player-losses.tsv", TrapsFile = "player-enemy-traps.tsv",
        ThinkingFile = "player-thinking.tsv", BeliefsFile = "player-beliefs.tsv", MapFile = "player-map.txt";

    /// <summary>Ticks between two creature scans: a death is seen while the corpse lies (far longer than this).</summary>
    private const int ScanEveryTicks = 4;

    private readonly GameDatabase _database;
    private readonly PlayerId _player;
    private readonly Dictionary<int, (CreatureState Creature, TilePos Tile)> _alive = [];
    private readonly HashSet<int> _enemiesSeen = [];
    private readonly List<PlayerMetricSample> _samples = [];
    private readonly List<PlayerCreatureLoss> _losses = [];
    private readonly Dictionary<int, EnemyTrap> _enemyTraps = [];

    /// <summary>An enemy trap as last seen: <see cref="Fires"/> counts the times its recharge moved forward (each a firing).</summary>
    private sealed class EnemyTrap
    {
        public EnemyTrap(string type, byte owner, TilePos tile)
        {
            Type = type;
            Owner = owner;
            Tile = tile;
        }

        public string Type { get; }
        public byte Owner { get; }
        public TilePos Tile { get; }
        public long RechargeAt;
        public int Fires, Health;
        public bool Hidden, Gone;
    }
    private int _kills;

    public PlayerRunDiagnostics(GameDatabase database, PlayerId player, int sampleEveryTicks = 100)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _player = player;
        if (sampleEveryTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleEveryTicks));
        }

        SampleEveryTicks = sampleEveryTicks;
    }

    public int SampleEveryTicks { get; }
    public IReadOnlyList<PlayerMetricSample> Samples
    {
        get
        {
            return _samples;
        }
    }

    public IReadOnlyList<PlayerCreatureLoss> Losses
    {
        get
        {
            return _losses;
        }
    }

    public int Kills
    {
        get
        {
            return _kills;
        }
    }

    /// <summary>Called after every completed tick.</summary>
    public void AfterTick(WorldState world, GameTick tick)
    {
        if (tick.Value % ScanEveryTicks == 0)
        {
            Scan(world, tick);
        }

        if (tick.Value % SampleEveryTicks == 0)
        {
            Sample(world, tick);
        }
    }

    private void Scan(WorldState world, GameTick tick)
    {
        foreach (EnemyTrap known in _enemyTraps.Values)
        {
            known.Gone = true;
        }

        foreach (TrapState trap in world.Traps)
        {
            if (!Hostile(world, new PlayerId(trap.PlayerId)) || trap.UnderConstruction)
            {
                continue;
            }

            if (!_enemyTraps.TryGetValue(trap.Id, out EnemyTrap? known))
            {
                _enemyTraps[trap.Id] = known = new EnemyTrap(TrapName(trap.TrapId), trap.PlayerId, new TilePos(trap.X, trap.Y)) { RechargeAt = trap.RechargeAt };
            }
            else if (trap.RechargeAt > known.RechargeAt) { known.Fires++; known.RechargeAt = trap.RechargeAt; }
            known.Health = trap.Health;
            known.Hidden = trap.Hidden;
            known.Gone = false;
        }
        var seen = new HashSet<int>();
        foreach (CreatureState creature in world.Creatures)
        {
            // A collapsed creature may still be carried to its bed: a loss or a kill counts once it is dead (or gone).
            bool dead = creature.Activity == CreatureActivity.Dead;
            if (creature.OwnerPlayerId == _player.Value)
            {
                if (dead)
                {
                    continue;
                }

                seen.Add(creature.Id);
                _alive[creature.Id] = (creature, creature.Tile);
            }
            else if (Hostile(world, creature.OwnerPlayerId))
            {
                // An enemy counts as killed once, when it dies with the controlled player last to hurt it.
                if (dead && creature.HealthSourcePlayerId == _player.Value && _enemiesSeen.Remove(creature.Id))
                {
                    _kills++;
                }
                else if (!dead)
                {
                    _enemiesSeen.Add(creature.Id);
                }
            }
        }
        List<int>? gone = null;
        foreach ((int id, (CreatureState creature, TilePos tile)) in _alive)
        {
            if (!seen.Contains(id))
            {
                (gone ??= []).Add(id);
            }
        }

        if (gone is null)
        {
            return;
        }

        foreach (int id in gone)
        {
            (CreatureState creature, TilePos tile) = _alive[id];
            _alive.Remove(id);
            CreatureState? now = world.CreatureById(id);
            bool died = now is null ? creature.Health <= 0 : now.Activity == CreatureActivity.Dead;
            string cause = !died ? (now is not null && now.OwnerPlayerId != _player.Value ? "converted" : "left")
                : creature.HealthSourcePlayerId == _player.Value || creature.HealthSourcePlayerId == 0 ? "died" : "killed";
            (int trapTiles, string? trapType) = NearestEnemyTrap(world, tile);
            // Its largest anger counter when last seen (what its flower would show as the grievance).
            CreatureAnger anger = (now ?? creature).Anger;
            string grievance = anger.IsCalm ? "None" : StatusFlowerCentre.LargestAnger(anger).ToString();
            _losses.Add(new PlayerCreatureLoss((int)tick.Value, id, _database.CreatureById(creature.CreatureTypeId).Name, creature.Level, tile, cause,
                (now ?? creature).HealthSourcePlayerId, trapTiles, trapType, NearestEnemy(world, tile), grievance));
        }
    }

    private (int Tiles, string? Type) NearestEnemyTrap(WorldState world, TilePos at)
    {
        int best = -1;
        string? type = null;
        foreach (TrapState trap in world.Traps)
        {
            if (!Hostile(world, new PlayerId(trap.PlayerId)) || trap.UnderConstruction)
            {
                continue;
            }

            int d = Math.Max(Math.Abs(trap.X - at.X), Math.Abs(trap.Y - at.Y));
            if (best < 0 || d < best) { best = d; type = TrapName(trap.TrapId); }
        }
        return (best, type);
    }

    private int NearestEnemy(WorldState world, TilePos at)
    {
        int best = -1;
        foreach (CreatureState creature in world.Creatures)
        {
            if (!Hostile(world, creature.OwnerPlayerId) || creature.Activity == CreatureActivity.Dead)
            {
                continue;
            }

            TilePos tile = creature.Tile;
            int d = Math.Max(Math.Abs(tile.X - at.X), Math.Abs(tile.Y - at.Y));
            if (best < 0 || d < best)
            {
                best = d;
            }
        }
        return best;
    }

    /// <summary>Whether <paramref name="owner"/> is the controlled player's enemy: owned, and not allied
    /// (<c>CombatRules.IsHostile</c>).</summary>
    private bool Hostile(WorldState world, PlayerId owner)
    {
        return owner != PlayerIds.Unowned && owner != _player && CombatRules.IsHostile(world, _player.Value, owner.Value);
    }

    private string TrapName(byte id)
    {
        return _database.Traps.TryGetValue(id, out TrapDef? trap) ? trap.Name : $"trap-{id}";
    }

    private void Sample(WorldState world, GameTick tick)
    {
        int fighters = 0, imps = 0, hungry = 0, unhappy = 0, angry = 0;
        foreach (CreatureState creature in world.Creatures)
        {
            if (creature.OwnerPlayerId != _player.Value || creature.Activity == CreatureActivity.Dead)
            {
                continue;
            }

            if ((_database.CreatureById(creature.CreatureTypeId).Flags & CreatureFlags.IsWorker) == CreatureFlags.IsWorker)
            {
                imps++;
            }
            else
            {
                fighters++;
            }

            if (creature.Anger.Hunger > 0)
            {
                hungry++;
            }

            if (creature.Mood == CreatureMood.Unhappy)
            {
                unhappy++;
            }
            else if (creature.Mood == CreatureMood.Angry)
            {
                angry++;
            }
        }
        int beds = 0, used = 0, hatchery = 0, treasury = 0;
        foreach (RoomInstance room in RoomIndex.Build(world, _database))
        {
            if (room.OwnerPlayerId != _player.Value)
            {
                continue;
            }

            if (LairRules.IsLairRoom(_database, room)) { beds += LairRules.Capacity(room); used += LairRules.Occupied(world, _database, room); }
            else if (room.Definition.TypeId == RoomTypeId.Hatchery)
            {
                hatchery += room.TileCount;
            }
            else if (room.Definition.TypeId == RoomTypeId.Treasury)
            {
                treasury += room.TileCount;
            }
        }
        int chickens = 0;
        foreach (ObjectState thing in world.Objects)
        {
            if (thing.ObjectTypeId == HatcheryRules.ChickenObjectId && thing.OwnerPlayerId == _player.Value)
            {
                chickens++;
            }
        }

        int enemyCreatures = 0;
        foreach (CreatureState creature in world.Creatures)
        {
            if (Hostile(world, creature.OwnerPlayerId) && creature.Activity != CreatureActivity.Dead)
            {
                enemyCreatures++;
            }
        }

        int enemyTraps = 0, ownTraps = 0;
        foreach (TrapState trap in world.Traps)
        {
            if (trap.UnderConstruction)
            {
                continue;
            }

            if (trap.PlayerId == _player.Value) { ownTraps++; continue; }
            enemyTraps++;
        }
        ref readonly PlayerState keeper = ref world.Player(_player);
        _samples.Add(new PlayerMetricSample((int)tick.Value, GoldStore.Total(world, _player), keeper.Mana, fighters, imps, hungry, unhappy, angry,
            beds, used, hatchery, chickens, treasury, CasinoJackpotRules.WageBill(world, _database, _player),
            (int)Math.Max(0, keeper.NextPaydayTick.Value - tick.Value), enemyCreatures, enemyTraps, ownTraps, _losses.Count, _kills));
    }

    /// <summary>Writes the diagnostics and the controller's own account (<paramref name="decisions"/>) into
    /// <paramref name="directory"/>. Thoughts are written when a routine's verdict changes; beliefs when a value does.</summary>
    public void Write(string directory, IReadOnlyList<PlayerDecisionRecord> decisions)
    {
        CultureInfo inv = CultureInfo.InvariantCulture;
        var metrics = new StringBuilder("tick\tgold\tmana\tfighters\timps\thungry\tunhappy\tangry\tlair_beds\tlair_used\thatchery_tiles\tchickens\t" +
            "treasury_tiles\twage_bill\tticks_to_payday\tenemy_creatures\tenemy_traps\town_traps\tlosses\tkills\n");
        foreach (PlayerMetricSample s in _samples)
        {
            metrics.Append(inv, $"{s.Tick}\t{s.Gold}\t{s.Mana}\t{s.Fighters}\t{s.Imps}\t{s.Hungry}\t{s.Unhappy}\t{s.Angry}\t{s.LairBeds}\t{s.LairUsed}\t" +
                $"{s.HatcheryTiles}\t{s.Chickens}\t{s.TreasuryTiles}\t{s.WageBill}\t{s.TicksToPayday}\t{s.EnemyCreatures}\t{s.EnemyTraps}\t{s.OwnTraps}\t{s.Losses}\t{s.Kills}\n");
        }

        PlayerRunReport.WriteNew(Path.Combine(directory, MetricsFile), Encoding.UTF8.GetBytes(metrics.ToString()));

        var losses = new StringBuilder("tick\tid\ttype\tlevel\tx\ty\tcause\tsource_player\tenemy_trap_tiles\tenemy_trap\tenemy_tiles\tgrievance\n");
        foreach (PlayerCreatureLoss l in _losses)
        {
            losses.Append(inv, $"{l.Tick}\t{l.Id}\t{l.Type}\t{l.Level}\t{l.Tile.X}\t{l.Tile.Y}\t{l.Cause}\t{l.Source}\t{l.TrapTiles}\t{l.TrapType}\t{l.EnemyTiles}\t{l.Grievance}\n");
        }

        PlayerRunReport.WriteNew(Path.Combine(directory, LossesFile), Encoding.UTF8.GetBytes(losses.ToString()));

        var traps = new StringBuilder("id\ttype\towner\tx\ty\tfires\thealth\thidden\tdestroyed\n");
        foreach ((int id, EnemyTrap t) in _enemyTraps.OrderBy(t => t.Key))
        {
            traps.Append(inv, $"{id}\t{t.Type}\t{t.Owner}\t{t.Tile.X}\t{t.Tile.Y}\t{t.Fires}\t{t.Health}\t{(t.Hidden ? 1 : 0)}\t{(t.Gone ? 1 : 0)}\n");
        }

        PlayerRunReport.WriteNew(Path.Combine(directory, TrapsFile), Encoding.UTF8.GetBytes(traps.ToString()));

        var thinking = new StringBuilder("tick\troutine\tacted\tnote\n");
        var lastNote = new Dictionary<string, (bool Acted, string Note)>(StringComparer.Ordinal);
        var beliefs = new StringBuilder("tick\tname\tvalue\n");
        var lastValue = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (PlayerDecisionRecord d in decisions)
        {
            foreach (PlayerThought t in d.Thoughts)
            {
                // An action is always written; a verdict of doing nothing only when it changes.
                if (!t.Acted && lastNote.TryGetValue(t.Routine, out (bool Acted, string Note) was) && !was.Acted && was.Note == t.Note)
                {
                    continue;
                }

                lastNote[t.Routine] = (t.Acted, t.Note);
                thinking.Append(inv, $"{d.Tick.Value}\t{t.Routine}\t{(t.Acted ? 1 : 0)}\t{Tsv(t.Note)}\n");
            }
            foreach (PlayerBelief b in d.Beliefs)
            {
                if (lastValue.TryGetValue(b.Name, out long before) && before == b.Value)
                {
                    continue;
                }

                lastValue[b.Name] = b.Value;
                beliefs.Append(inv, $"{d.Tick.Value}\t{b.Name}\t{b.Value}\n");
            }
        }
        PlayerRunReport.WriteNew(Path.Combine(directory, ThinkingFile), Encoding.UTF8.GetBytes(thinking.ToString()));
        PlayerRunReport.WriteNew(Path.Combine(directory, BeliefsFile), Encoding.UTF8.GetBytes(beliefs.ToString()));
    }

    /// <summary>Draws <paramref name="seen"/>, the controller's last observation, as text (<see cref="MapFile"/>): what the
    /// player knew of the map when the run ended, nothing more. The file carries its own key (<see cref="MapKey"/>) and
    /// the room letters it used (never <c>R</c>, which is rock); a room is lower case when owned.</summary>
    public const string MapKey = "key: blank unknown, # rock to dig, $ gold or gems, R rock that cannot be dug, % own wall, " +
        "& another player's wall, ~ water, = lava, : unclaimed floor, . own floor, , another player's floor, " +
        "room letter (lower case own), ! visible enemy trap, @ own creature, * any other creature";

    public void WriteMap(string directory, PlayerObservation? seen)
    {
        if (seen is null)
        {
            return;
        }

        PlayerTileGrid grid = seen.Tiles;
        var letters = new Dictionary<RoomTypeId, char>();
        var legend = new StringBuilder();
        char next = 'A';
        char Letter(RoomTypeId room)
        {
            if (letters.TryGetValue(room, out char c))
            {
                return c;
            }

            if (next == 'R')
            {
                next++;
            }

            c = next <= 'Z' ? next++ : '?';
            letters[room] = c;
            legend.Append(CultureInfo.InvariantCulture, $"{c} {(_database.Rooms.TryGetValue(room, out RoomDef? def) ? def.Name : $"room-{(int)room}")}\n");
            return c;
        }
        char[][] rows = new char[grid.Height][];
        for (int y = 0; y < grid.Height; y++)
        {
            rows[y] = new char[grid.Width];
            for (int x = 0; x < grid.Width; x++)
            {
                PlayerTile t = grid[new TilePos(x, y)];
                PlayerTileFlags f = t.Flags;
                bool own = t.Owner == _player;
                rows[y][x] = !t.IsKnown ? ' '
                    : (f & PlayerTileFlags.GoldSeam) != 0 ? '$'
                    : (f & PlayerTileFlags.Solid) != 0 ? (own ? '%' : t.Owner != default ? '&' : (f & PlayerTileFlags.Diggable) != 0 ? '#' : 'R')
                    : (f & PlayerTileFlags.Water) != 0 ? '~'
                    : (f & PlayerTileFlags.Lava) != 0 ? '='
                    : t.Room != default ? (own ? char.ToLowerInvariant(Letter(t.Room)) : Letter(t.Room))
                    : own ? '.' : t.Owner == default ? ':' : ',';
            }
        }
        foreach (PlayerVisibleThing thing in seen.Things)
        {
            if (thing.Kind == PlayerThingKind.Creature && grid.Contains(thing.Tile))
            {
                rows[thing.Tile.Y][thing.Tile.X] = thing.Owner == _player ? '@' : '*';
            }
        }

        foreach (PlayerVisibleTrap trap in seen.EnemyTraps)
        {
            if (grid.Contains(trap.Tile))
            {
                rows[trap.Tile.Y][trap.Tile.X] = '!';
            }
        }

        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"tick {seen.Stamp.Tick.Value}, player {_player}, {grid.Width}x{grid.Height}; rows are y, columns x\n");
        text.Append(MapKey).Append('\n');
        text.Append(legend);
        text.Append("     ");
        for (int x = 0; x < grid.Width; x++)
        {
            text.Append(x % 10 == 0 ? (char)('0' + x / 10 % 10) : ' ');
        }

        text.Append("\n     ");
        for (int x = 0; x < grid.Width; x++)
        {
            text.Append((char)('0' + x % 10));
        }

        text.Append('\n');
        for (int y = 0; y < grid.Height; y++)
        {
            text.Append(CultureInfo.InvariantCulture, $"{y,4} ").Append(rows[y]).Append('\n');
        }

        PlayerRunReport.WriteNew(Path.Combine(directory, MapFile), Encoding.UTF8.GetBytes(text.ToString()));
    }

    /// <summary>The run's headline numbers, for the verdict file and for comparing runs.</summary>
    public object Summary(IReadOnlyList<PlayerDecisionRecord> decisions)
    {
        PlayerMetricSample? last = _samples.Count > 0 ? _samples[^1] : null;
        int peakFighters = 0, hungrySamples = 0, shortSamples = 0, idleGoldSamples = 0, bedsShortSamples = 0;
        foreach (PlayerMetricSample s in _samples)
        {
            peakFighters = Math.Max(peakFighters, s.Fighters);
            if (s.Hungry > 0)
            {
                hungrySamples++;
            }

            if (s.Gold < s.WageBill)
            {
                shortSamples++;
            }

            if (s.Gold >= 20000)
            {
                idleGoldSamples++;
            }

            if (s.LairUsed >= s.LairBeds && s.Fighters > 0)
            {
                bedsShortSamples++;
            }
        }
        var routines = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (PlayerDecisionRecord d in decisions)
        {
            foreach (PlayerThought t in d.Thoughts)
            {
                if (t.Acted)
                {
                    routines[t.Routine] = routines.GetValueOrDefault(t.Routine) + 1;
                }
            }
        }

        return new
        {
            samples = _samples.Count,
            sample_every_ticks = SampleEveryTicks,
            final = last,
            peak_fighters = peakFighters,
            kills = _kills,
            losses = _losses.GroupBy(l => l.Cause).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()),
            losses_near_enemy_traps = _losses.Count(l => l.Cause is "killed" or "died" && l.TrapTiles is >= 0 and <= 6),
            losses_by_type = _losses.GroupBy(l => l.Type).OrderByDescending(g => g.Count()).ToDictionary(g => g.Key, g => g.Count()),
            left_by_grievance = _losses.Where(l => l.Cause == "left").GroupBy(l => l.Grievance).OrderByDescending(g => g.Count())
                .ToDictionary(g => g.Key, g => g.Count()),
            samples_with_hungry = hungrySamples,
            samples_gold_below_wage_bill = shortSamples,
            samples_gold_idle_20k = idleGoldSamples,
            samples_lair_full = bedsShortSamples,
            enemy_traps_firing = _enemyTraps.Values.Where(t => t.Fires > 0).OrderByDescending(t => t.Fires)
                .Select(t => new { type = t.Type, owner = t.Owner, x = t.Tile.X, y = t.Tile.Y, fires = t.Fires, destroyed = t.Gone }),
            enemy_traps_destroyed = _enemyTraps.Values.Count(t => t.Gone),
            actions_by_routine = routines.OrderBy(r => r.Key, StringComparer.Ordinal).ToDictionary(r => r.Key, r => r.Value),
        };
    }

    private static string Tsv(string text)
    {
        return text.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
    }
}
