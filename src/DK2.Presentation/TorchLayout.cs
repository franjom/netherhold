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

/// <summary>
/// One wall torch, as a renderer needs it: a light, and a mesh when the floor's room has torch
/// art (wall-torches.md §4). Named by the floor tile it lights; that tile holds at most one.
/// </summary>
public readonly struct TorchView : IEquatable<TorchView>
{
    public TorchView(int floorTileIndex, TilePos wall, int posX, int posY, int height, int yaw,
        int red, int green, int blue, byte intensity, int radius, AssetRef mesh)
    {
        FloorTileIndex = floorTileIndex;
        Wall = wall;
        PosX = posX;
        PosY = posY;
        Height = height;
        Yaw = yaw;
        Red = red;
        Green = green;
        Blue = blue;
        Intensity = intensity;
        Radius = radius;
        Mesh = mesh;
    }

    public int FloorTileIndex { get; }

    /// <summary>The wall tile it hangs on.</summary>
    public TilePos Wall { get; }

    /// <summary>Position in <see cref="Fp32"/> tile units: centred on the shared edge, an eighth of
    /// a tile out from the wall face (table <c>0x0066CBF0</c>).</summary>
    public int PosX { get; }

    public int PosY { get; }

    /// <summary>Height above the floor, in <see cref="Fp32"/> tile units.</summary>
    public int Height { get; }

    /// <summary>Facing, 2048 to a full turn: <c>((d + 127) &lt;&lt; 9) &amp; 0x7FF</c> for face d.</summary>
    public int Yaw { get; }

    /// <summary>Light colour; a channel can exceed 255 by a room flag's +255.</summary>
    public int Red { get; }

    public int Green { get; }

    public int Blue { get; }

    public byte Intensity { get; }

    /// <summary>Reach in <see cref="Fp32"/> tile units, after the light setter's clamp.</summary>
    public int Radius { get; }

    /// <summary>The torch mesh; empty for a light with no mesh.</summary>
    public AssetRef Mesh { get; }

    public bool Equals(TorchView other) =>
        FloorTileIndex == other.FloorTileIndex && Wall.Equals(other.Wall) && PosX == other.PosX
        && PosY == other.PosY && Height == other.Height && Yaw == other.Yaw && Red == other.Red
        && Green == other.Green && Blue == other.Blue && Intensity == other.Intensity
        && Radius == other.Radius && Mesh == other.Mesh;

    public override bool Equals(object? obj) => obj is TorchView other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(FloorTileIndex, Wall, PosX, PosY, Yaw, Red, Green, Blue);
}

/// <summary>
/// DK2's wall torches (<c>0x00439B90</c>, CBridge slot <c>+0xB8</c>; wall-torches.md). A torch is an
/// engine light the simulation never reads, so it lives here. Which wall wins a floor tile facing
/// two depends on the order walls were refreshed, so unlike <see cref="WorldView"/> this keeps
/// state: it lays the level out once, then repeats DK2's refreshes for each tile whose terrain or
/// room changed.
/// <para>Deviates: the level start refreshes each floor tile's walls in row order with every tile
/// already loaded, where DK2 refreshes while it loads (Q446); the Torture Chamber's and Casino's
/// torch phase follows their wall furniture in DK2 and the plain parity here, since ours keeps no
/// furniture phase; the terrain-art gate (§1 step 3) is not tested, being inert in shipped data.</para>
/// </summary>
public sealed class TorchLayout
{
    /// <summary>The light setter's radius clamp (<c>0x0043A1E0</c>).</summary>
    public const int MaxRadius = 0x2780;

    // Faces in DK2's order N, E, S, W (0x006BC350), and the torch's offset in its floor tile.
    private static readonly int[] Dx = [0, 1, 0, -1];
    private static readonly int[] Dy = [-1, 0, 1, 0];
    private static readonly int[] OffsetX = [0x800, 0x200, 0x800, 0xE00];
    private static readonly int[] OffsetY = [0xE00, 0x800, 0x200, 0x800];

    private readonly GameDatabase _database;
    private readonly int _defaultRed, _defaultGreen, _defaultBlue, _defaultRadius, _defaultHeight;
    private readonly byte _defaultIntensity;

