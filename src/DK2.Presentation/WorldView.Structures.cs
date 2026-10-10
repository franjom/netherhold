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
                trap.UnderConstruction, FloorHeight(world, tile.X, tile.Y));
        }
    }
}
