// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.AI;

/// <summary>The keeper's training moves (<c>0x004FF5C0</c>), run at step 9 when the army is not
/// ready: every own non-worker the branches select is lifted and put down on a floor slot of a
/// Combat Pit, if it is close to a level or past the Training Room's cap, or of a Training Room
/// otherwise; one allowance each (computer-keeper-management.md §2).</summary>
/// <remarks>Deviates: the pick-up and drop are commands executed a tick later, issued as the
/// retreat's are; the rooms and slots are drawn from the keeper's own stream, and the drop is at
/// the slot's tile without its <c>+0x900</c> fraction (ADR-0317).</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/computer-keeper-management.md")]
public sealed class KeeperTrainingStep
{
    /// <summary>The state a creature prepared for the hand sits in when the hand refuses it
    /// (<c>0x004BFBD0</c>); a lifted and dropped one is landing (76).</summary>
    public const int PreparedState = 75;

    /// <summary>The dropped-counter guard, as the retreat's (<c>0x004FF731</c>, <c>0x004FF8E7</c>).</summary>
    public const int DroppedGraceTurns = KeeperThreatResponder.DroppedGraceTurns;

    /// <summary>The step clamps a scaled experience threshold to this (<c>0x004FF6B7</c>).</summary>
    private const int ExperienceThresholdCap = 0xFFFE;

    private readonly KeeperView _view;
    private readonly KeeperAIState _state;
    private readonly KeeperAIParameters _p;
    private readonly SimRandom _rng;
    private readonly List<PlayerCommand> _commands;
    private readonly KeeperReadiness _readiness;
    private readonly HashSet<int> _prepared;
    private readonly long _turn;

    /// <param name="prepared">The creatures earlier steps of this tick prepared for the hand; this
    /// step adds its own.</param>
    public KeeperTrainingStep(KeeperView view, KeeperAIState state, KeeperAIParameters parameters, SimRandom rng,
        List<PlayerCommand> commands, KeeperReadiness readiness, HashSet<int> prepared)
    {
        _view = view;
        _state = state;
        _p = parameters;
        _rng = rng;
        _commands = commands;
        _readiness = readiness;
        _prepared = prepared;
        // DK2's first game turn is 1; the observation's tick is the turn about to run.
        _turn = view.Tick.Value + 1;
    }

    /// <summary>Draws a Training Room and a Combat Pit, then walks the owned list while allowance
    /// remains. A branch is off without its room or while the keeper is building that type
    /// (<c>CurrentRoomToBuild</c> low seven bits); with both off nothing is walked.</summary>
    public int Run(int allowance)
    {
        KeeperRoomView? training = _view.RandomRoomOfType(_view.PlayerId, RoomTypeId.TrainingRoom, _rng);
        KeeperRoomView? pit = _view.RandomRoomOfType(_view.PlayerId, RoomTypeId.CombatPit, _rng);
        int building = _state.CurrentRoomToBuild & 0x7F;
        if (building == (byte)RoomTypeId.TrainingRoom) training = null;
        if (building == (byte)RoomTypeId.CombatPit) pit = null;
        if (training is null && pit is null) return allowance;

        foreach (KeeperCreatureView creature in Owned())
        {
            if (allowance <= 0) break;
            CreatureDef definition = _view.Database.CreatureById(creature.CreatureTypeId);
            if (definition.Flags.HasFlag(CreatureFlags.IsWorker)) continue;
            int level = LevelOf(creature);

            if (WantsThePit(creature, definition, level))
            {
                // No fallback: a Pit candidate without a Pit is passed over.
                if (pit is null || !HealthyEnough(creature, definition, level)) continue;
                if (!Eligible(creature, CreatureStateCategory.CombatPit)) continue;
                // The Pit asks for its slot before the permission.
                if (Slot(pit) is TilePos slot && creature.PickupPermitted) Move(creature, slot);
            }
            else
            {
                if (training is null || !Eligible(creature, CreatureStateCategory.Training)) continue;
                // Training asks the permission first; refused, it draws no slot.
                if (creature.PickupPermitted && Slot(training) is TilePos slot) Move(creature, slot);
            }
            allowance--;
        }
        return allowance;
    }

    /// <summary>Experience strictly above three quarters of the next level's threshold, compared
    /// unsigned; or a level at or above <c>TrainingRoomMaxExperienceLevel</c> and strictly below
    /// <c>IACreatureSkillLevel</c>. Experience at the bar, or level at the skill level, is Training.</summary>
    private bool WantsThePit(KeeperCreatureView creature, CreatureDef definition, int level)
    {
        int threshold = ExperienceThreshold(definition, level);
        if ((uint)creature.Experience > (uint)((threshold * 3) / 4)) return true;
        int cap = _view.Database[VariableTypeId.TrainingRoomMaxExperienceLevel] & 0xFF;
        return level >= cap && level < (_p.IACreatureSkillLevel & 0xF);
    }

