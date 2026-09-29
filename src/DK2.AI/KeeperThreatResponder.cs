// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.AI;

/// <summary>The keeper's reactive half: the combat-message walk (<c>0x004FA400</c>), the
/// position reader (<c>0x004F9B90</c>) and the respond-to-attack event (<c>0x004FE0B0</c>),
/// each emitting commands the simulation validates on the next tick.</summary>
/// <remarks>
/// <para>Built: message kinds 1 and 6, the imprisonment reroll, threat assessment, the event
/// reservation, both event arms and the whole-tick allowance. Kinds 12, 34 and 47 have no
/// producer here, and the room reader waits for 34.</para>
/// <para>Deviates: picks and drops are commands executed one tick later, so an attempt's outcome
/// is predicted from the captured permissions, and only a creature predicted to move leaves the
/// tick's candidates, as DK2's does by its hand state. Not represented: the spiral's furniture-slot
/// exclusion <c>0x00454110</c> (ADR-0278).</para>
/// </remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/computer-keeper-threat.md")]
public sealed class KeeperThreatResponder
{
    /// <summary>The withdrawal arm considers a creature strictly nearer than this squared
    /// fixed-point distance: five tiles.</summary>
    public const int WithdrawRadiusSquared = 0x19000;

    /// <summary>A creature put down at most this many turns ago is left alone once the game turn
    /// is past the same number (<c>0x004FE189</c>).</summary>
    public const int DroppedGraceTurns = 300;

    /// <summary>DK2 drops the moved creature at tile × 4096 + 0x900.</summary>
    public const int DropOffset = 0x900;

    /// <summary>The keeper's <c>TrapsStartFights</c> bit: set by the defaults and never cleared.</summary>
    public const bool TrapsStartFights = true;

    private readonly KeeperView _view;
    private readonly KeeperAIState _state;
    private readonly KeeperAIParameters _p;
    private readonly SimRandom _rng;
    private readonly List<PlayerCommand> _commands;
    private readonly HashSet<int> _moved = new();
    private readonly Dictionary<int, bool> _marks = new();
    private readonly uint _turn;

    public KeeperThreatResponder(KeeperView view, KeeperAIState state, KeeperAIParameters parameters,
        SimRandom rng, List<PlayerCommand> commands)
    {
        _view = view;
        _state = state;
        _p = parameters;
        _rng = rng;
        _commands = commands;
        // DK2's first game turn is 1; the observation's tick is the turn about to run.
        _turn = unchecked((uint)(view.Tick.Value + 1));
    }

    // ----------------------------------------------------------------- 5.3 the message walk

    /// <summary>Walks the keeper's retained messages in queue order, skipping handled ones. A
    /// combat message is evaluated when a slot is free and acknowledged when the reader
    /// accepts it, cancelled reservations included; the fight flag is set either way. Every
    /// other kind is marked handled, as DK2's default branch does.</summary>
    public void HandleMessages()
    {
        foreach (PlayerMessage message in _view.Messages)
        {
            if (message.Handled) continue;
            switch (message.Kind)
            {
                case PlayerMessageKind.HeartAttacked:
                case PlayerMessageKind.CombatStarted:
                    // Flags bit 3 (respond to attack) is always on: both loaders write 0xFFFF.
                    var target = new TilePos(message.Data1, message.Data2);
                    if (_state.Responses.TryReceive(() => EvaluatePosition(target)))
                        _commands.Add(new AcknowledgePlayerMessageCommand(_view.PlayerId, message.Id));
                    _state.FightGoingOn = true; // 0x004FA520, even when the queue refused
                    break;
                default:
                    // Kinds 0, 13, 22 and 26 would be left in the queue; none is produced yet.
                    _commands.Add(new AcknowledgePlayerMessageCommand(_view.PlayerId, message.Id));
                    break;
            }
        }
    }

    /// <summary>The position reader: rerolls imprisonment when the keeper owns a Prison, sums
    /// strength at the tile, records the attacker, seeks a drop tile and reserves the event.
    /// Null cancels the reservation; the message is still handled.</summary>
    public KeeperThreatResponse? EvaluatePosition(TilePos target)
    {
        if (_view.OwnRoomOfType((byte)RoomTypeId.Prison) is not null) RerollImprisonment();
        KeeperThreatAssessment threat = ThreatEvaluator.Assess(_view, target, false, TrapsStartFights);
        if (threat.AttackingPlayer is byte attacker)
        {
            _state.AttackingPlayerId = attacker;
            _state.HaveBeenAttacked = true;
        }
        TilePos? site = ThreatEvaluator.DropSite(target, false, _view.IsThreatDropCandidate);
        return ThreatEvaluator.Response(target, site, threat.Enemy, threat.Allied,
            _p.BAThreatSuperiority, _p.BARemoveIfCantReinforce, false);
    }

