// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Combat;

namespace DK2.Presentation;

public sealed partial class WorldView
{
    private readonly Dictionary<int, GameTick> _minimapPickups = [];
    private readonly List<int> _settledMinimapPickups = [];
    private WorldState? _minimapHeartWorld;
    private TilePos _minimapHeartTile = new(FearRules.NoHeartTile, FearRules.NoHeartTile);
    private readonly byte? _minimapHeartTerrain;

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
        MinimapMapBounds bounds = MinimapMapBounds.Fit(world.Width, world.Height);
        if (_minimapHeartWorld != world)
        {
            _minimapHeartWorld = world;
            _minimapHeartTile = new(FearRules.NoHeartTile, FearRules.NoHeartTile);
        }
        if (world.HasPlayer(_viewingPlayerId))
        {
            FindMinimapHeart(world);
            // DK2 retains the player's last Heart position after its room is ruined.
            output.Add(MinimapMarkerRules.ReportingRing(TileCentre(_minimapHeartTile),
                ReportingDistance(VariableTypeId.DungeonHeartReportingDistanceTiles), bounds, world.Width));
        }
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
                FpPosition3 position = new(new(trap.PosX), new(trap.PosY), new(0));
                Add(MinimapMarkerKind.Trap, position,
                    owner, trap.Hidden ? MinimapMarkerFlags.Hidden : 0);
                if (owner != _viewingPlayerId) continue;
                if (!_database.Traps.TryGetValue(trap.TrapId, out TrapDef? definition)) continue;
                if (definition.ShotTypeId == 13)
                {
                    if (MinimapMarkerRules.IsAlarmRingActive(world.CurrentTick, new(trap.RechargeAt)))
                        output.Add(MinimapMarkerRules.AlarmRing(position, world.CurrentTick));
                }
                else if ((definition.Flags & TrapRules.GuardPostBranch) != 0 && !trap.UnderConstruction)
                    output.Add(MinimapMarkerRules.ReportingRing(position,
                        ReportingDistance(VariableTypeId.GuardPostReportingDistanceTiles), bounds, world.Width));
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
                FpPosition3 position = new(new(creature.PosX), new(creature.PosY), new(creature.PosZ));
                int before = output.Count;
                Add(MinimapMarkerKind.Creature, position, owner, flags);
                if (output.Count != before && owner == _viewingPlayerId && creature.Possessed)
                    output.Add(MinimapMarkerRules.PossessionRing(position, world.CurrentTick));
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

    private int ReportingDistance(VariableTypeId variable) => _database.Variables.TryGet(variable, out int distance) ? distance : 0;

    private void FindMinimapHeart(WorldState world)
    {
        if (!_minimapHeartTerrain.HasValue) return;
        int minX = world.Width, minY = world.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                TileState tile = world.Tile(x, y);
                if (tile.OwnerPlayerId != _viewingPlayerId || tile.TerrainId != _minimapHeartTerrain.Value) continue;
                minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
        if (maxX >= 0) _minimapHeartTile = new((minX + maxX) / 2, (minY + maxY) / 2);
    }

    private static FpPosition3 TileCentre(TilePos position) =>
        new(new((position.X << 12) + 0x800), new((position.Y << 12) + 0x800), new(0));
}
