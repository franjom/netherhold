// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

public sealed partial class WorldState
{
    private Dictionary<int, CreatureState>? _copyIndex;
    private List<(PlayerId Player, TilePos Tile)>? _staleSecretDoorKeys;

    /// <summary>A new world holding the same state as <paramref name="source"/>, sharing nothing
    /// mutable with it; <see cref="CopyFrom"/> keeps it current. It has no simulation: no generator is
    /// bound, no event fires, so it is a world to read, not to tick (ADR-0693).</summary>
    public static WorldState CopyOf(WorldState source)
    {
        var players = new PlayerState[source._players.Length];
        for (int i = 0; i < players.Length; i++)
        {
            players[i] = source._players[i];
            players[i].Hand = [];
            players[i].Availabilities = [];
            players[i].EntranceInfo = [];
            players[i].IntroducedCreatureTypes = [];
            players[i].ManufactureQueue = [];
            players[i].Blueprints = [];
        }
        var copy = new WorldState(source.Width, source.Height, new TileState[source._tiles.Length], players, source.FogOfWarEnabled);
        copy.CopyFrom(source);
        return copy;
    }

    /// <summary>Makes this world's state equal to <paramref name="source"/>'s, in place: every tile,
    /// player and collection is overwritten, and a creature, object, shot, door or trap that exists on
    /// both sides keeps its instance (<see cref="StateCopy.Sync{T}"/>). Everything <see cref="HashInto"/>
    /// covers is copied, and <c>StateCopyTests</c> holds the copy to every field by reflection. The
    /// derived caches, the events and the bound generators are this world's own and are left alone.</summary>
    public void CopyFrom(WorldState source)
    {
        if (source.Width != Width || source.Height != Height || source._players.Length != _players.Length
            || source.FogOfWarEnabled != FogOfWarEnabled)
            throw new ArgumentException("A world copies only from a world of the same map and players.", nameof(source));

        CurrentTick = source.CurrentTick;
        GameMode = source.GameMode;
        ActivePlayerOrderSlots = StateCopy.Array(ActivePlayerOrderSlots, source.ActivePlayerOrderSlots);
        Array.Copy(source.TremorSlots, TremorSlots, TremorSlotCount);
        Array.Copy(source.HeroSlotArray, HeroSlotArray, HeroSlotCount);
        RoomBorderCursorList.Clear();
        RoomBorderCursorList.AddRange(source.RoomBorderCursorList);
        Array.Copy(source._tiles, _tiles, _tiles.Length);
        NextSlabOrder = source.NextSlabOrder;
        NextRoomListPosition = source.NextRoomListPosition;
        NextPlaceClaim = source.NextPlaceClaim;
        NextTagOrder = source.NextTagOrder;
        Carry = source.Carry;
        LocalPlayerId = source.LocalPlayerId;
        CopySecretDoorEntries(source);
        for (int i = 0; i < _players.Length; i++) CopyPlayer(ref _players[i], in source._players[i]);
        Array.Copy(source.RoomBuildWaveSlots, RoomBuildWaveSlots, RoomBuildWaveSlots.Length);
        Array.Copy(source.RoomSellWaveSlots, RoomSellWaveSlots, RoomSellWaveSlots.Length);
        _pendingWork.Clear();
        _pendingWork.AddRange(source._pendingWork);

        _nextCreatureId = source._nextCreatureId;
        CreatureSlots.CopyFrom(source.CreatureSlots);
        StateCopy.Sync(_creatures, source._creatures);
        CopyOwnershipOrder(source);
        _nextObjectId = source._nextObjectId;
        StateCopy.Sync(_objects, source._objects);
        _nextShotId = source._nextShotId;
        StateCopy.Sync(_shots, source._shots);
        foreach (CreatureState creature in _creatures) creature.LinkWorld = this;
        foreach (ObjectState thing in _objects) thing.LinkWorld = this;
        foreach (ShotState shot in _shots) shot.LinkWorld = this;

        StateCopy.Sync(_actionPoints, source._actionPoints);
        TileHighlights.CopyFrom(source.TileHighlights);
        _doors.CopyFrom(source._doors);
        _navStamps.Clear();
        _navStamps.AddRange(source._navStamps);
        _traps.CopyFrom(source._traps);
        _nextChainOrder = source._nextChainOrder;
        _nextTileLinkOrder = source._nextTileLinkOrder;
        StateCopy.Sync(_heroParties, source._heroParties);
        StateCopy.Sync(_effectGenerators, source._effectGenerators);
        StateCopy.Sync(_templeSacrifices, source._templeSacrifices);
        StateCopy.Sync(_graveyards, source._graveyards);
        Triggers.CopyFrom(source.Triggers);
        Messages.CopyFrom(source.Messages);
    }

    // A struct copy takes every value field, new ones included; the lists stay this player's own.
    private static void CopyPlayer(ref PlayerState target, in PlayerState source)
    {
        List<HandItem> hand = target.Hand;
        List<AvailabilityState> availabilities = target.Availabilities;
        List<EntranceInfoState> entrances = target.EntranceInfo;
        List<byte> introduced = target.IntroducedCreatureTypes;
        List<ManufactureOrder> queue = target.ManufactureQueue;
        List<BlueprintRef> blueprints = target.Blueprints;
        target = source;
        target.Hand = Refill(hand, source.Hand);
        target.Availabilities = Refill(availabilities, source.Availabilities);
        target.EntranceInfo = Refill(entrances, source.EntranceInfo);
        target.IntroducedCreatureTypes = Refill(introduced, source.IntroducedCreatureTypes);
        target.ManufactureQueue = Refill(queue, source.ManufactureQueue);
        target.Blueprints = Refill(blueprints, source.Blueprints);

        static List<T> Refill<T>(List<T> target, List<T> source)
        {
            target.Clear();
            target.AddRange(source);
            return target;
        }
    }

    private void CopySecretDoorEntries(WorldState source)
    {
        foreach (var pair in source.SecretDoorEntries)
        {
            if (!SecretDoorEntries.TryGetValue(pair.Key, out SecretDoorTaskEntry? entry) || entry.Order != pair.Value.Order)
                SecretDoorEntries[pair.Key] = entry = new SecretDoorTaskEntry { Order = pair.Value.Order };
            Array.Copy(pair.Value.Holders, entry.Holders, entry.Holders.Length);
        }
        if (SecretDoorEntries.Count == source.SecretDoorEntries.Count) return;
        _staleSecretDoorKeys ??= [];
        foreach (var key in SecretDoorEntries.Keys)
            if (!source.SecretDoorEntries.ContainsKey(key)) _staleSecretDoorKeys.Add(key);
        foreach (var key in _staleSecretDoorKeys) SecretDoorEntries.Remove(key);
        _staleSecretDoorKeys.Clear();
    }

    private void CopyOwnershipOrder(WorldState source)
    {
        _copyIndex ??= new Dictionary<int, CreatureState>(_creatures.Count);
        _copyIndex.Clear();
        foreach (CreatureState creature in _creatures) _copyIndex.Add(creature.Id, creature);
        _creaturesByOwnership.Clear();
        foreach (CreatureState creature in source._creaturesByOwnership) _creaturesByOwnership.Add(_copyIndex[creature.Id]);
    }
}
