// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.AI;

/// <summary>The keeper's battle step (<c>0x00503060</c>), which the tick runs instead of imp
/// creation while the fight flag is on, and the Call to Arms removal test (<c>0x004FEFC0</c>);
/// both emit the commands the human would: a cast at a place, a cast at a creature, or the slap
/// that takes the flag down.</summary>
/// <remarks>
/// <para>Built: the fight flag's recomputation, the emergency call at a fighter, the spell roll,
/// both selection arms with their target scans, and the removal's three gates; Thunderbolt, Heal
/// and Chicken are cast at the creature and Inferno at its position. A chosen spell the simulation
/// cannot cast (none shipped) is counted and not issued.</para>
/// <para>Deviates: a cast is a command executed a tick later, so its success is predicted from
/// the captured permissions and the removal test waits until the call is seen (ADR-0283).</para>
/// </remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/computer-keeper-management.md")]
public sealed class KeeperBattleStep
{
    /// <summary>The emergency call is cast only on a turn divisible by this.</summary>
    public const int CastTurnModulus = 40;

    /// <summary>The strength radius, twelve tiles in 4096ths; its square overflows the helper's
    /// 32-bit arithmetic, so it admits the whole map (computer-keeper-threat.md §2).</summary>
    public const int StrengthRadius = 12 * Fp32.One;

    /// <summary>A target scan stops at its current pick when <c>random(100)</c> exceeds this.</summary>
    public const int StopScanAbove = 75;

    /// <summary>Each spell's roll draws from its mana cost divided by this, plus one or two.</summary>
    public const int ManaCostPerRollStep = 2499;

    /// <summary>Ours: turns after a cast during which an unseen call is still in flight.</summary>
    public const int CastGraceTurns = 3;

    private readonly KeeperView _view;
    private readonly KeeperAIState _state;
    private readonly KeeperAIParameters _p;
    private readonly SimRandom _rng;
    private readonly List<PlayerCommand> _commands;
    private readonly long _turn;
    private readonly HashSet<int> _prepared;
    private IReadOnlyList<KeeperThreatSource>? _sources;

    /// <param name="prepared">Creatures the tick's earlier steps are predicted to have prepared for
    /// the hand, which DK2's walk finds in state 75 or 78, outside the combat category.</param>
    public KeeperBattleStep(KeeperView view, KeeperAIState state, KeeperAIParameters parameters,
        SimRandom rng, List<PlayerCommand> commands, HashSet<int>? prepared = null)
    {
        _view = view;
        _state = state;
        _p = parameters;
        _rng = rng;
        _commands = commands;
        _prepared = prepared ?? new HashSet<int>();
        // DK2's first game turn is 1; the observation's tick is the turn about to run.
        _turn = view.Tick.Value + 1;
    }

    /// <summary>Clears the fight flag and walks the owned list: every creature fighting through a
    /// walk sets it again; the first with a target, no call and no attack wait may raise the call
    /// at itself, and the first that passes the spell roll ends the walk with one spell attempt.
    /// Returns the allowance left: one less after a cast the permission passed.</summary>
    public int Run(int allowance)
    {
        _state.FightGoingOn = false;
        foreach (KeeperCreatureView fighter in Owned(_view.PlayerId))
        {
            if (!IsFighting(fighter)) continue;
            _state.FightGoingOn = true;
            if (TryEmergencyCall(fighter)) return allowance - 1;
            if ((uint)_rng.NextInt(4) < (uint)(_p.UseKeeperSpells & 7)) continue;
            return CastASpell(fighter, allowance);
        }
        return allowance;
    }

    /// <summary>The removal test: on the counter's zero, fewer enemies than wanted strictly within
    /// the range of the stored position (each enemy player's creatures on the map and its Heart
    /// room), or fewer own non-workers anywhere than wanted, the flag is slapped down and the
    /// attack wait set to four times the parameter. No allowance is spent.</summary>
    public void TestRemoval()
    {
        if (!_view.CallToArms.On)
        {
            if (_turn - _state.CallToArmsCastTurn <= CastGraceTurns) return;
            // Ours: the simulation refused the cast a tick after DK2's would have refused at once.
            _state.CallToArmsInUse = false;
            _state.CallToArmsCounter = 0;
            return;
        }
        bool remove = _state.CallToArmsCounter == 0;
        if (!remove)
        {
            int centreX = ((_state.CallToArmsX >> 12) << 12) + (Fp32.One / 2);
            int centreY = ((_state.CallToArmsY >> 12) << 12) + (Fp32.One / 2);
            int limit = _p.RemoveCallToArmsWithinRange * _p.RemoveCallToArmsWithinRange * Fp32.One;
            int enemies = 0;
            foreach (PlayerId player in _view.PlayerOrder)
            {
                if (_view.AreAllied(player.Value)) continue;
                foreach (KeeperCreatureView creature in Owned(player))
                    if (!OffMap(creature) && KeeperThreatResponder.ShiftedDistance(creature.PosX, creature.PosY, centreX, centreY) < limit)
                        enemies++;
                foreach (KeeperRoomView room in _view.AllRooms)
                {
                    if (room.OwnerPlayerId != player || room.Definition.RoomId != (byte)RoomTypeId.DungeonHeart) continue;
                    TilePos centre = KeeperView.Centre(room);
                    if (KeeperThreatResponder.ShiftedDistance(CreatureState.CentreOf(centre.X), CreatureState.CentreOf(centre.Y), centreX, centreY) < limit)
                        enemies++;
                    break;
                }
            }
            if (enemies < _p.RemoveCallToArmsEnemyCreatures) remove = true;
        }
        if (!remove)
        {
            int own = 0;
            foreach (KeeperCreatureView creature in Owned(_view.PlayerId))
                if (!IsWorker(creature) && !OffMap(creature)) own++;
            if (own < _p.RemoveCallToArmsOwnCreatures) remove = true;
        }
        if (!remove) return;
        _state.CallToArmsInUse = false;
        _commands.Add(new RemoveCallToArmsCommand(_view.PlayerId));
        _state.CannotAttackCounter = _p.WaitBeforeAttackingAgain << 2;
    }