    private TorchView?[] _torches = [];
    private byte[] _terrain = [];
    private int _width;
    private readonly HashSet<int> _changed = [];

    public TorchLayout(GameDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _defaultRed = Variable(VariableTypeId.DefaultTorchLightRed);
        _defaultGreen = Variable(VariableTypeId.DefaultTorchLightGreen);
        _defaultBlue = Variable(VariableTypeId.DefaultTorchLightBlue);
        _defaultIntensity = (byte)Variable(VariableTypeId.DefaultTorchLightIntensity);
        _defaultRadius = Variable(VariableTypeId.DefaultTorchLightRadiusTiles);
        _defaultHeight = Variable(VariableTypeId.DefaultTorchLightHeightTiles);
    }

    /// <summary>The torch lighting a floor tile, if it has one.</summary>
    public TorchView? At(int floorTileIndex) =>
        floorTileIndex >= 0 && floorTileIndex < _torches.Length ? _torches[floorTileIndex] : null;

    /// <summary>
    /// Brings the torches up to the world, and returns the floor tiles whose torch appeared,
    /// changed or went, in ascending order. The first call, or one after the world's size changed,
    /// lays the whole level out.
    /// </summary>
    public IReadOnlyList<int> Update(WorldState world)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        _changed.Clear();

        if (_terrain.Length != world.TileCount || _width != world.Width)
        {
            _torches = new TorchView?[world.TileCount];
            _terrain = new byte[world.TileCount];
            _width = world.Width;
            for (int i = 0; i < _terrain.Length; i++) _terrain[i] = world.TileAtIndex(i).TerrainId;
            LayOut(world);
        }
        else
        {
            for (int i = 0; i < _terrain.Length; i++)
            {
                byte now = world.TileAtIndex(i).TerrainId;
                if (now == _terrain[i]) continue;
                bool roomChanged = RoomOf(_terrain[i]) != RoomOf(now);
                _terrain[i] = now;
                TerrainSet(world, i % _width, i / _width);
                if (roomChanged) Recolour(world, i % _width, i / _width);
            }
        }

