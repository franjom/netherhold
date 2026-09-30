// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.AI;

/// <summary>The keeper's readiness test (<c>0x004FAB10</c>): strictly more non-workers than
/// <c>IAMinimumCreatures</c>, every one at or above <c>IACreatureSkillLevel</c>; when the
/// under-levelled ones are at most that level and fewer than the army, each first gets one
/// ordinary level-up (<c>0x0048B120</c>), a grant this build issues as a command. One instance
/// serves a tick's calls, so the training step's grants are seen by the planner's test.</summary>
/// <remarks>Deviates: the grant lands a tick later, so this tick's tests read the predicted level
/// and a creature granted once is not granted again the same tick (ADR-0298).</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/computer-keeper-threat.md")]
public sealed class KeeperReadiness
{
    private readonly KeeperView _view;
    private readonly KeeperAIParameters _p;
    private readonly List<PlayerCommand> _commands;
    private readonly Dictionary<int, int> _granted = new();

    public KeeperReadiness(KeeperView view, KeeperAIParameters parameters, List<PlayerCommand> commands)
    {
        _view = view;
        _p = parameters;
        _commands = commands;
    }

    /// <summary>The creatures granted a level this tick, by id, with the level predicted.</summary>
    public IReadOnlyDictionary<int, int> Granted => _granted;

    public bool ArmyReady()
    {
        int skill = _p.IACreatureSkillLevel & 0xF;
        int total = 0, below = 0;
        foreach (KeeperCreatureView creature in NonWorkers())
        {
            total++;
            if (LevelOf(creature) < skill) below++;
        }
        if (total <= _p.IAMinimumCreatures) return false;
        if (below <= skill && below < total)
            foreach (KeeperCreatureView creature in NonWorkers())
                if (LevelOf(creature) < skill) Grant(creature);
        foreach (KeeperCreatureView creature in NonWorkers())
            if (LevelOf(creature) < skill) return false;
        return true;
    }

    private int LevelOf(KeeperCreatureView creature) =>
        _granted.TryGetValue(creature.Id, out int level) ? level : creature.Level;

    /// <summary>The level-up refuses a finished creature and level 10, as <c>CreatureLevelRules</c>
    /// does, so those are predicted unchanged.</summary>
    private void Grant(KeeperCreatureView creature)
    {
        int level = LevelOf(creature);
        if (level >= CreatureLevelRules.MaxLevel || creature.Health <= 0) return;
        _commands.Add(new GrantCreatureLevelCommand(_view.PlayerId, creature.Id));
        _granted[creature.Id] = level + 1;
    }

    /// <summary>The owner's list in DK2's order, workers left out; a corpse is a different thing.</summary>
    private IEnumerable<KeeperCreatureView> NonWorkers() =>
        _view.Creatures.Where(c => c.OwnerPlayerId == _view.PlayerId && c.OwnershipRank >= 0
                && c.Activity != CreatureActivity.Dead
                && !_view.Database.CreatureById(c.CreatureTypeId).Flags.HasFlag(CreatureFlags.IsWorker))
            .OrderBy(c => c.OwnershipRank);
}
