// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;

namespace DK2.AI;

/// <summary>The keeper's attack planner (<c>0x004FAEF0</c>, task 0's first step), the moving Call to
/// Arms front (<c>0x004FE9C0</c>) and attack management (<c>0x004FECE0</c>, step 7): an eligible
/// keeper picks an enemy, digs a tunnel from a start room to an end room through task 7 and raises
/// the call at the front through the same action 54 a human uses.</summary>
/// <remarks>
/// <para>Built: the gates, readiness with its level grants, both modes, the strength test, the
/// nearest-heart choice, the room choices, stored tunnels resumed with the attack wait, the staging
/// point, the front for every usage, the cast and usage 1's event; the research move is counted.</para>
/// <para>Deviates: a cast is a command a tick later, predicted from the captured permissions; the
/// keeper's own RNG; a room's first slab is its first tile and its list order the capture's
/// (ADR-0298).</para>
/// </remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/computer-keeper-management.md")]
public sealed class KeeperAttackPlanner
{
    /// <summary>The creep counts own non-workers strictly within ten tiles of the call.</summary>
    public const int CreepRadiusSquared = 0x64000;

    private readonly KeeperView _view;
    private readonly KeeperAIState _state;
    private readonly KeeperAIParameters _p;
    private readonly SimRandom _rng;
    private readonly List<PlayerCommand> _commands;
    private readonly KeeperReadiness _readiness;
    private readonly KeeperBattleStep _battle;
    private readonly long _turn;

    public KeeperAttackPlanner(KeeperView view, KeeperAIState state, KeeperAIParameters parameters, SimRandom rng,
        List<PlayerCommand> commands, KeeperReadiness readiness, KeeperBattleStep battle)
    {
        _view = view;
        _state = state;
        _p = parameters;
        _rng = rng;
        _commands = commands;
        _readiness = readiness;
        _battle = battle;
        _turn = view.Tick.Value + 1;
    }

    // ----------------------------------------------------------------- 0x004FAEF0

    /// <summary>Task 0's attack decision. True when a tunnel task was started; the planner's flag
    /// writes happen even when a later choice fails, as DK2's do.</summary>
    public bool Plan()
    {
        if (_p.NeverAttack || _state.CannotAttackCounter != 0 || _state.CallToArmsInUse) return false;
        bool ordinary = false;
        byte attacker = 0;
        int superiority = 0;
        bool ready = _readiness.ArmyReady(); // its grants run before the rooms test
        if (ready && (!_p.IAAllRoomsPlaced || _state.AllRoomsBuilt))
        {
            if ((_p.IAAllSpellsResearched & 1) != 0 && ResearchableSpellRemains())
            {
                // 0x004C6460(6) draws a random own Library; with one, 0x004FA8D0 frees a portal
                // slot, a move this build counts and does not make.
                if (RandomRoomOfType(_view.PlayerId, RoomTypeId.Library) is not null) _state.UnbuiltResearchMoves++;
                return false;
            }
            if (!_p.IAOnlyAttackAttackers) ordinary = true;
            else if (_state.HaveBeenAttacked)
            {
                attacker = _state.AttackingPlayerId;
                _state.HaveBeenAttacked = false;
                _state.AttackingPlayerId = 0;
            }
            superiority = _p.IAThreatSuperiority;
        }
        if (!ordinary) return attacker != 0 && AttackTheAttacker(attacker, superiority);
        return AttackTheNearest(superiority);
    }

    /// <summary>Spell ids 1 to the descriptor count at availability 2, the state the research
    /// chooser looks for.</summary>
    private bool ResearchableSpellRemains()
    {
        int count = _view.Database.KeeperSpells.Count;
        for (int id = 1; id <= count; id++)
            if (_view.Availability(KeeperSpellRules.SpellAvailabilityKind, id) == 2) return true;
        return false;
    }

