// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using DK2.Assets;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>Pushes what changed in the world to an <see cref="IRenderTarget"/>, and nothing that
/// did not. It owns what counts as a visible change, the push order, which asset key each thing
/// needs, and when to create versus update versus remove; the Unity adapter turns a
/// <see cref="ResolvedAsset"/> into a <c>GameObject</c> and no more.
/// <para><b>Diff rather than redraw</b>: a campaign level is 61x61, so 3,721 tiles — iterating
/// that per frame is nothing, instantiating it is not, so the presenter remembers what it last
/// pushed per tile and pushes again only when the visible part differs. <see cref="TextRenderer"/>
/// redraws everything by design, and having both proves the boundary assumes neither.</para>
/// <para><b>Order is part of the contract</b>: tiles in row order, creatures in the simulation's
/// own order — spawn order, and behaviour (ADR-0010). <b>Never reads back from the target</b>, so
/// the plan's "deleting every GameObject must not change the simulation" (§4.7) holds by
/// construction; <see cref="Resync"/> refills a target that did throw everything away.</para></summary>
public sealed class ScenePresenter
{
    /// <summary>
    /// The part of a tile a renderer can see. Compared field by field to decide
    /// whether to push.
    ///
    /// <para>Deliberately not the whole <see cref="TileView"/>: position never
    /// changes, so including it would cost a comparison per tile per frame for
    /// nothing. Everything here is something a player could point at.</para>
    /// </summary>
    private readonly struct TileVisual : IEquatable<TileVisual>
    {
        public TileVisual(in TileView tile)
        {
            Ground = tile.Ground;
            Furniture = tile.Furniture;
            OwnerPlayerId = tile.OwnerPlayerId;
            Tagged = tile.Tagged;
            GoldHeld = tile.GoldHeld;
            HealthPermille = tile.HealthPermille;
            Walls = tile.Walls;
            Pieces = tile.Pieces;
            Explored = tile.Explored;
            HasDoor = tile.HasDoor;
            RoomFloor = tile.RoomFloor;
            RoomKeys = tile.RoomKeys;
            Bed = tile.Bed;
            Surface = tile.Surface;
            AmbientTint = tile.AmbientTint;
        }

        public AssetRef Ground { get; }
        public AssetRef Furniture { get; }
        public byte OwnerPlayerId { get; }
        public bool Tagged { get; }
        public int GoldHeld { get; }
        public int HealthPermille { get; }
        public RoomWalls Walls { get; }
        public TerrainPieces Pieces { get; }
        public bool Explored { get; }
        public bool HasDoor { get; }
        public PieceSet RoomFloor { get; }
        public RoomPieceKeys? RoomKeys { get; }
        public PieceSet Bed { get; }
        public LiquidSurface Surface { get; }
        public TerrainLightColour AmbientTint { get; }

        public bool Equals(TileVisual other) =>
            Ground == other.Ground
            && Furniture == other.Furniture
            && OwnerPlayerId == other.OwnerPlayerId
            && Tagged == other.Tagged
            && GoldHeld == other.GoldHeld
            && HealthPermille == other.HealthPermille
            && Walls == other.Walls
            && Pieces == other.Pieces
            && Explored == other.Explored
            && HasDoor == other.HasDoor
            && RoomFloor == other.RoomFloor
            && ReferenceEquals(RoomKeys, other.RoomKeys)
            && Bed == other.Bed
            && Surface == other.Surface
            && AmbientTint == other.AmbientTint;

        public override bool Equals(object? obj) => obj is TileVisual other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            HashCode.Combine(Ground, Furniture, OwnerPlayerId, Tagged), GoldHeld, HealthPermille, Walls, Pieces,
            HashCode.Combine(Explored, HasDoor, RoomFloor, RoomKeys, Bed, Surface, AmbientTint));
    }

    /// <summary>The part of a creature a renderer can see.</summary>
    private readonly struct CreatureVisual : IEquatable<CreatureVisual>
    {
        public CreatureVisual(in CreatureView creature)
        {
            Mesh = creature.Mesh;
            Animation = creature.Animation;
            PosX = creature.PosX;
            PosY = creature.PosY;
            OwnerPlayerId = creature.OwnerPlayerId;
            GoldHeld = creature.GoldHeld;
            Height = creature.Height;
            MeshYaw = creature.MeshYaw;
            Pitch = creature.Pitch;
            Roll = creature.Roll;
            PreviousPosX = creature.PreviousPosX;
            PreviousPosY = creature.PreviousPosY;
            PreviousHeight = creature.PreviousHeight;
            PlaysAnimation = creature.PlaysAnimation;
            AnimationLoops = creature.AnimationLoops;
            AnimationStartedAt = creature.AnimationStartedAt;
            AnimationMovementSpeed = creature.AnimationMovementSpeed;
            AnimationPaused = creature.AnimationPaused;
            AnimationDeferred = creature.AnimationDeferred;
        }

        public AssetRef Mesh { get; }
        public AssetRef Animation { get; }
        public int PosX { get; }
        public int PosY { get; }
        public byte OwnerPlayerId { get; }
        public int GoldHeld { get; }
        public int Height { get; }
        public int MeshYaw { get; }
        public int Pitch { get; }
        public int Roll { get; }

        public int PreviousPosX { get; }
        public int PreviousPosY { get; }
        public int PreviousHeight { get; }
        public bool PlaysAnimation { get; }
        public bool AnimationLoops { get; }
        public GameTick? AnimationStartedAt { get; }
        public Fp32? AnimationMovementSpeed { get; }
        public bool AnimationPaused { get; }
        public bool AnimationDeferred { get; }

        public bool Equals(CreatureVisual other) =>
            Mesh == other.Mesh
            && Animation == other.Animation
            && PosX == other.PosX
            && PosY == other.PosY
            && OwnerPlayerId == other.OwnerPlayerId
            && GoldHeld == other.GoldHeld
            && Height == other.Height
            && MeshYaw == other.MeshYaw
            && Pitch == other.Pitch
            && Roll == other.Roll
            && PreviousPosX == other.PreviousPosX
            && PreviousPosY == other.PreviousPosY
            && PreviousHeight == other.PreviousHeight
            && PlaysAnimation == other.PlaysAnimation
            && AnimationLoops == other.AnimationLoops
            && AnimationStartedAt == other.AnimationStartedAt
            && AnimationMovementSpeed == other.AnimationMovementSpeed
            && AnimationPaused == other.AnimationPaused
            && AnimationDeferred == other.AnimationDeferred;

        public override bool Equals(object? obj) => obj is CreatureVisual other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            Mesh, Animation, PosX, PosY, OwnerPlayerId, GoldHeld,
            HashCode.Combine(Height, MeshYaw, Pitch, Roll, PreviousPosX, PreviousPosY, PlaysAnimation,
                HashCode.Combine(AnimationLoops, AnimationStartedAt, AnimationMovementSpeed, AnimationPaused, AnimationDeferred, PreviousHeight)));
    }

    /// <summary>The part of an object a renderer can see.</summary>
    private readonly struct ObjectVisual : IEquatable<ObjectVisual>
    {
        public ObjectVisual(in ObjectView instance)
        {
            Mesh = instance.Mesh;
            Tile = instance.Tile;
            OwnerPlayerId = instance.OwnerPlayerId;
            GoldHeld = instance.GoldHeld;
            PosX = instance.PosX;
            PosY = instance.PosY;
            Height = instance.Height;
            Heading = instance.Heading;
            Scale = instance.Scale;
            Pitch = instance.Pitch;
            Roll = instance.Roll;
            PreviousPosX = instance.PreviousPosX;
            PreviousPosY = instance.PreviousPosY;
            PreviousHeading = instance.PreviousHeading;
            PreviousHeight = instance.PreviousHeight;
            PreviousPitch = instance.PreviousPitch;
            PreviousRoll = instance.PreviousRoll;
            PlaysAnimation = instance.PlaysAnimation;
            AnimationStartedAt = instance.AnimationStartedAt;
            AnimationLoops = instance.AnimationLoops;
            HeartVisual = instance.HeartVisual;
        }

        public int Pitch { get; }
        public int Roll { get; }

        public int PreviousPosX { get; }
        public int PreviousPosY { get; }
        public int PreviousHeading { get; }
        public int PreviousHeight { get; }
        public int PreviousPitch { get; }
        public int PreviousRoll { get; }
        public bool PlaysAnimation { get; }
        public GameTick? AnimationStartedAt { get; }
        public bool AnimationLoops { get; }

        public DungeonHeartVisual? HeartVisual { get; }

        public AssetRef Mesh { get; }
        public TilePos Tile { get; }
        public byte OwnerPlayerId { get; }
        public int GoldHeld { get; }
        public int PosX { get; }
        public int PosY { get; }
        public int Height { get; }
        public int Heading { get; }
        public int Scale { get; }

        public bool Equals(ObjectVisual other) =>
            Mesh == other.Mesh
            && Tile.Equals(other.Tile)
            && OwnerPlayerId == other.OwnerPlayerId
            && GoldHeld == other.GoldHeld
            && PosX == other.PosX
            && PosY == other.PosY
            && Height == other.Height
            && Heading == other.Heading
            && Scale == other.Scale
            && Pitch == other.Pitch
            && Roll == other.Roll
            && PreviousPosX == other.PreviousPosX && PreviousPosY == other.PreviousPosY
            && PreviousHeading == other.PreviousHeading && PlaysAnimation == other.PlaysAnimation
            && PreviousHeight == other.PreviousHeight && PreviousPitch == other.PreviousPitch && PreviousRoll == other.PreviousRoll
            && AnimationStartedAt == other.AnimationStartedAt && AnimationLoops == other.AnimationLoops
            && HeartVisual == other.HeartVisual;

        public override bool Equals(object? obj) => obj is ObjectVisual other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            Mesh, Tile, OwnerPlayerId, GoldHeld, HashCode.Combine(PosX, PosY, Height, Heading, Scale, Pitch, Roll),
            HashCode.Combine(PreviousPosX, PreviousPosY, PreviousHeading, PlaysAnimation, AnimationStartedAt, AnimationLoops,
                HashCode.Combine(PreviousHeight, PreviousPitch, PreviousRoll, HeartVisual)));
    }

    private readonly WorldView _view;
    private readonly IAssetProvider _assets;
    private readonly IRenderTarget _target;
    private readonly TorchLayout? _torches;
    private readonly HashSet<int> _torchesDrawn = [];
    private bool _redrawTorches;

    private TileVisual[] _lastTiles = [];
    private bool[] _tileCreated = [];

    // What the tile pass last read: the tiles byte for byte, which hearts were ruined, the fog
    // flag, the doors, the crypts (a Graveyard's floor slots), the tag preview and the tick (the
    // view's room facts are per tick). While those are the same the pass would push nothing, so it
    // is not run (ADR-0456).
    private TileState[] _tileSnapshot = [];
    private ulong _ruinedSnapshot;
    private bool _fogSnapshot;
    private int _doorsSnapshot;
    private int _previewSnapshot;
    private long _tickSnapshot;
    private readonly List<long> _cryptSnapshot = [];
    private readonly List<long> _cryptScratch = [];
    private readonly Dictionary<int, CreatureVisual> _lastCreatures = [];
    private readonly List<int> _goneCreatures = [];
    private readonly HashSet<int> _seenCreatures = [];
    private readonly Dictionary<int, ObjectVisual> _lastObjects = [];
    private readonly List<int> _goneObjects = [];
    private readonly HashSet<int> _seenObjects = [];
    private readonly Dictionary<StructureId, StructureView> _lastStructures = [];

    public ScenePresenter(WorldView view, IAssetProvider assets, IRenderTarget target)
        : this(view, assets, target, null)
    {
    }

    /// <summary>With <paramref name="torches"/>, the wall torches are pushed too (ADR-0391).</summary>
    public ScenePresenter(WorldView view, IAssetProvider assets, IRenderTarget target, TorchLayout? torches)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _torches = torches;
    }

    /// <summary>How many tiles were pushed by the most recent <see cref="Present"/>.</summary>
    public int TilesPushed { get; private set; }

    /// <summary>How many creatures were pushed by the most recent <see cref="Present"/>.</summary>
    public int CreaturesPushed { get; private set; }

    /// <summary>How many objects were pushed by the most recent <see cref="Present"/>.</summary>
    public int ObjectsPushed { get; private set; }

    /// <summary>How many torches were pushed by the most recent <see cref="Present"/>.</summary>
    public int TorchesPushed { get; private set; }

    /// <summary>
    /// Forgets everything, so the next <see cref="Present"/> creates the whole
    /// scene again. For a target that discarded what it drew — a Unity scene
    /// reload, a camera moving to a different level.
    /// </summary>
    public void Resync()
    {
        _lastTiles = [];
        _tileCreated = [];
        _tileSnapshot = [];
        _lastCreatures.Clear();
        _lastObjects.Clear();
        _lastStructures.Clear();
        _torchesDrawn.Clear();
        _redrawTorches = true;
    }

    /// <summary>
    /// Pushes the current state of the world to the target.
    ///
    /// <para>Safe to call every frame or every tick; the first call creates
    /// everything and later calls push only differences. A world whose
    /// dimensions changed is treated as a different world and resynced, because
    /// a tile index means nothing across a resize.</para>
    /// </summary>
    public void Present(WorldState world)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));

        TilesPushed = 0;
        CreaturesPushed = 0;
        ObjectsPushed = 0;
        TorchesPushed = 0;

        if (_lastTiles.Length != world.TileCount)
        {
            _lastTiles = new TileVisual[world.TileCount];
            _tileCreated = new bool[world.TileCount];
            _tileSnapshot = [];
        }

        if (!TilesUnchanged(world))
        {
            PresentTiles(world);
            SnapshotTiles(world);
        }
        PresentTorches(world);

        // Objects before creatures, because a creature stands on an object and
        // the text renderer draws them in that precedence -- one order, decided
        // here, so two renderers cannot disagree about what occludes what.
        PresentObjects(world);
        PresentStructures(world);
        PresentCreatures(world);

        _target.Flush();
    }

    /// <summary>Whether every input of the tile pass is what the last pass saw. The tile view reads
    /// the tiles (own and neighbours), the owner's ruined heart, the fog flag, the door on a tile,
    /// the crypts (a Graveyard's floor slots), the tag preview and its per-tick room facts; the
    /// viewing player and the database are fixed for the view's life.</summary>
    private bool TilesUnchanged(WorldState world)
    {
        ReadOnlySpan<TileState> tiles = world.Tiles;
        if (tiles.Length != _tileSnapshot.Length
            || _fogSnapshot != world.FogOfWarEnabled
            || _doorsSnapshot != world.DoorsVersion
            || _previewSnapshot != _view.PreviewVersion
            || _tickSnapshot != world.CurrentTick.Value
            || _ruinedSnapshot != RuinedHearts(world))
            return false;
        Crypts(world, _cryptScratch);
        if (_cryptScratch.Count != _cryptSnapshot.Count) return false;
        for (int i = 0; i < _cryptScratch.Count; i++)
            if (_cryptScratch[i] != _cryptSnapshot[i]) return false;
        return MemoryMarshal.AsBytes(tiles).SequenceEqual(MemoryMarshal.AsBytes(_tileSnapshot.AsSpan()));
    }

    private void SnapshotTiles(WorldState world)
    {
        ReadOnlySpan<TileState> tiles = world.Tiles;
        if (_tileSnapshot.Length != tiles.Length) _tileSnapshot = new TileState[tiles.Length];
        tiles.CopyTo(_tileSnapshot);
        _fogSnapshot = world.FogOfWarEnabled;
        _doorsSnapshot = world.DoorsVersion;
        _previewSnapshot = _view.PreviewVersion;
        _tickSnapshot = world.CurrentTick.Value;
        _ruinedSnapshot = RuinedHearts(world);
        Crypts(world, _cryptSnapshot);
    }

    /// <summary>The crypts, as a Graveyard's floor slots read them: tile and whether held.</summary>
    private static void Crypts(WorldState world, List<long> into)
    {
        into.Clear();
        foreach (ObjectState thing in world.Objects)
            if (thing.ObjectTypeId == AutoFurnitureRules.CryptObjectId)
                into.Add(((long)thing.TileIndex << 1) | (thing.InHand ? 1L : 0L));
    }

    /// <summary>One bit per player slot whose heart is ruined.</summary>
    private static ulong RuinedHearts(WorldState world)
    {
        ulong mask = 0;
        for (int i = 0; i < world.PlayerCount && i < 64; i++)
            if (world.PlayerAtIndex(i).HeartRuined) mask |= 1UL << i;
        return mask;
    }

    private void PresentTiles(WorldState world)
    {
        for (int y = 0; y < world.Height; y++)
        {
            for (int x = 0; x < world.Width; x++)
            {
                int index = (y * world.Width) + x;
                TileView tile = _view.TileAt(world, x, y);
                var visual = new TileVisual(tile);

                if (_tileCreated[index] && _lastTiles[index].Equals(visual)) continue;

                ResolvedAsset ground = Resolve(tile.Ground);
                ResolvedAsset furniture = Resolve(tile.Furniture);

                if (_tileCreated[index])
                    _target.UpdateTile(index, tile, ground, furniture);
                else
                    _target.CreateTile(index, tile, ground, furniture);

                _lastTiles[index] = visual;
                _tileCreated[index] = true;
                TilesPushed++;
            }
        }
    }

    /// <summary>The torches after the tiles they hang on, in floor-tile order.</summary>
    private void PresentTorches(WorldState world)
    {
        if (_torches is null) return;
        IReadOnlyList<int> changed = _torches.Update(world);
        if (_redrawTorches)
        {
            _redrawTorches = false;
            var all = new List<int>();
            for (int i = 0; i < world.TileCount; i++)
                if (_torches.At(i) is not null || _torchesDrawn.Contains(i)) all.Add(i);
            changed = all;
        }

        foreach (int index in changed)
        {
            if (_torches.At(index) is TorchView torch)
            {
                ResolvedAsset mesh = Resolve(torch.Mesh);
                if (_torchesDrawn.Add(index)) _target.CreateTorch(index, torch, mesh);
                else _target.UpdateTorch(index, torch, mesh);
            }
            else if (_torchesDrawn.Remove(index))
            {
                _target.RemoveTorch(index);
            }
            else
            {
                continue;
            }

            TorchesPushed++;
        }
    }

    private void PresentCreatures(WorldState world)
    {
        _seenCreatures.Clear();

        foreach (CreatureView creature in _view.Creatures(world))
        {
            _seenCreatures.Add(creature.Id);
            var visual = new CreatureVisual(creature);

            bool known = _lastCreatures.TryGetValue(creature.Id, out CreatureVisual last);
            if (known && last.Equals(visual)) continue;

            ResolvedAsset mesh = Resolve(creature.Mesh);
            ResolvedAsset animation = Resolve(creature.Animation);

            if (known)
                _target.UpdateCreature(creature.Id, creature, mesh, animation);
            else
                _target.CreateCreature(creature.Id, creature, mesh, animation);

            _lastCreatures[creature.Id] = visual;
            CreaturesPushed++;
        }

        // Anything we knew about and did not see has died or been removed.
        // Collected first rather than removed mid-enumeration, and in ascending
        // id order so the call sequence is reproducible.
        _goneCreatures.Clear();
        foreach (int id in _lastCreatures.Keys)
            if (!_seenCreatures.Contains(id)) _goneCreatures.Add(id);

        _goneCreatures.Sort();

        foreach (int id in _goneCreatures)
        {
            _target.RemoveCreature(id);
            _lastCreatures.Remove(id);
        }
    }

    /// <summary>The simulation's objects, then the view's decorations.</summary>
    private IEnumerable<ObjectView> Scenery(WorldState world)
    {
        foreach (ObjectView instance in _view.Objects(world)) yield return instance;
        foreach (ObjectView decoration in _view.Decorations(world)) yield return decoration;
    }

    private void PresentStructures(WorldState world)
    {
        var seen = new HashSet<StructureId>();
        foreach (StructureView structure in _view.Structures(world))
        {
            seen.Add(structure.Id);
            bool known = _lastStructures.TryGetValue(structure.Id, out StructureView last);
            if (known && last == structure) continue;
            ResolvedAsset mesh = Resolve(structure.Mesh);
            if (known) _target.UpdateStructure(structure.Id, structure, mesh);
            else _target.CreateStructure(structure.Id, structure, mesh);
            _lastStructures[structure.Id] = structure;
        }
        var gone = new List<StructureId>();
        foreach (StructureId id in _lastStructures.Keys)
            if (!seen.Contains(id)) gone.Add(id);
        gone.Sort((left, right) => left.Kind != right.Kind
            ? left.Kind.CompareTo(right.Kind) : left.Value.CompareTo(right.Value));
        foreach (StructureId id in gone)
        {
            _target.RemoveStructure(id);
            _lastStructures.Remove(id);
        }
    }

    private void PresentObjects(WorldState world)
    {
        _seenObjects.Clear();

        foreach (ObjectView instance in Scenery(world))
        {
            _seenObjects.Add(instance.Id);
            var visual = new ObjectVisual(instance);

            bool known = _lastObjects.TryGetValue(instance.Id, out ObjectVisual last);
            if (known && last.Equals(visual)) continue;

            ResolvedAsset mesh = Resolve(instance.Mesh);

            if (known)
                _target.UpdateObject(instance.Id, instance, mesh);
            else
                _target.CreateObject(instance.Id, instance, mesh);

            _lastObjects[instance.Id] = visual;
            ObjectsPushed++;
        }

        // Same rule as creatures: anything we knew about and did not see is
        // gone, collected first and removed in ascending id order so the call
        // sequence is reproducible.
        _goneObjects.Clear();
        foreach (int id in _lastObjects.Keys)
            if (!_seenObjects.Contains(id)) _goneObjects.Add(id);

        _goneObjects.Sort();

        foreach (int id in _goneObjects)
        {
            _target.RemoveObject(id);
            _lastObjects.Remove(id);
        }
    }

    /// <summary>
    /// Resolves a key, tolerating the empty one.
    ///
    /// <para>An empty key is normal — a tile that is not a room has no
    /// furniture — so it resolves to nothing rather than throwing. Everything
    /// else must resolve, because the chain ends in a total provider; a miss
    /// there means the chain was built without a placeholder, which is a
    /// configuration bug and not a missing file.</para>
    /// </summary>
    private ResolvedAsset Resolve(AssetRef reference)
    {
        if (reference.IsEmpty) return default;

        if (_assets.TryResolve(reference, out ResolvedAsset asset)) return asset;

        throw new InvalidOperationException(
            $"nothing resolved '{reference}'. With a placeholder in the chain this cannot " +
            "happen, so the chain was built without one.");
    }
}