    /// <summary>Every enemy player's creatures in list order: <c>random(100) &lt;
    /// ImprisonPercentage</c> sets this keeper's mark, otherwise clears it. Only a changed bit
    /// becomes a command, against the mark pending from an earlier reroll this tick, so the
    /// world ends up as DK2's does after the last roll.</summary>
    private void RerollImprisonment()
    {
        int bit = 1 << (_view.PlayerId - 1);
        foreach (byte player in _view.PlayerOrder)
        {
            if (_view.AreAllied(player)) continue;
            foreach (KeeperCreatureView creature in Owned(player))
            {
                bool mark = _rng.NextInt(100) < _p.ImprisonPercentage;
                if (!_marks.TryGetValue(creature.Id, out bool pending))
                    pending = (creature.RescuePriorityMask & bit) != 0;
                if (mark == pending) continue;
                _marks[creature.Id] = mark;
                _commands.Add(new SetRescuePriorityCommand(_view.PlayerId, creature.Id, mark));
            }
        }
    }

    // ----------------------------------------------------------------- 5.4 the event

    /// <summary>Step 6: dispatches the head event while the allowance lasts. Returns the
    /// allowance left, which is zero whenever an event ran (<c>0x004FE37D</c>).</summary>
    public int Dispatch(int budget)
    {
        if (budget <= 0 || _state.Responses.Count == 0) return budget;
        return _state.Responses.Dispatch((byte)Math.Min(budget, byte.MaxValue), Attempt);
    }

    private KeeperResponseAttempt Attempt(KeeperThreatResponse response)
    {
        bool withdrawing = response.RemainingThreat < 0;
        KeeperCreatureView? pick = withdrawing ? SelectForWithdrawal(response.Position) : SelectForReinforcement();
        if (pick is not KeeperCreatureView creature) return new KeeperResponseAttempt(false, null);
        if (!creature.PickupPermitted) return new KeeperResponseAttempt(true, null);

        CreatureDef definition = _view.Database.CreatureById(creature.CreatureTypeId);
        int threat = ThreatEvaluator.CreatureThreat(definition.Threat, (byte)creature.Level,
            unchecked((short)_view.Database.StatPercent(CreatureStatId.Threat, creature.Level)));
        if (withdrawing)
        {
            Move(creature, _view.Heart!.Value);
            return new KeeperResponseAttempt(true, threat);
        }

        // An owned payload tile moves nobody and still debits the threat (0x004FE5AA to
        // 0x004FE85C). Only an unowned one seeks a replacement and moves the creature there.
        TilePos? replacement = null;
        if (!_view.IsMine(response.Position))
        {
            replacement = ReplacementSite(response.Position);
            if (replacement is TilePos site) Move(creature, site);
        }
        return new KeeperResponseAttempt(true, threat, replacement);
    }

    /// <summary>Pick up, and drop when the hand is predicted to take it (<c>0x004BCAE0</c>
    /// after <c>0x004BC500</c>); the creature then leaves this tick's candidates, as DK2's
    /// does through its hand state. A pickup the full hand refuses leaves it where it stands,
    /// and the next attempt may select it again.</summary>
    private void Move(KeeperCreatureView creature, TilePos to)
    {
        _commands.Add(new PickUpCreatureCommand(_view.PlayerId, creature.Id));
        if (!creature.CanPickUp) return;
        _commands.Add(new DropCommand(_view.PlayerId, to));
        _moved.Add(creature.Id);
    }

    private IEnumerable<KeeperCreatureView> Owned(byte player) =>
        _view.Creatures.Where(c => c.OwnerPlayerId == player && c.OwnershipRank >= 0)
            .OrderBy(c => c.OwnershipRank);

