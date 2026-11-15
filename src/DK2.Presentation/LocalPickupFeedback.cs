// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;
namespace DK2.Presentation;
/// <summary>Local pickup feedback, separate from authoritative hand insertion; see 0040DD20 and 0040E0D0.</summary>
public sealed class LocalPickupFeedback
{
    private sealed class Pending(PlayerId player, int id, byte type, long milliseconds)
    {
        public readonly PlayerId Player = player;
        public readonly int Id = id;
        public readonly byte Type = type;
        public readonly long Milliseconds = milliseconds;
        public DropCommand? Drop;
        public bool DropDispatched;
    }
    private readonly List<Pending> _pending = [];
    private readonly List<(PlayerId Player, CreatureId Creature, long Milliseconds)> _dust = [];
    public event Action<ViewEffectRequest>? DustRequested;
    public int Count => _pending.Count;
    public bool Contains(CreatureId creatureId) => _pending.Exists(p => p.Id == creatureId.Value);
    public bool TryQueue(WorldState world, GameDatabase database, PlayerId player, CreatureId creatureId, long milliseconds)
    {
        Advance(world, milliseconds);
        CreatureState? creature = world.CreatureById(creatureId.Value);
        if (!world.HasPlayer(player) || creature is null || creature.Health < 1 || Contains(creatureId)
            || !HandRules.CanPickUp(world, database, creature, player)) return false;
        int count = world.Player(player).Hand.Count;
        foreach (Pending pending in _pending)
            if (pending.Player == player) count++;
        if (count >= HandRules.Capacity(database)) return false;
        _pending.Add(new Pending(player, creatureId.Value, creature.CreatureTypeId, milliseconds));
        if (DustRequested is { } requestDust)
        {
            _dust.Add((player, creatureId, milliseconds));
            requestDust(new ViewEffectRequest(new EffectId(MovementDustSystem.FootDustEffectId), player,
                new FpPosition3(new(creature.PosX), new(creature.PosY), new(0x1000)),
                new Dk2Angle(unchecked((ushort)creature.Heading))) { SourceCreatureId = creatureId });
        }
        return true;
    }
    public void Advance(WorldState world, long milliseconds)
    {
        _dust.RemoveAll(p => milliseconds - p.Milliseconds >= 2001);
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            Pending pending = _pending[i];
            CreatureState? creature = world.CreatureById(pending.Id);
            bool held = IsHeld(world, pending);
            if (creature is null || held && pending.Drop is null
                || !held && (pending.DropDispatched || milliseconds - pending.Milliseconds >= 2001)) _pending.RemoveAt(i);
        }
    }
    public IEnumerable<HeldIcon> Icons(WorldState world, PlayerId player)
    {
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            Pending pending = _pending[i];
            if (pending.Player == player && pending.Drop is null && !IsHeld(world, pending))
                yield return new HeldIcon(HeldIconKind.Creature, pending.Type);
        }
    }
    public bool TryQueueDrop(DropCommand command)
    {
        for (int i = _pending.Count - 1; i >= 0; i--)
            if (_pending[i].Player == command.PlayerId && _pending[i].Drop is null)
            {
                _pending[i].Drop = command;
                return true;
            }
        return false;
    }
    public void DrainReadyDrops(WorldState world, List<PlayerCommand> output)
    {
        foreach (Pending pending in _pending)
        {
            if (pending.Drop is not null && !pending.DropDispatched && IsHeld(world, pending))
            {
                output.Add(pending.Drop);
                pending.DropDispatched = true;
            }
        }
    }
    public bool HidesHeld(PlayerId player, HandItem item) => !item.IsObject
        && _pending.Exists(p => p.Player == player && p.Id == item.Id && p.Drop is not null);
    public bool HasVisiblePendingItems(WorldState world, PlayerId player)
    {
        foreach (HeldIcon icon in Icons(world, player)) return true;
        return false;
    }
    public bool HasVisibleItems(WorldState world, PlayerId player)
    {
        if (HasVisiblePendingItems(world, player)) return true;
        if (world.HasPlayer(player))
            foreach (HandItem item in world.Player(player).Hand)
                if (!HidesHeld(player, item)) return true;
        return false;
    }
    public bool HoldingGold(WorldState world, PlayerId player)
    {
        if (HasVisiblePendingItems(world, player) || !world.HasPlayer(player)) return false;
        List<HandItem> hand = world.Player(player).Hand;
        if (hand.Count == 0) return false;
        HandItem item = hand[hand.Count - 1];
        return item.IsObject && world.ObjectById(item.Id) is ObjectState thing
            && thing.ObjectTypeId is 1 or 2 or 3;
    }
    private static bool IsHeld(WorldState world, Pending pending) => world.HasPlayer(pending.Player)
        && world.Player(pending.Player).Hand.Exists(item => !item.IsObject && item.Id == pending.Id);
    internal bool ConsumeDust(ViewEffectRequest request)
    {
        if (request.EffectId.Value != MovementDustSystem.FootDustEffectId || request.SourceCreatureId == CreatureId.None) return false;
        int index = _dust.FindIndex(p => p.Player == request.Owner && p.Creature == request.SourceCreatureId);
        if (index < 0) return false;
        _dust.RemoveAt(index);
        return true;
    }
    public void Reset() { _pending.Clear(); _dust.Clear(); }
}