    /// <summary>Attacker-only mode: the recorded attacker's stored tunnel is resumed without a wait
    /// once its tunnels are used up; otherwise its strength is met, the enemy non-bridge room
    /// nearest the own heart is the end and the own non-bridge room nearest that the start.</summary>
    private bool AttackTheAttacker(byte attacker, int superiority)
    {
        if (attacker >= _state.Breaches.Length) return false;
        KeeperBreachState breach = _state.Breaches[attacker];
        if (breach.TunnelsLeft == 0)
        {
            _state.AttackTarget = 0;
            return ResumeStoredTunnel(attacker, withWait: false);
        }
        _state.PlayersAttackedFlags |= (ushort)(1 << attacker);
        int own = TotalStrength(_view.PlayerId), enemy = TotalStrength(attacker);
        if (own < unchecked(enemy * superiority) / 100) return false;
        KeeperRoomView? end = NearestNonBridgeRoom(attacker, PointOf(HeartPosition(_view.PlayerId)));
        if (end is null) return false;
        BeginTunnel();
        TilePos endSlab = FirstSlab(end);
        _state.DigRoomAt = endSlab;
        // 0x004C6010 leaves its out-variable alone when nothing qualifies: the end room stands in.
        KeeperRoomView start = NearestNonBridgeRoom(_view.PlayerId, PointOf(endSlab)) ?? end;
        _state.DigRoomAtCurrent = Position(start);
        breach.TunnelsLeft--;
        _state.AttackTarget = attacker;
        if (_p.CallToArmsUsage != 0 || breach.TunnelsLeft != 0 || _state.CallToArmsInUse) return true;
        StageTheCall(start);
        return true;
    }

    /// <summary>Ordinary mode: the manager-list walk, a used-up enemy's stored tunnel resumed at once
    /// with the attack wait, else the nearest enemy heart whose army the keeper's outweighs.</summary>
    private bool AttackTheNearest(int superiority)
    {
        byte best = 0;
        int bestDistance = int.MaxValue;
        int own = TotalStrength(_view.PlayerId);
        TilePos ownHeart = HeartPosition(_view.PlayerId);
        foreach (byte player in _view.PlayerOrder)
        {
            if (_view.StatusOf(player) is PlayerStatus.Conquered or PlayerStatus.Lost) continue;
            if (player is PlayerIds.Good or PlayerIds.Neutral || _view.AreAllied(player)) continue;
            if (player >= _state.Breaches.Length) continue;
            if (_state.Breaches[player].TunnelsLeft == 0)
            {
                _state.AttackTarget = 0;
                if (ResumeStoredTunnel(player, withWait: true)) return true;
                continue;
            }
            if (unchecked(TotalStrength(player) * superiority) / 100 > own) continue;
            TilePos heart = HeartPosition(player);
            int distance = KeeperThreatResponder.ShiftedDistance(
                CreatureState.CentreOf(heart.X), CreatureState.CentreOf(heart.Y),
                CreatureState.CentreOf(ownHeart.X), CreatureState.CentreOf(ownHeart.Y));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = player;
            }
        }
        if (best == 0) return false;

        ushort bit = (ushort)(1 << best);
        _state.PlayersAttackedFlags |= bit;
        KeeperRoomView? end, start;
        if ((_state.FirstAttackFlag & bit) == 0)
        {
            _state.FirstAttackFlag |= bit;
            end = (_p.IAFirstEnd & 3) switch
            {
                0 => RandomRoomOfType(best, RoomTypeId.DungeonHeart),
                1 => RandomNonBridgeRoom(best),
                2 => RoomByOwnStrength(best, weakest: true),
                _ => RoomByOwnStrength(best, weakest: false),
            };
            if (end is null) return false;
            start = (_p.IAFirstStart & 3) switch
            {
                0 => FurthestOwnRoomFromHeart(),
                1 => RandomNonBridgeRoom(_view.PlayerId),
                2 => RandomRoomOfType(_view.PlayerId, RoomTypeId.DungeonHeart),
                _ => NearestNonBridgeRoom(_view.PlayerId, PointOf(FirstSlab(end))),
            };
        }
        else
        {
            start = RandomNonBridgeRoom(_view.PlayerId);
            end = RandomNonBridgeRoom(best);
        }
        if (start is null || end is null) return false;

