// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Combat;

namespace DK2.Presentation;

public readonly record struct BattleViewSlot(int Value)
{
    public static BattleViewSlot None => new(-1);
}
public readonly record struct BattleViewSnapshot(BattleViewSlot Slot, TilePos Centre, PlayerMask Owners, int MemberCount);

/// <summary>View-owned battle membership and music raises captured from each settled turn; never writes the world.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/music.md#8-pseudocode-ours")]
public sealed class MusicWorldObserver(PlayerId viewer, MusicDirector music)
{
    private sealed class Battle
    {
        public readonly Dictionary<int, long> Members = new();
        public int X, Y;
    }
    private readonly Battle[] _battles = Enumerable.Range(0, 8).Select(_ => new Battle()).ToArray();
    private readonly Dictionary<int, (int State, int Resume)> _previous = new();
    private long _lastTick = -1;
    private readonly BattleViewSnapshot[] _snapshots = new BattleViewSnapshot[8];
    private IReadOnlyList<BattleViewSnapshot>? _snapshotView;
    public IReadOnlyList<BattleViewSnapshot> Battles => _snapshotView ??= Array.AsReadOnly(_snapshots);

    public void Capture(WorldState world, GameDatabase database, GameTick tick)
    {
        if (_lastTick == tick.Value) return;
        _lastTick = tick.Value;
        foreach (CreatureState creature in world.Creatures)
        {
            if (!CreatureStateMachine.HasState(creature)) continue;
            int state = creature.Dk2StateId;
            bool moving = CreatureStateTable.Get(state).Category == CreatureStateCategory.Movement;
            int resume = moving ? creature.UtilityNewState : state;
            var current = (state, resume);
            if (!_previous.TryGetValue(creature.Id, out var previous) || previous != current)
                if (moving && resume is 164 or 180 or 184 || state == 186) music.Raise(MusicLevel.Raid);
            _previous[creature.Id] = current;
            if (database.Creatures.TryGetValue(creature.CreatureTypeId, out CreatureDef? definition)
                && (definition.Flags & CreatureFlags.IsHorny) != 0)
                if ((world.Player(creature.OwnerPlayerId).Finale.Flags & FinaleRules.SequenceStarted) != 0) music.Set(MusicLevel.Silent);
                else music.Raise(MusicLevel.Reaper);
        }
        if (tick.Value + 1 == HeartIntroRules.StartTurn(database) + HeartIntroRules.SequenceSeconds * SimulationTiming.TicksPerSecond)
            music.Set(MusicLevel.Silent);
        foreach (Battle battle in _battles)
        {
            int threat = 0;
            foreach (int id in battle.Members.Keys.ToArray())
            {
                CreatureState? creature = world.CreatureById(id);
                bool fighting = creature is not null && Fighting(creature);
                if (creature is null || !CreatureStateMachine.HasState(creature) || creature.Health <= 0
                    || !fighting && tick.Value >= battle.Members[id])
                { battle.Members.Remove(id); continue; }
                if (fighting && creature.OwnerPlayerId != viewer
                    && database.Creatures.TryGetValue(creature.CreatureTypeId, out CreatureDef? definition))
                    threat += FearRules.ThreatAtLevel(database, definition, creature.Level);
            }
            Recenter(battle, world);
            if (threat >= database[VariableTypeId.MusicLevelFourThreatThreshold]) music.Raise(MusicLevel.Reaper);
            else if (threat >= database[VariableTypeId.MusicLevelThreeThreatThreshold]) music.Raise(MusicLevel.LargeBattle);
            else if (threat >= database[VariableTypeId.MusicLevelTwoThreatThreshold]) music.Raise(MusicLevel.Battle);
        }
        for (byte owner = 0; owner < 8; owner++)
        {
            var player = new PlayerId(owner);
            if (!world.HasPlayer(player)) continue;
            foreach (CreatureState creature in world.OwnedCreatures(player))
            {
                if (!Fighting(creature) || _battles.Any(b => b.Members.ContainsKey(creature.Id))
                    || creature.FightTargetKind != FightTargetKind.Creature || world.CreatureById(creature.FightTargetId) is null) continue;
                Battle? nearest = null;
                long distance = long.MaxValue;
                foreach (Battle battle in _battles)
                    if (battle.Members.Count != 0)
                    {
                        long dx = Math.Abs((long)battle.X - creature.PosX) / 4;
                        long dy = Math.Abs((long)battle.Y - creature.PosY) / 4;
                        long squared = (dx * dx >> 12) + (dy * dy >> 12);
                        if (squared < distance) { distance = squared; nearest = battle; }
                    }
                Battle chosen = nearest is not null && distance <= 0x1900 ? nearest
                    : _battles.FirstOrDefault(b => b.Members.Count == 0) ?? nearest!;
                chosen.Members.Add(creature.Id, tick.Value + 4 * SimulationTiming.TicksPerSecond);
                Recenter(chosen, world);
            }
        }
        for (int i = 0; i < _battles.Length; i++)
        {
            Battle battle = _battles[i];
            PlayerMask owners = PlayerMask.None;
            foreach (int id in battle.Members.Keys) owners = owners.With(world.CreatureById(id)!.OwnerPlayerId);
            _snapshots[i] = new BattleViewSnapshot(new BattleViewSlot(i),
                new TilePos(battle.X / Fp32.One, battle.Y / Fp32.One), owners, battle.Members.Count);
        }
    }

    private static bool Fighting(CreatureState creature) =>
        CreatureStateMachine.UpdatingCategory(creature) == CreatureStateCategory.Combat
        && creature.Dk2StateId != 202 && creature.UtilityNewState != 202 && !creature.CombatPitFighter;

    private static void Recenter(Battle battle, WorldState world)
    {
        if (battle.Members.Count == 0) { battle.X = battle.Y = 0; return; }
        long x = 0, y = 0;
        foreach (int id in battle.Members.Keys)
        {
            CreatureState creature = world.CreatureById(id)!;
            x += creature.PosX; y += creature.PosY;
        }
        battle.X = (int)(x / battle.Members.Count); battle.Y = (int)(y / battle.Members.Count);
    }
}