    /// <summary>The emergency call: the target owner's strength within twelve tiles of the fighter
    /// must exceed <c>BACallToArmsValue</c> and the fighter must not stand on lava; the position is
    /// stored before the cost (strictly below the mana), the fortieth turn, availability 3 and the
    /// route from the heart are tested. A cast the permission passes sets the counter and the bit.</summary>
    private bool TryEmergencyCall(KeeperCreatureView fighter)
    {
        if (fighter.FightTargetOwner == 0 || _state.CallToArmsInUse || _state.CannotAttackCounter != 0) return false;
        int strength = ThreatEvaluator.LocalStrength(Sources, fighter.FightTargetOwner, fighter.PosX, fighter.PosY, StrengthRadius, false);
        if (strength <= _p.BACallToArmsValue || _view.IsLava(TileOf(fighter))) return false;
        _state.CallToArmsX = fighter.PosX;
        _state.CallToArmsY = fighter.PosY;
        KeeperSpellDef? spell = _view.Database.KeeperSpellById(CallToArmsRules.SpellId);
        if (spell is null || spell.ManaCost >= _view.Mana) return false;
        if (_turn % CastTurnModulus != 0) return false;
        if (_view.Availability(AvailabilityKind.Spell, CallToArmsRules.SpellId) != 3) return false;
        if (!fighter.HeartRouteExists) return false;
        // 0x004BBD30's own permission, predicted from the capture: the row's cooldown and the two
        // validators at the fighter's tile; the cost was tested above, more strictly.
        if (!Ready(spell) || !fighter.CallToArmsSiteAllowed) return false;
        _commands.Add(new CastSpellAtPlaceCommand(_view.PlayerId, CallToArmsRules.SpellId, fighter.PosX, fighter.PosY));
        _state.CallToArmsCounter = _p.RemoveCallToArmsAfter;
        _state.CallToArmsInUse = true;
        _state.CallToArmsCastTurn = _turn;
        return true;
    }