    /// <summary>The withdrawal arm's first eligible creature: within five tiles of the payload,
    /// not the Reaper, not under Call to Arms, not put down within the grace once the level is
    /// past it, not in this hand and not in the hand's state.</summary>
    private KeeperCreatureView? SelectForWithdrawal(TilePos target)
    {
        int centreX = target.X * Fp32.One + Fp32.One / 2, centreY = target.Y * Fp32.One + Fp32.One / 2;
        foreach (KeeperCreatureView creature in Owned(_view.PlayerId))
        {
            if (_moved.Contains(creature.Id)) continue;
            if (!WithinFiveTiles(creature.PosX, creature.PosY, centreX, centreY)) continue;
            CreatureDef definition = _view.Database.CreatureById(creature.CreatureTypeId);
            if (definition.Flags.HasFlag(CreatureFlags.IsHorny)) continue;
            if (UnderCallToArms(creature)) continue;
            if (creature.LastDroppedCounter <= DroppedGraceTurns && _turn > DroppedGraceTurns) continue;
            if (creature.InMyHand || creature.StateId == DropLandingRules.LandingState) continue;
            return creature;
        }
        return null;
    }

    /// <summary>The reinforcement arm's first eligible creature, in DK2's test order: a
    /// non-worker that is not the Reaper, not under Call to Arms, may look for a fight, has
    /// more than a quarter and then more than half of its level-scaled health, is not in a
    /// group 7 or 3 state, is not fighting through a walk, is not in this hand, not in the
    /// hand's state and not stunned.</summary>
    private KeeperCreatureView? SelectForReinforcement()
    {
        foreach (KeeperCreatureView creature in Owned(_view.PlayerId))
        {
            if (_moved.Contains(creature.Id)) continue;
            CreatureDef definition = _view.Database.CreatureById(creature.CreatureTypeId);
            if (definition.Flags.HasFlag(CreatureFlags.IsWorker) || definition.Flags.HasFlag(CreatureFlags.IsHorny)) continue;
            if (UnderCallToArms(creature) || !creature.MayFight) continue;
            int group = CreatureStateTable.Get(creature.StateId).Group;
            if (creature.MaxHealth / 4 >= creature.Health || group is 7 or 3) continue;
            if (creature.MaxHealth / 2 >= creature.Health) continue;
            if (CreatureStateTable.Get(creature.ResolvedStateId).Category == CreatureStateCategory.Combat) continue;
            if (creature.InMyHand || creature.StateId == DropLandingRules.LandingState) continue;
            if (IsStunned(creature.StateId)) continue;
            return creature;
        }
        return null;
    }

    /// <summary><c>0x0048F350</c>: the stunned states 209 and 255 to 257.</summary>
    public static bool IsStunned(int stateId) => stateId is 209 or 255 or 256 or 257;

    /// <summary><c>0x004888C0</c>, captured with the creature: the owner's flag, its bit 20 or
    /// the states 190 to 192 through a walk (<c>CallToArmsRules</c>).</summary>
    public static bool UnderCallToArms(KeeperCreatureView creature) => creature.UnderCallToArms;

    /// <summary>Per-axis absolute differences, squared as 64-bit values, shifted down twelve,
    /// summed as 32-bit values and compared signed, as <c>0x004FE0DA</c> does.</summary>
    public static bool WithinFiveTiles(int x, int y, int centreX, int centreY) =>
        ShiftedDistance(x, y, centreX, centreY) < WithdrawRadiusSquared;

    /// <summary>The keeper's squared distance in tiles times 4096: per-axis absolute differences,
    /// squared as 64-bit values, shifted down twelve and summed as 32-bit values (also
    /// <c>0x00503060</c>, <c>0x004FEFC0</c>); the caller compares it signed.</summary>
    public static int ShiftedDistance(int x, int y, int centreX, int centreY)
    {
        long dx = Math.Abs((long)x - centreX), dy = Math.Abs((long)y - centreY);
        return unchecked((int)((uint)((dx * dx) >> 12) + (uint)((dy * dy) >> 12)));
    }

    /// <summary>The square spiral from an unowned payload: the tile itself, then right one,
    /// down one, left two, up two and so on, each candidate an owned tile whose navigation byte
    /// is open; it gives up when a side reaches twice the larger map dimension.</summary>
    public TilePos? ReplacementSite(TilePos payload)
    {
        if (_view.IsThreatDropCandidate(payload)) return payload;
        int bound = 2 * Math.Max(_view.Width, _view.Height);
        int x = payload.X + 1, y = payload.Y;
        int step = 1, run = 1, count = 1;
        while (true)
        {
            for (; run > 0; run--)
            {
                var tile = new TilePos(x, y);
                if (_view.IsThreatDropCandidate(tile)) return tile;
                y += step;
            }
            run = count + 1;
            int pending = -step;
            count = run;
            for (; count > 0; count--)
            {
                var tile = new TilePos(x, y);
                if (_view.IsThreatDropCandidate(tile)) return tile;
                x += pending;
            }
            step = pending;
            count = run;
            if (run >= bound) return null;
        }
    }
}