    /// <summary><c>ExpForNextLevel</c> raw at level 1; otherwise scaled by the current level's
    /// percentage and clamped to 0..<c>0xFFFE</c>, as the step computes it.</summary>
    private int ExperienceThreshold(CreatureDef definition, int level)
    {
        if (level == CreatureLevelRules.FirstLevel) return definition.ExpForNextLevel;
        int scaled = CreatureLevelRules.Scale(definition.ExpForNextLevel,
            CreatureLevelRules.Percent(_view.Database, CreatureStatId.ExperienceForNextLevel, level));
        return Math.Clamp(scaled, 0, ExperienceThresholdCap);
    }

    /// <summary>Health strictly above three quarters of the level-scaled maximum, an unsigned
    /// shift; a creature readiness levelled this tick carries its health share across.</summary>
    private bool HealthyEnough(KeeperCreatureView creature, CreatureDef definition, int level)
    {
        int max = CreatureLevelRules.MaxHealth(_view.Database, definition, level);
        return HealthOf(creature, definition, level) > (int)((uint)(max * 3) >> 2);
    }

    /// <summary>The predicates both branches apply, in DK2's order: the dropped-counter guard, not
    /// already in the destination's category, no dropped-to-do job, not in the needs group, not
    /// fighting, not in this hand, not landing, not stunned.</summary>
    public bool Eligible(KeeperCreatureView creature, CreatureStateCategory destination)
    {
        if (creature.LastDroppedCounter <= DroppedGraceTurns && _turn > DroppedGraceTurns) return false;
        (int state, int resolved) = StatesOf(creature);
        CreatureStateCategory category = CreatureStateTable.Get(resolved).Category;
        if (category == destination) return false;
        if (creature.DroppedToDoJob != JobType.None) return false;
        if (CreatureStateTable.Get(state).Group == NeedRules.NeedGroup) return false;
        if (category == CreatureStateCategory.Combat) return false;
        if (creature.InMyHand) return false;
        return state != DropLandingRules.LandingState && !KeeperThreatResponder.IsStunned(state);
    }

    /// <summary>A creature an earlier step prepared is, in DK2, landing (76) when the hand took and
    /// dropped it, or still in 75 when the hand refused it.</summary>
    private (int State, int Resolved) StatesOf(KeeperCreatureView creature)
    {
        if (!_prepared.Contains(creature.Id)) return (creature.StateId, creature.ResolvedStateId);
        int state = creature.CanPickUp ? DropLandingRules.LandingState : PreparedState;
        return (state, state);
    }

    /// <summary><c>0x004E8210(1, 0)</c>: a floor slot drawn uniformly in chain order; none, no draw.</summary>
    private TilePos? Slot(KeeperRoomView room) =>
        room.FloorSlots.Count == 0 ? null : _view.FromIndex(room.FloorSlots[_rng.NextInt(room.FloorSlots.Count)]);

    /// <summary><c>0x004BFBD0</c> prepares, <c>0x004BC500</c> lifts unless the hand refuses, and the
    /// drop follows only a lift.</summary>
    private void Move(KeeperCreatureView creature, TilePos slot)
    {
        _commands.Add(new PickUpCreatureCommand(_view.PlayerId, creature.Id));
        _prepared.Add(creature.Id);
        if (creature.CanPickUp) _commands.Add(new DropCommand(_view.PlayerId, slot));
    }

    private int LevelOf(KeeperCreatureView creature) =>
        _readiness.Granted.TryGetValue(creature.Id, out int level) ? level : creature.Level;

    /// <summary>The level-up's 12-bit share (<c>0x0048B120</c>) for a creature readiness granted a
    /// level this tick; the captured health otherwise.</summary>
    private int HealthOf(KeeperCreatureView creature, CreatureDef definition, int level)
    {
        if (level == creature.Level || creature.MaxHealth <= 0) return creature.Health;
        long ratio = ((long)creature.Health << 12) / creature.MaxHealth;
        return (int)((ratio * CreatureLevelRules.MaxHealth(_view.Database, definition, level)) >> 12);
    }

    /// <summary>The owner's list in DK2's order, held ones included; a corpse is a different thing.</summary>
    private IEnumerable<KeeperCreatureView> Owned() =>
        _view.Creatures.Where(c => c.OwnerPlayerId == _view.PlayerId && c.OwnershipRank >= 0
                && c.Activity != CreatureActivity.Dead)
            .OrderBy(c => c.OwnershipRank);
}