    /// <summary>One spell attempt: <c>random(4)</c> below the attacking-versus-defensive preference
    /// takes the supportive arm, else the offensive one; each walks spell ids ascending with its own
    /// roll and fallback, then scans for a target strictly within five tiles of the fighter. A
    /// thing-targeted cast the permission passes spends one allowance; a refused one spends none.</summary>
    private int CastASpell(KeeperCreatureView fighter, int allowance)
    {
        byte spell = 0;
        int target = 0;
        int count = _view.Database.KeeperSpells.Count;
        uint threshold = unchecked((uint)(4 - (_p.SpellPreference & 7)));
        if ((uint)_rng.NextInt(4) < (uint)(_p.AttackingVsDefensiveSpellPreference & 7))
        {
            // Supportive: each eligible spell is retained before its roll, so the last stays selected.
            for (int id = 1; id <= count; id++)
            {
                KeeperSpellDef? candidate = Eligible((byte)id, KeeperSpellFlags.Supportive);
                if (candidate is null) continue;
                spell = (byte)id;
                if ((uint)_rng.NextInt((candidate.ManaCost / ManaCostPerRollStep) + 1) >= threshold) break;
            }
            if (spell != 0)
                foreach (KeeperCreatureView creature in Owned(_view.PlayerId))
                {
                    if (!KeeperThreatResponder.WithinFiveTiles(creature.PosX, creature.PosY, fighter.PosX, fighter.PosY)) continue;
                    if (creature.Health >= (int)((uint)creature.MaxHealth >> 1) || creature.Activity == CreatureActivity.Dying) continue;
                    target = creature.Id;
                    if (_rng.NextInt(100) > StopScanAbove) break;
                }
        }
        else
        {
            // Offensive: the first eligible spell whose roll succeeds; none when every roll fails.
            for (int id = 1; id <= count; id++)
            {
                KeeperSpellDef? candidate = Eligible((byte)id, KeeperSpellFlags.Offensive);
                if (candidate is null) continue;
                if ((uint)_rng.NextInt((candidate.ManaCost / ManaCostPerRollStep) + 2) < threshold) continue;
                spell = (byte)id;
                break;
            }
            if (spell != 0 && fighter.FightTargetOwner != 0)
                foreach (KeeperCreatureView creature in Owned(fighter.FightTargetOwner))
                {
                    if (!KeeperThreatResponder.WithinFiveTiles(creature.PosX, creature.PosY, fighter.PosX, fighter.PosY)) continue;
                    target = creature.Id;
                    if (_rng.NextInt(100) > StopScanAbove) break;
                }
        }
        if (spell == 0 || target == 0) return allowance;

        KeeperSpellDef chosen = _view.Database.KeeperSpellById(spell)!;
        bool atAThing = KeeperSpellRules.TargetsAThing(chosen.TargetSelector);
        if (atAThing ? !KeeperSpellRules.ExecutesOnCreature(spell) : !KeeperSpellRules.ExecutesAtPlace(spell))
        {
            // A spell DK2 would test and cast that ours has no effect for: nothing is issued or spent.
            _state.UnbuiltSpellChoices++;
            return allowance;
        }
        KeeperCreatureView victim = default;
        foreach (KeeperCreatureView creature in _view.Creatures)
            if (creature.Id == target) { victim = creature; break; }
        if (!Ready(chosen) || chosen.ManaCost > _view.Mana) return allowance;
        if (!atAThing)
        {
            // 0x004BB8A0 then 0x004BBD30 at the target's position: the allowance goes only when both
            // succeed, and the cast succeeds wherever the permission passes.
            if (!victim.InfernoPlaceAllowed) return allowance;
            _commands.Add(new CastSpellAtPlaceCommand(_view.PlayerId, spell, victim.PosX, victim.PosY));
            return allowance - 1;
        }
        // 0x004BBA40's permission, predicted: the row's cooldown, the cost against the mana and the
        // spell's captured validators. The cast's own return is ignored, as DK2 ignores it.
        bool permitted = spell switch
        {
            KeeperSpellRules.Heal => victim.HealTargetAllowed,
            KeeperSpellRules.Chicken => victim.ChickenTargetAllowed,
            _ => victim.SpellTargetAllowed,
        };
        if (!permitted) return allowance;
        _commands.Add(new CastSpellOnCreatureCommand(_view.PlayerId, spell, target));
        return allowance - 1;
    }

    /// <summary>A spell the arm may pick: availability 3, no cooldown left, the arm's flag set and
    /// the excluded flag clear, in that order.</summary>
    private KeeperSpellDef? Eligible(byte id, KeeperSpellFlags arm)
    {
        if (_view.Availability(AvailabilityKind.Spell, id) != 3) return null;
        KeeperSpellDef? spell = _view.Database.KeeperSpellById(id);
        if (spell is null || !Ready(spell)) return null;
        if ((spell.Flags & arm) == 0 || (spell.Flags & KeeperSpellFlags.ExcludedFromBattle) != 0) return null;
        return spell;
    }

    private bool Ready(KeeperSpellDef spell) => Ready(_view, spell);

    /// <summary><c>0x004BE390</c>: no cooldown left on the keeper's row for the spell.</summary>
    internal static bool Ready(KeeperView view, KeeperSpellDef spell)
    {
        AvailabilityState? row = view.AvailabilityRow(AvailabilityKind.Spell, spell.KeeperSpellId);
        if (row is not AvailabilityState entry || entry.LastCastTick == 0) return true;
        long cooldown = (uint)unchecked(spell.CooldownRaw * SimulationTiming.OriginalTicksPerGameSecond) >> 12;
        return view.Tick.Value - entry.LastCastTick >= cooldown;
    }

    private IReadOnlyList<KeeperThreatSource> Sources => _sources ??= ThreatEvaluator.Sources(_view);

    /// <summary><c>0x00478050</c> answering the combat category: an own creature fighting through a
    /// walk. A held creature is out of the category in DK2 (state 76 or 78) and excluded here, as
    /// is one the retreat prepared for the hand this tick.</summary>
    private bool IsFighting(KeeperCreatureView creature) =>
        creature.Activity != CreatureActivity.Dead && !creature.InHand && !_prepared.Contains(creature.Id)
        && creature.ResolvedStateId >= 0
        && CreatureStateTable.Get(creature.ResolvedStateId).Category == CreatureStateCategory.Combat;

    /// <summary>An owner's creature list in DK2's order; a corpse is a different thing there.</summary>
    private IEnumerable<KeeperCreatureView> Owned(PlayerId player) =>
        _view.Creatures.Where(c => c.OwnerPlayerId == player && c.OwnershipRank >= 0 && c.Activity != CreatureActivity.Dead)
            .OrderBy(c => c.OwnershipRank);

    /// <summary>Map layer 2: held or carried, which the counts and the strength skip.</summary>
    private static bool OffMap(KeeperCreatureView creature) => creature.InHand || creature.CarriedByCreatureId >= 0;

    private bool IsWorker(KeeperCreatureView creature) =>
        _view.Database.CreatureById(creature.CreatureTypeId).Flags.HasFlag(CreatureFlags.IsWorker);

    private static TilePos TileOf(KeeperCreatureView creature) => new(creature.PosX >> 12, creature.PosY >> 12);
}
