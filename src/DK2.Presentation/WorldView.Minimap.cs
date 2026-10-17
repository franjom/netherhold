// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Presentation;

public sealed partial class WorldView
{
    private readonly Dictionary<int, GameTick> _minimapPickups = [];
    private readonly List<int> _settledMinimapPickups = [];

    public void QueueMinimapPickupPreview(PickUpCreatureCommand command, GameTick appliesAt)
    {
        if (command.PlayerId != _viewingPlayerId) return;
        _minimapPickups[command.CreatureId] = appliesAt;
        PreviewVersion++;
    }

    public void CaptureMinimapMarkers(WorldState world, MinimapPalette palette, List<MinimapMarker> output)
    {
        output.Clear();
        _settledMinimapPickups.Clear();
        foreach (var pending in _minimapPickups)
            if (pending.Value <= world.CurrentTick) _settledMinimapPickups.Add(pending.Key);
        foreach (int id in _settledMinimapPickups) _minimapPickups.Remove(id);
        AddCreatures(_viewingPlayerId);
        for (int p = 0; p < world.PlayerCount; p++)
        {
            PlayerId owner = world.PlayerAtIndex(p).PlayerId;
            if (owner != _viewingPlayerId) AddCreatures(owner);
        }
        for (int p = 0; p < world.PlayerCount; p++)
        {
            PlayerId owner = world.PlayerAtIndex(p).PlayerId;
            for (int i = 0; i < world.Traps.Count; i++)
            {
                TrapState trap = world.Traps[i];
                if (trap.PlayerId != owner) continue;
                Add(MinimapMarkerKind.Trap, new(new(trap.PosX), new(trap.PosY), new(0)),
                    owner, trap.Hidden ? MinimapMarkerFlags.Hidden : 0);
            }
        }
        for (int p = 0; p < world.PlayerCount; p++)
        {
            PlayerId owner = world.PlayerAtIndex(p).PlayerId;
            for (int i = 0; i < world.Doors.Count; i++)
            {
                DoorState door = world.Doors[i];
                if (door.PlayerId != owner) continue;
                Add(door.Orientation == 0 ? MinimapMarkerKind.VerticalDoor : MinimapMarkerKind.HorizontalDoor,
                    new(new((door.X << 12) + 0x800), new((door.Y << 12) + 0x800), new(0)), owner, 0);
            }
        }

        void AddCreatures(PlayerId owner)
        {
            for (int i = 0; i < world.Creatures.Count; i++)
            {
                CreatureState creature = world.Creatures[i];
                if (creature.OwnerPlayerId != owner) continue;
                MinimapMarkerFlags flags = _minimapPickups.ContainsKey(creature.Id) ? MinimapMarkerFlags.PickupPending : 0;
                if (world.HasPlayer(_viewingPlayerId) && world.Player(_viewingPlayerId).Hand.Contains(HandItem.Of(creature)))
                    flags |= MinimapMarkerFlags.InViewerHand;
                Add(MinimapMarkerKind.Creature, new(new(creature.PosX), new(creature.PosY), new(creature.PosZ)), owner, flags);
            }
        }

        void Add(MinimapMarkerKind kind, FpPosition3 position, PlayerId owner, MinimapMarkerFlags flags)
        {
            var tile = new TilePos(position.X.Raw >> 12, position.Y.Raw >> 12);
            if (!world.Contains(tile.X, tile.Y)) return;
            if (!world.FogOfWarEnabled || world.Tile(tile).IsExploredBy(_viewingPlayerId)) flags |= MinimapMarkerFlags.Explored;
            var facts = new MinimapMarkerFacts(kind, position, owner, flags);
            if (MinimapMarkerRules.TryCreate(facts, _viewingPlayerId, world.CurrentTick, palette, out var marker)) output.Add(marker);
        }
    }
}