        BeginTunnel();
        _state.DigRoomAt = Position(end);
        _state.DigRoomAtCurrent = Position(start);
        KeeperBreachState breach = _state.Breaches[best];
        breach.TunnelsLeft--;
        _state.AttackTarget = best;
        if (_p.CallToArmsUsage == 0 && breach.TunnelsLeft == 0 && !_state.CallToArmsInUse) StageTheCall(start);
        return true;
    }

    /// <summary>Task 7 with task 4 to follow, the clock reset and the wall hug off, as the planner's
    /// masked write does.</summary>
    private void BeginTunnel()
    {
        _state.BeginTask(KeeperTask.DigPath, KeeperTask.Done);
        _state.WallHug = false;
    }

    /// <summary>The first stored tunnel of an enemy becomes the task; the ordinary walk then sets the
    /// attack wait and clears it again when another of that enemy's tunnels remains.</summary>
    private bool ResumeStoredTunnel(byte player, bool withWait)
    {
        KeeperBreachState breach = _state.Breaches[player];
        for (int i = 0; i < KeeperBreachState.Slots; i++)
        {
            if (breach.Start[i].X == 0) continue;
            BeginTunnel();
            _state.DigRoomAt = breach.End[i];
            _state.DigRoomAtCurrent = breach.Start[i];
            breach.Start[i] = default;
            if (withWait)
            {
                _state.CannotAttackCounter = _p.WaitBeforeAttackingAgain << 2;
                if (breach.HasStoredTunnel) _state.CannotAttackCounter = 0;
            }
            return true;
        }
        return false;
    }

    /// <summary>Task 7's attack branch (<c>0x004FDC50</c>): the tunnel from the cursor to the target
    /// goes into the target enemy's first empty slot and the target is forgotten.</summary>
    public static void StoreTunnel(KeeperAIState state)
    {
        if (state.AttackTarget < state.Breaches.Length)
        {
            KeeperBreachState breach = state.Breaches[state.AttackTarget];
            for (int i = 0; i < KeeperBreachState.Slots; i++)
            {
                if (breach.Start[i].X != 0) continue;
                breach.End[i] = state.DigRoomAt;
                breach.Start[i] = state.DigRoomAtCurrent;
                break;
            }
        }
        state.AttackTarget = 0;
    }

    /// <summary>The staging point for a keeper that creeps its call (usage 0): two tiles off the
    /// heart both ways, if a walker routes there; else the start room's position; else the first
    /// of its slabs that routes, the last tried remaining when none does.</summary>
    private void StageTheCall(KeeperRoomView start)
    {
        TilePos heart = HeartPosition(_view.PlayerId);
        SetCall(new TilePos(heart.X + 2, heart.Y + 2));
        KeeperCreatureView? walker = RouteTester();
        if (walker is not KeeperCreatureView creature || !Routes(creature, _state.CallToArmsX, _state.CallToArmsY)) return;
        SetCall(_state.DigRoomAtCurrent);
        if (Routes(creature, _state.CallToArmsX, _state.CallToArmsY)) return;
        foreach (int index in start.TileIndices)
        {
            SetCall(_view.FromIndex(index));
            if (Routes(creature, _state.CallToArmsX, _state.CallToArmsY)) return;
        }
    }

    private void SetCall(TilePos tile)
    {
        _state.CallToArmsX = CreatureState.CentreOf(tile.X);
        _state.CallToArmsY = CreatureState.CentreOf(tile.Y);
    }

    // ----------------------------------------------------------------- 0x004FECE0 and 0x004FE9C0

    /// <summary>Step 7 while any player is attacked: with a nonzero-usage call up, only the removal
    /// test; else, with no attack wait, a front and the cast there, one allowance whether or not
    /// availability or the permission let the cast through. Usage 1 also queues half the army's
    /// threat for reinforcement at an owned drop site near the call.</summary>
    public int Manage(int allowance)
    {
        if (_state.CallToArmsInUse && _p.CallToArmsUsage != 0)
        {
            _battle.TestRemoval();
            return allowance;
        }
        if (_state.CannotAttackCounter != 0 || !FindFront()) return allowance;
        if (_view.Availability(KeeperSpellRules.SpellAvailabilityKind, CallToArmsRules.SpellId) == 3) TryCastTheCall();
        return allowance - 1;
    }

    /// <summary><c>0x004BBD30</c>'s permission, predicted: the row's cooldown, the cost at most the
    /// mana and the place validators at the call's tile; a passing cast sets the counter and the bit.</summary>
    private void TryCastTheCall()
    {
        KeeperSpellDef? spell = _view.Database.KeeperSpellById(CallToArmsRules.SpellId);
        if (spell is null || !KeeperBattleStep.Ready(_view, spell) || spell.ManaCost > _view.Mana) return;
        var tile = new TilePos(_state.CallToArmsX >> 12, _state.CallToArmsY >> 12);
        if (!_view.CallToArmsPlaceAllowed(tile)) return;
        _commands.Add(new CastSpellAtPlaceCommand(_view.PlayerId, CallToArmsRules.SpellId, _state.CallToArmsX, _state.CallToArmsY));
        _state.CallToArmsCounter = _p.RemoveCallToArmsAfter;
        _state.CallToArmsInUse = true;
        _state.CallToArmsCastTurn = _turn;
        if ((_p.CallToArmsUsage & 3) != 1 || _state.Responses.Count >= 3) return;
        // The room reader's own score, on owned navigation-open tiles within five (0x004FECE0).
        TilePos? site = ThreatEvaluator.DropSite(tile, roomMessage: true, _view.IsThreatDropCandidate);
        if (site is not TilePos drop) return;
        short half = unchecked((short)(TotalStrength(_view.PlayerId) / 2));
        _state.Responses.TryReceive(() => new KeeperThreatResponse(half, drop));
    }

    /// <summary>The front: for each attacked player in bit order, a point two tiles off its heart
    /// room's position in a random diagonal that an own walker routes to. Usage 1 and 2 store it;
    /// usage 0 keeps the position until a call is up, then creeps it to the route's first gate
    /// midpoint toward the point once the army has gathered.</summary>
    public bool FindFront()
    {
        if (_state.PlayersAttackedFlags == 0) return false;
        for (int player = 0; player < 16; player++)
        {
            ushort bit = (ushort)(1 << player);
            if ((_state.PlayersAttackedFlags & bit) == 0) continue;
            // DK2 tests the player slot's address, never null, so an attacked bit is never
            // cleared; a player the world lacks simply has no Heart room to draw.
            KeeperRoomView? heart = RandomRoomOfType((byte)player, RoomTypeId.DungeonHeart);
            if (heart is null) continue;
            int rx = _rng.NextInt(2), ry = _rng.NextInt(2);
            TilePos position = Position(heart);
            int targetX = CreatureState.CentreOf(position.X + (rx != 0 ? -2 : 2));
            int targetY = CreatureState.CentreOf(position.Y + (ry != 0 ? -2 : 2));
            if (RouteTester() is not KeeperCreatureView walker || !Routes(walker, targetX, targetY)) continue;
            if (_p.CallToArmsUsage != 0)
            {
                _state.CallToArmsX = targetX;
                _state.CallToArmsY = targetY;
                return true;
            }
            if (!_state.CallToArmsInUse) return true;
            int total = 0, nearby = 0;
            foreach (KeeperCreatureView creature in Owned(_view.PlayerId))
            {
                if (IsWorker(creature)) continue;
                total++;
                if ((uint)KeeperThreatResponder.ShiftedDistance(creature.PosX, creature.PosY, _state.CallToArmsX, _state.CallToArmsY) < CreepRadiusSquared)
                    nearby++;
            }
            var points = new List<Fixed16Vector>();
            bool routed = _view.CanRoute(_state.CallToArmsX, _state.CallToArmsY, targetX, targetY,
                WorldSteering.ProfileOf(_view.Database.CreatureById(walker.CreatureTypeId), _view.PlayerId, 0), points);
            if (routed && points.Count > 0 && total <= (nearby * 5) / 4)
            {
                _state.CallToArmsX = points[0].X >> 4;
                _state.CallToArmsY = points[0].Y >> 4;
                return true;
            }
        }
        return false;
    }

    // ----------------------------------------------------------------- helpers

    /// <summary>The route tests' creature: the first own creature that walks neither water nor
    /// lava, else the list head; none without creatures.</summary>
    private KeeperCreatureView? RouteTester()
    {
        KeeperCreatureView? head = null;
        foreach (KeeperCreatureView creature in Owned(_view.PlayerId))
        {
            head ??= creature;
            CreatureFlags flags = _view.Database.CreatureById(creature.CreatureTypeId).Flags;
            if (!flags.HasFlag(CreatureFlags.CanWalkOnWater) || !flags.HasFlag(CreatureFlags.CanWalkOnLava)) return creature;
        }
        return head;
    }

    /// <summary><c>0x004D0E60</c> with option 1: the creature's own water and lava flags, no door
    /// bits, from its position to the point.</summary>
    private bool Routes(KeeperCreatureView creature, int x, int y) =>
        _view.CanRoute(creature.PosX, creature.PosY, x, y,
            WorldSteering.ProfileOf(_view.Database.CreatureById(creature.CreatureTypeId), _view.PlayerId, 0));

    /// <summary>The whole owned list's level-scaled threat, workers and held creatures included; an
    /// own creature granted a level this tick counts at the level predicted, as DK2's test sees
    /// the grant already applied.</summary>
    private int TotalStrength(byte player)
    {
        int sum = 0;
        foreach (KeeperCreatureView creature in Owned(player))
        {
            CreatureDef definition = _view.Database.CreatureById(creature.CreatureTypeId);
            int level = player == _view.PlayerId && _readiness.Granted.TryGetValue(creature.Id, out int granted) ? granted : creature.Level;
            sum = unchecked(sum + ThreatEvaluator.CreatureThreat(definition.Threat, (byte)level,
                unchecked((short)_view.Database.StatPercent(CreatureStatId.Threat, level))));
        }
        return sum;
    }

    /// <summary>A player's <c>DungeonHeartPosition</c>, tile (0, 0) for a player without a heart.</summary>
    private TilePos HeartPosition(byte player) => _view.HeartOf(player) ?? new TilePos(0, 0);

    private static Fixed16Vector PointOf(TilePos tile) => new(CreatureState.CentreOf(tile.X), CreatureState.CentreOf(tile.Y));

    /// <summary>The room's stored position (<c>+0x27</c>, <see cref="KeeperView.PositionOf"/>).</summary>
    private TilePos Position(KeeperRoomView room) => _view.PositionOf(room);

    /// <summary>The room's first slab (<c>+0x2D</c>, <see cref="KeeperView.FirstSlabOf"/>).</summary>
    private TilePos FirstSlab(KeeperRoomView room) => _view.FirstSlabOf(room);

    /// <summary>A player's room list in DK2's order: <c>Room_Construct</c> (<c>0x004E3800</c>) links a
    /// new room at the head, so the walk runs newest first; the capture's scan order stands in for
    /// the order the level's rooms were made.</summary>
    private IEnumerable<KeeperRoomView> RoomsOf(byte player) => _view.RoomsOf(player);

    /// <summary><c>0x004C6010</c>: the player's non-bridge room whose first slab lies nearest a
    /// point by the shifted square, the first kept on a tie.</summary>
    private KeeperRoomView? NearestNonBridgeRoom(byte player, Fixed16Vector point)
    {
        KeeperRoomView? best = null;
        uint bestDistance = uint.MaxValue;
        foreach (KeeperRoomView room in RoomsOf(player))
        {
            if (BridgeRules.IsBridge(room.Definition)) continue;
            TilePos slab = FirstSlab(room);
            uint distance = (uint)KeeperThreatResponder.ShiftedDistance(
                CreatureState.CentreOf(slab.X), CreatureState.CentreOf(slab.Y), point.X, point.Y);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = room;
            }
        }
        return best;
    }

    /// <summary><c>0x004C63D0</c>: an index drawn over the whole room list; a bridge at that index
    /// yields nothing, since the walk past it never stops.</summary>
    private KeeperRoomView? RandomNonBridgeRoom(byte player)
    {
        List<KeeperRoomView> rooms = RoomsOf(player).ToList();
        if (rooms.Count == 0) return null;
        KeeperRoomView drawn = rooms[_rng.NextInt(rooms.Count)];
        return BridgeRules.IsBridge(drawn.Definition) ? null : drawn;
    }

    private KeeperRoomView? RandomRoomOfType(byte player, RoomTypeId type) => _view.RandomRoomOfType(player, type, _rng);

    /// <summary>IAFirstEnd 2 and 3: the enemy room with the least or the greatest strength of its own
    /// creatures within five tiles of its position; the least from 0x7FFFFFFF strictly below, the
    /// greatest from zero strictly above, so all-zero strengths leave the greatest unset.</summary>
    private KeeperRoomView? RoomByOwnStrength(byte player, bool weakest)
    {
        IReadOnlyList<KeeperThreatSource> sources = ThreatEvaluator.Sources(_view);
        KeeperRoomView? best = null;
        int bestStrength = weakest ? int.MaxValue : 0;
        foreach (KeeperRoomView room in RoomsOf(player))
        {
            TilePos position = Position(room);
            int strength = ThreatEvaluator.LocalStrength(sources, player,
                CreatureState.CentreOf(position.X), CreatureState.CentreOf(position.Y), 5 * Fp32.One, false);
            if (weakest ? strength < bestStrength : strength > bestStrength)
            {
                bestStrength = strength;
                best = room;
            }
        }
        return best;
    }

    /// <summary><c>0x004FADF0</c>: the own room whose position lies furthest from the heart, strictly
    /// above zero, bridges and the heart room included.</summary>
    private KeeperRoomView? FurthestOwnRoomFromHeart()
    {
        TilePos heart = HeartPosition(_view.PlayerId);
        KeeperRoomView? best = null;
        int bestDistance = 0;
        foreach (KeeperRoomView room in RoomsOf(_view.PlayerId))
        {
            TilePos position = Position(room);
            int distance = KeeperThreatResponder.ShiftedDistance(
                CreatureState.CentreOf(position.X), CreatureState.CentreOf(position.Y),
                CreatureState.CentreOf(heart.X), CreatureState.CentreOf(heart.Y));
            if (distance > bestDistance)
            {
                bestDistance = distance;
                best = room;
            }
        }
        return best;
    }

    /// <summary>An owner's creature list in DK2's order; a corpse is a different thing there.</summary>
    private IEnumerable<KeeperCreatureView> Owned(byte player) =>
        _view.Creatures.Where(c => c.OwnerPlayerId == player && c.OwnershipRank >= 0 && c.Activity != CreatureActivity.Dead)
            .OrderBy(c => c.OwnershipRank);

    private bool IsWorker(KeeperCreatureView creature) =>
        _view.Database.CreatureById(creature.CreatureTypeId).Flags.HasFlag(CreatureFlags.IsWorker);
}