        var changed = new List<int>(_changed);
        changed.Sort();
        return changed;
    }

    /// <summary>The level start: the KWD loader sets each tile's terrain in row order through the
    /// setter (<c>0x0051EC60</c> → <c>0x0044FC40</c>); its command-3 refresh of the tile itself
    /// is dropped at the bridge's closed gate, so only the command-4 refreshes of the walls
    /// around it place torches (wall-torches.md §6, HYPOTHESIS for Q446).</summary>
    private void LayOut(WorldState world)
    {
        for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
                TerrainSet(world, x, y, refreshItself: false);
    }

    /// <summary>The terrain setter's torch work (<c>0x0044FC40</c>): when the tile is not solid,
    /// each qualifying wall beside it in N, E, S, W order (command 4), then those two steps away
    /// (<c>0x00450125..0x004501B3</c>); then the tile itself (command 3), which in a running game
    /// is assumed posted (the setter posts it only with its fifth argument set).</summary>
    private void TerrainSet(WorldState world, int x, int y, bool refreshItself = true)
    {
        if (!Solid(world, x, y))
        {
            for (int d = 0; d < 4; d++)
                if (Qualifies(world, x + Dx[d], y + Dy[d])) Refresh(world, x + Dx[d], y + Dy[d]);
            for (int d = 0; d < 4; d++)
                if (Qualifies(world, x + (2 * Dx[d]), y + (2 * Dy[d]))) Refresh(world, x + (2 * Dx[d]), y + (2 * Dy[d]));
        }

        if (refreshItself) Refresh(world, x, y);
    }

    /// <summary>A room built or sold: command 16 recolours the torches on the changed tile and
    /// the tiles around it from their own room or the defaults (<c>0x00448710</c>); the mesh
    /// stays.</summary>
    private void Recolour(WorldState world, int x, int y)
    {
        for (int ty = y - 1; ty <= y + 1; ty++)
        {
            for (int tx = x - 1; tx <= x + 1; tx++)
            {
                if (!world.Contains(tx, ty)) continue;
                int index = (ty * _width) + tx;
                if (_torches[index] is not TorchView torch) continue;
                (int red, int green, int blue) = ColourOf(world, tx, ty);
                Set(index, new TorchView(index, torch.Wall, torch.PosX, torch.PosY, torch.Height, torch.Yaw,
                    red, green, blue, torch.Intensity, torch.Radius, torch.Mesh));
            }
        }
    }

    /// <summary><c>0x00439B90</c> for the wall at (x, y).</summary>
    private void Refresh(WorldState world, int x, int y)
    {
        if (!world.Contains(x, y)) return;

        // A tile that is now solid loses the torch it held as a floor.
        if (Solid(world, x, y)) Set((y * _width) + x, null);

        if (!Qualifies(world, x, y))
        {
            for (int d = 0; d < 4; d++)
            {
                int fx = x + Dx[d], fy = y + Dy[d];
                if (!world.Contains(fx, fy)) continue;
                int floor = (fy * _width) + fx;
                if (_torches[floor] is TorchView torch && torch.Wall == new TilePos(x, y)) Set(floor, null);
            }

            return;
        }

        for (int d = 0; d < 4; d++)
        {
            int fx = x + Dx[d], fy = y + Dy[d];
            if (!world.Contains(fx, fy) || Solid(world, fx, fy) || !ParityPasses(d, fx, fy)
                || world.DoorAt(fx, fy) is not null)
                continue;
            int floor = (fy * _width) + fx;
            if (_torches[floor] is not null) continue;
            Set(floor, Create(world, floor, fx, fy, x, y, d));
        }
    }

    private TorchView Create(WorldState world, int floor, int fx, int fy, int wx, int wy, int face)
    {
        RoomTorch? room = RoomOf(world.Tile(fx, fy).TerrainId)?.Torch;
        (int red, int green, int blue) = ColourOf(world, fx, fy);
        byte intensity = room?.Intensity ?? _defaultIntensity;
        int radius = Math.Min(room?.Radius ?? _defaultRadius, MaxRadius);
        int height = room?.Height ?? _defaultHeight;
        AssetRef mesh = room is null
            ? AssetCatalogue.TorchMesh(AssetCatalogue.DefaultTorchArt)
            : room.HasArt ? AssetCatalogue.TorchMesh(room.ArtName) : default;
        return new TorchView(floor, new TilePos(wx, wy),
            (fx * Fp32.One) + OffsetX[face], (fy * Fp32.One) + OffsetY[face], height,
            ((face + 127) << 9) & 0x7FF, red, green, blue, intensity, radius, mesh);
    }

    private (int Red, int Green, int Blue) ColourOf(WorldState world, int x, int y) =>
        RoomOf(world.Tile(x, y).TerrainId)?.Torch is RoomTorch room
            ? (room.Red, room.Green, room.Blue)
            : (_defaultRed, _defaultGreen, _defaultBlue);

    /// <summary><c>0x00452D80</c>: a Torch-flagged terrain that is not a door.</summary>
    private bool Qualifies(WorldState world, int x, int y) =>
        world.Contains(x, y)
        && _database.TerrainById(world.Tile(x, y).TerrainId).Flags.HasFlag(TerrainFlags.Torch)
        && world.DoorAt(x, y) is null;

    /// <summary>§2: a N or S face needs the floor's x odd, an E or W face its y even.</summary>
    private static bool ParityPasses(int face, int fx, int fy) =>
        face % 2 == 0 ? (fx & 1) == 1 : (fy & 1) == 0;

    private bool Solid(WorldState world, int x, int y) =>
        _database.TerrainById(world.Tile(x, y).TerrainId).IsSolid;

    private RoomDef? RoomOf(byte terrainId) =>
        _database.RoomsByTerrain.TryGetValue(terrainId, out RoomDef? room) ? room : null;

    private void Set(int floor, TorchView? torch)
    {
        TorchView? before = _torches[floor];
        if (before.HasValue == torch.HasValue && (!before.HasValue || before.Value.Equals(torch!.Value))) return;
        _torches[floor] = torch;
        _changed.Add(floor);
    }

    private int Variable(VariableTypeId id) => _database.Variables.TryGet(id, out int value) ? value : 0;
}
