// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using DK2.Assets;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

public enum StructureKind { Door, Trap }

/// <summary>Door and trap registries allocate ids independently.</summary>
public readonly record struct StructureId(StructureKind Kind, int Value);

/// <summary>How an unbuilt door or trap is drawn: DK2 replaces every material of its model with the
/// additive <c>AA_BlueTexture</c> or <c>AA_RedTexture</c> (structure-drawing.md section 5).</summary>
public enum BlueprintTint : byte { None, Blue, Red }

/// <summary>A door or trap at its tile centre; settled door poses, trap heading and viewer visibility.
/// Evidence and remaining animation limits: structure-drawing.md.</summary>
public readonly record struct StructureView(StructureId Id, TilePos Tile, PlayerId Owner, AssetRef Mesh,
    int Heading, bool UnderConstruction, int Height)
{
    public int PosX => Tile.X * Fp32.One + Fp32.One / 2;
    public int PosY => Tile.Y * Fp32.One + Fp32.One / 2;
    public int MeshYaw => (Heading + 0x400) & 0x7FF;
    public bool LastFrame => Id.Kind == StructureKind.Door;
    public AssetRef LockMesh { get; init; }

    /// <summary>None for a built structure; otherwise this turn's plan colour (<see cref="WorldView.PlanTint"/>).</summary>
    public BlueprintTint Plan { get; init; }

    /// <summary>Both are blueprints and only the plan colour differs: a blink, not a new model.</summary>
    public bool IsBlinkOf(in StructureView other) =>
        Plan != BlueprintTint.None && other.Plan != BlueprintTint.None && this with { Plan = other.Plan } == other;
}

public sealed partial class WorldView
{
    public IEnumerable<StructureView> Structures(WorldState world)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        foreach (DoorState door in world.Doors)
        {
            var tile = new TilePos(door.X, door.Y);
            if (!world.Contains(tile.X, tile.Y) || !IsVisible(world, tile)
                || (door.UnderConstruction && door.PlayerId != _viewingPlayerId)
                || !_database.Doors.TryGetValue(door.DoorId, out DoorDef? definition)) continue;
            yield return new StructureView(new StructureId(StructureKind.Door, door.Id), tile,
                new PlayerId(door.PlayerId), AssetRef.ForDoor(definition.Name, door.Open),
                door.Orientation == 0 ? 0x200 : 0, door.UnderConstruction, FloorHeight(world, tile.X, tile.Y))
            {
                LockMesh = !door.UnderConstruction && door.Health > 0 && !door.Open
                    && door.LockMode == 3 && door.LockPickTicks == 0
                    && (definition.Flags & (DoorFlags.Barricade | DoorFlags.Secret)) == 0
                    ? AssetRef.ForDoorLock(definition.Name) : default,
                Plan = door.UnderConstruction ? PlanTint(world, StructureKind.Door, new PlayerId(door.PlayerId)) : BlueprintTint.None,
            };
        }
        foreach (TrapState trap in world.Traps)
        {
            var tile = new TilePos(trap.X, trap.Y);
            if (!world.Contains(tile.X, tile.Y) || !IsVisible(world, tile)
                || ((trap.Hidden || trap.UnderConstruction) && trap.PlayerId != _viewingPlayerId)
                || !_database.Traps.TryGetValue(trap.TrapId, out TrapDef? definition)) continue;
            yield return new StructureView(new StructureId(StructureKind.Trap, trap.Id), tile,
                new PlayerId(trap.PlayerId), AssetRef.ForTrap(definition.Name), trap.Heading,
                trap.UnderConstruction, FloorHeight(world, tile.X, tile.Y))
            {
                Plan = trap.UnderConstruction ? PlanTint(world, StructureKind.Trap, new PlayerId(trap.PlayerId)) : BlueprintTint.None,
            };
        }
    }

    private WorldState? _planWorkshopWorld;
    private long _planWorkshopTick = -1;
    private PlayerId _planWorkshopPlayer;
    private bool _planWorkshopOwned;

    /// <summary>The plan colour of a blueprint this turn: blue while its keeper owns a Workshop
    /// (player <c>+0x1121</c>, refreshed each turn by <c>Player_Tick</c>), otherwise red and blue on
    /// alternate turns, a door blue on even turns and a trap red (<c>0x004A6B70</c>, <c>0x004A9F10</c>).</summary>
    public BlueprintTint PlanTint(WorldState world, StructureKind kind, PlayerId owner)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        long turn = world.CurrentTick.Value;
        if (!ReferenceEquals(world, _planWorkshopWorld) || turn != _planWorkshopTick || owner != _planWorkshopPlayer)
        {
            _planWorkshopWorld = world;
            // Once per turn and keeper, as DK2 refreshes the flag, not once per structure or frame.
            _planWorkshopTick = turn;
            _planWorkshopPlayer = owner;
            _planWorkshopOwned = OwnsWorkshopTile(world, owner);
        }
        if (_planWorkshopOwned) return BlueprintTint.Blue;
        bool even = (turn & 1) == 0;
        return even == (kind == StructureKind.Door) ? BlueprintTint.Blue : BlueprintTint.Red;
    }

    private bool OwnsWorkshopTile(WorldState world, PlayerId owner)
    {
        for (int y = 0; y < world.Height; y++)
        for (int x = 0; x < world.Width; x++)
        {
            ref TileState tile = ref world.Tile(new TilePos(x, y));
            if (tile.OwnerPlayerId == owner.Value && _database.RoomsByTerrain.TryGetValue(tile.TerrainId, out RoomDef? room)
                && room.RoomId == (byte)RoomTypeId.Workshop) return true;
        }
        return false;
    }
}
