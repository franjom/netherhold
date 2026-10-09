// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Generic;
using DK2.Assets;
using DK2.Core;
using DK2.Presentation;
using UnityEngine;

namespace DK2.Unity
{
    /// <summary>Turns the presenter's pushes into GameObjects: the Unity-specific half of the renderer.</summary>
    /// <remarks>
    /// <para>Almost no judgement lives here: <see cref="ScenePresenter"/> decided
    /// what changed, in what order, which asset each thing needs and create-vs-
    /// update, and the asset layer decided file or placeholder (ADR-0036). This
    /// file is the one part of the project the test suite cannot cover.</para>
    /// <para>Coordinates: the grid lies on Unity's XZ plane, Y up, X east and Z north (ADR-0411);
    /// tile <c>(x, y)</c> centres at <c>(x + 0.5, 0, -(y + 0.5))</c> (<see cref="Ground"/>) and a
    /// creature's <see cref="Fp32"/> position divides by <see cref="Fp32.One"/> — here and
    /// nowhere else (ADR-0001).</para>
    /// </remarks>
    public sealed class DungeonScene : IRenderTarget
    {
        /// <summary>DK2's ordinary floor height, 0x1000 (<c>0x004545C0</c>; pits, pools and
        /// water lie lower). Heights here are DK2's own, so a torch's 1.5 is mid-wall.</summary>
        public const float FloorHeight = 1f;

        /// <summary>The top of a tile whose terrain fills its cell: every solid terrain KMF in
        /// the install spans heights 1 to 2, a wall one tile above the floor. Public because
        /// <see cref="TilePicker"/> needs the same number to work out what the pointer is over.</summary>
        public const float SolidHeight = 2f;

        /// <summary>The top of a tile you could stand on, the floor; for the picker too.</summary>
        public const float OpenHeight = FloorHeight;

        /// <summary>A floor slab's thickness: thin, so a creature on it reads as standing on it.</summary>
        private const float SlabThickness = 0.06f;

        /// <summary>What a tile marked for digging is tinted towards. DK2 plants flags;
        /// this is a placeholder renderer, so it tints. An interface choice, not a claim
        /// about the original (ADR-0104).</summary>
        private static readonly Color TagTint = new Color(1f, 0.85f, 0.2f);

        private readonly Transform _root;
        private readonly Dictionary<int, GameObject> _tiles = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> _furniture = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> _creatures = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> _objects = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> _torches = new Dictionary<int, GameObject>();
        private readonly Dictionary<uint, Material> _materials = new Dictionary<uint, Material>();
        private readonly int _worldWidth;
        private readonly ClassicMeshes _classic;
        private readonly TerrainChunks _terrain;
        private readonly LiquidSurfaces _liquids;
        private readonly HashSet<int> _meshObjects = new HashSet<int>();
        private readonly HashSet<int> _meshCreatures = new HashSet<int>();

        private bool _warnedAboutFiles;

        /// <summary>
        /// How many tiles and creatures exist as objects. For the on-screen
        /// diagnostics, and for noticing a leak.
        /// </summary>
        public int TileObjects => _tiles.Count;
        public int CreatureObjects => _creatures.Count;
        public int ObjectObjects => _objects.Count;

        /// <summary>
        /// Set when a provider handed back a real file. Nothing loads one yet
        /// (see <see cref="Instantiate"/>), so this is how the runner can say so
        /// on screen rather than silently drawing a box.
        /// </summary>
        public bool SawFileBackedAssets { get; private set; }

        /// <summary>A point on the ground plane, from DK2's plane coordinates in tiles: X east,
        /// Y south. The one place the grid's axes meet Unity's.</summary>
        public static Vector3 Ground(float x, float y, float height = 0f) => new Vector3(x, height, -y);

        /// <param name="classic">Draws install-backed objects, creatures, torches and plain terrain
        /// with their real meshes; null keeps every one a placeholder.</param>
        /// <param name="lattice">Bends the classic terrain as DK2 does; null draws it straight.</param>
        public DungeonScene(Transform root, int worldWidth, int worldHeight, ClassicMeshes classic = null,
            TerrainLattice lattice = null)
        {
            _root = root;
            _worldWidth = worldWidth;
            _classic = classic;
            if (classic != null)
            {
                _terrain = new TerrainChunks(root, classic, worldWidth, worldHeight, lattice);
                _liquids = new LiquidSurfaces(root, classic, worldWidth, worldHeight);
            }
        }

        public void CreateTile(int tileIndex, in TileView tile, in ResolvedAsset ground, in ResolvedAsset furniture)
        {
            // Steps 1 and 2 of the classic look: plain terrain and rooms from their pieces. A room
            // drawn so needs no furniture box; its objects are step 3.
            _liquids?.Set(tileIndex, tile);
            if (_terrain != null && _terrain.TrySet(tileIndex, tile))
            {
                ApplyFurniture(tileIndex, tile, default);
                return;
            }

            GameObject go = Instantiate(ground, $"tile {tile.Position.X},{tile.Position.Y}");
            if (go != null)
            {
                Place(go, tile.Position.X, tile.Position.Y, tile.Solid);
                if (tile.Tagged)
                    SetColour(go, Color.Lerp(ColourOf(LookOf(ground)), TagTint, 0.65f));
                _tiles[tileIndex] = go;
            }

            ApplyFurniture(tileIndex, tile, furniture);
        }

        public void UpdateTile(int tileIndex, in TileView tile, in ResolvedAsset ground, in ResolvedAsset furniture)
        {
            // The terrain may have become something else entirely — a dug wall
            // becomes floor — so the ground object is rebuilt rather than
            // recoloured. Tiles change rarely (the presenter guarantees only
            // changed ones arrive), so this costs nothing in practice and avoids
            // a second, subtly different code path.
            if (_tiles.TryGetValue(tileIndex, out GameObject old) && old != null)
            {
                Object.Destroy(old);
                _tiles.Remove(tileIndex);
            }

            CreateTile(tileIndex, tile, ground, furniture);
        }

        public void CreateCreature(int creatureId, in CreatureView creature, in ResolvedAsset mesh, in ResolvedAsset animation)
        {
            // The idle KMF's first frame, unturned: the pose and the facing wait for step 4.
            GameObject go = _classic?.TryCreate(mesh, $"creature {creatureId}", _root);
            if (go != null) _meshCreatures.Add(creatureId);
            else go = Instantiate(mesh, $"creature {creatureId}");
            if (go == null) return;

            _creatures[creatureId] = go;
            PositionCreature(go, creatureId, creature, mesh.Look);
        }

        public void UpdateCreature(int creatureId, in CreatureView creature, in ResolvedAsset mesh, in ResolvedAsset animation)
        {
            if (!_creatures.TryGetValue(creatureId, out GameObject go) || go == null)
            {
                CreateCreature(creatureId, creature, mesh, animation);
                return;
            }

            PositionCreature(go, creatureId, creature, mesh.Look);
            if (_meshCreatures.Contains(creatureId)) return;

            // A creature carrying gold reads as carrying it. Standing in for the
            // real thing, which is an object an imp holds — and objects are M6.
            SetColour(go, creature.GoldHeld > 0
                ? new Color(0.95f, 0.82f, 0.25f)
                : ColourOf(mesh.Look));
        }

        public void RemoveCreature(int creatureId)
        {
            if (_creatures.TryGetValue(creatureId, out GameObject go) && go != null)
                Object.Destroy(go);
            _meshCreatures.Remove(creatureId);

            _creatures.Remove(creatureId);
        }

        public void CreateObject(int objectId, in ObjectView instance, in ResolvedAsset mesh)
        {
            // Step 0 of the classic look draws objects from the install; terrain waits for its
            // piece choice (docs/plans/2026-10-09-classic-look.md).
            GameObject go = _classic?.TryCreate(mesh, $"object {objectId}", _root);
            if (go != null) _meshObjects.Add(objectId);
            else go = Instantiate(mesh, $"object {objectId}");
            if (go == null) return;

            _objects[objectId] = go;
            PositionObject(go, objectId, instance, mesh.Look);
        }

        public void UpdateObject(int objectId, in ObjectView instance, in ResolvedAsset mesh)
        {
            if (!_objects.TryGetValue(objectId, out GameObject go) || go == null)
            {
                CreateObject(objectId, instance, mesh);
                return;
            }

            PositionObject(go, objectId, instance, mesh.Look);
        }

        public void RemoveObject(int objectId)
        {
            if (_objects.TryGetValue(objectId, out GameObject go) && go != null)
                Object.Destroy(go);

            _objects.Remove(objectId);
            _meshObjects.Remove(objectId);
        }

        /// <summary>A wall torch: a point light, and its mesh when it has one. How DK2's light
        /// numbers map onto Unity's (colour over 255, intensity over 128) is this renderer's
        /// choice, not a claim about the original (ADR-0391).</summary>
        public void CreateTorch(int floorTileIndex, in TorchView torch, in ResolvedAsset mesh)
        {
            // Unity's fake null makes ?? unsafe on its objects, hence the explicit tests.
            GameObject go = _classic?.TryCreate(mesh, $"torch {floorTileIndex}", _root);
            if (go == null)
            {
                go = Instantiate(mesh, $"torch {floorTileIndex}");
                if (go == null) go = new GameObject($"torch {floorTileIndex}");
                else go.transform.localScale = new Vector3(0.1f, 0.25f, 0.1f); // a placeholder cube is a whole tile
            }
            go.transform.SetParent(_root, false);
            go.transform.localPosition = Ground(
                torch.PosX / (float)Fp32.One, torch.PosY / (float)Fp32.One, torch.Height / (float)Fp32.One);
            // Unverified in either frame: the sign and zero of DK2's yaw are unread (ADR-0411,
            // plan step 2), so this keeps the formula it had before the frame changed.
            go.transform.localRotation = Quaternion.Euler(0f, torch.Yaw * 360f / 2048f, 0f);
            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            ApplyTorchLight(light, torch);
            _torches[floorTileIndex] = go;
        }

        public void UpdateTorch(int floorTileIndex, in TorchView torch, in ResolvedAsset mesh)
        {
            if (!_torches.TryGetValue(floorTileIndex, out GameObject go) || go == null)
            {
                CreateTorch(floorTileIndex, torch, mesh);
                return;
            }

            Light light = go.GetComponent<Light>();
            if (light != null) ApplyTorchLight(light, torch);
        }

        public void RemoveTorch(int floorTileIndex)
        {
            if (_torches.TryGetValue(floorTileIndex, out GameObject go) && go != null)
                Object.Destroy(go);

            _torches.Remove(floorTileIndex);
        }

        private static void ApplyTorchLight(Light light, in TorchView torch)
        {
            light.color = new Color(torch.Red / 255f, torch.Green / 255f, torch.Blue / 255f);
            light.intensity = torch.Intensity / 128f;
            light.range = torch.Radius / (float)Fp32.One;
        }

        /// <summary>
        /// Nothing to do: this target draws one object per thing rather than
        /// batching. A combined-mesh target would rebuild here, which is why the
        /// method exists on the interface at all.
        /// </summary>
        public void Flush()
        {
            _terrain?.Flush();
            _liquids?.Animate(Time.time);
        }

        /// <summary>Destroys everything. Pair with <see cref="ScenePresenter.Resync"/>.</summary>
        public void Clear()
        {
            foreach (GameObject go in _tiles.Values) if (go != null) Object.Destroy(go);
            foreach (GameObject go in _furniture.Values) if (go != null) Object.Destroy(go);
            foreach (GameObject go in _creatures.Values) if (go != null) Object.Destroy(go);
            foreach (GameObject go in _objects.Values) if (go != null) Object.Destroy(go);
            foreach (GameObject go in _torches.Values) if (go != null) Object.Destroy(go);
            _terrain?.Clear();
            _liquids?.Clear();

            _tiles.Clear();
            _furniture.Clear();
            _creatures.Clear();
            _objects.Clear();
            _meshObjects.Clear();
            _meshCreatures.Clear();
            _torches.Clear();
        }

        private void ApplyFurniture(int tileIndex, in TileView tile, in ResolvedAsset furniture)
        {
            if (_furniture.TryGetValue(tileIndex, out GameObject existing) && existing != null)
            {
                Object.Destroy(existing);
                _furniture.Remove(tileIndex);
            }

            if (furniture.Reference.IsEmpty) return;

            GameObject go = Instantiate(furniture, $"room {tile.Position.X},{tile.Position.Y}");
            if (go == null) return;

            // Room furniture stands on the floor rather than replacing it, so it
            // is deliberately smaller than a tile and sits above the ground.
            go.transform.localScale = new Vector3(0.6f, 0.35f, 0.6f);
            go.transform.position = Ground(tile.Position.X + 0.5f, tile.Position.Y + 0.5f, FloorHeight + 0.2f);
            _furniture[tileIndex] = go;
        }

        /// <summary>Makes a placeholder object for a resolved asset. A converter file
        /// (<see cref="ResolvedAsset.FilePath"/>) is never loaded: ADR-0408 reads the install
        /// instead, through <see cref="ClassicMeshes"/>, which draws objects so far. Anything else
        /// file- or install-backed resolves to the same primitive a placeholder does, and
        /// <see cref="SawFileBackedAssets"/> records that it happened, so the runner can say so on
        /// screen.</summary>
        private GameObject Instantiate(in ResolvedAsset asset, string name)
        {
            if (asset.Reference.IsEmpty) return null;

            if (!asset.IsPlaceholder)
            {
                SawFileBackedAssets = true;
                if (!_warnedAboutFiles)
                {
                    _warnedAboutFiles = true;
                    Debug.LogWarning(
                        $"DK2: '{asset.Reference}' resolved to {asset} but nothing draws that kind " +
                        "yet — drawing a placeholder. See DungeonScene.Instantiate.");
                }
            }

            PlaceholderShape shape = asset.IsPlaceholder
                ? asset.Look.Shape
                : PlaceholderProvider.ShapeFor(asset.Reference);

            if (shape == PlaceholderShape.None) return null;

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(_root, worldPositionStays: false);

            // Colliders on thousands of tiles cost a great deal and buy nothing
            // until something needs picking. Removed rather than never added,
            // because CreatePrimitive is the cheapest way to a cube with a mesh.
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);

            SetColour(go, ColourOf(LookOf(asset)));

            return go;
        }

        /// <summary>
        /// A tile fills its cell if the terrain does and is a thin slab if it does not.
        ///
        /// <para><b>This changed nothing you can see, and the commit that made it said
        /// otherwise.</b> Height used to come from the resolved asset's
        /// <c>HeightPermille</c> — which is a flat 1000 for a block and 40 for a slab, and
        /// hashed only for a creature, so a solid tile was already full height. Reading
        /// <see cref="TileView.Solid"/> says the same thing on purpose rather than by
        /// coincidence, and covers the one case the old code got wrong: a file-backed
        /// asset carries no placeholder look, so a real wall mesh would have drawn as a
        /// slab. Nothing loads files yet (Q71). Colour still comes from the hash.</para>
        /// </summary>
        private void Place(GameObject go, int x, int y, bool solid)
        {
            // A block stands from 0 to its top; a slab's top is the floor.
            float top = solid ? SolidHeight : OpenHeight;
            float thickness = solid ? SolidHeight : SlabThickness;

            go.transform.localScale = new Vector3(1f, thickness, 1f);
            go.transform.position = Ground(x + 0.5f, y + 0.5f, top - thickness / 2f);
        }

        private static PlaceholderLook LookOf(in ResolvedAsset asset) => asset.IsPlaceholder
            ? asset.Look
            : PlaceholderProvider.LookFor(asset.Reference);

        private void PositionCreature(GameObject go, int creatureId, in CreatureView creature, in PlaceholderLook look)
        {
            if (_meshCreatures.Contains(creatureId))
            {
                // A real mesh is modelled in tiles about its feet.
                go.transform.position = Ground(creature.PosX / (float)Fp32.One, creature.PosY / (float)Fp32.One, FloorHeight);
                return;
            }

            float height = look.HeightPermille / 1000f;
            if (height <= 0f) height = 0.5f;

            go.transform.localScale = new Vector3(0.4f, height, 0.4f);
            go.transform.position = Ground(
                creature.PosX / (float)Fp32.One,
                creature.PosY / (float)Fp32.One,
                FloorHeight + height / 2f);
        }

        /// <summary>
        /// An object sits at the centre of its tile, small and low.
        ///
        /// <para>The centre because that is all the simulation knows: an
        /// <see cref="ObjectView"/> carries a tile and not a position, and DK2's
        /// own sub-tile placement is recorded but not modelled
        /// (gold-on-the-floor.md section 7). Small and low so a bed reads as
        /// furniture on a floor rather than as another creature standing on
        /// it.</para>
        /// </summary>
        private void PositionObject(GameObject go, int objectId, in ObjectView instance, in PlaceholderLook look)
        {
            if (_meshObjects.Contains(objectId))
            {
                // A real mesh is modelled in tiles about its own origin on the floor.
                go.transform.position = Ground(instance.Tile.X + 0.5f, instance.Tile.Y + 0.5f, FloorHeight);
                return;
            }

            float height = look.HeightPermille / 1000f;
            if (height <= 0f) height = 0.25f;
            height *= 0.5f;

            go.transform.localScale = new Vector3(0.5f, height, 0.5f);
            go.transform.position = Ground(instance.Tile.X + 0.5f, instance.Tile.Y + 0.5f, FloorHeight + height / 2f);
        }

        private static Color ColourOf(in PlaceholderLook look) =>
            new Color(look.Red / 255f, look.Green / 255f, look.Blue / 255f);

        /// <summary>
        /// Sets a renderer's colour, sharing one material per colour.
        ///
        /// <para>Sharing matters: a level is thousands of objects and a material
        /// per object would mean thousands of draw calls and thousands of
        /// allocations. Colours come from a hash of the asset key, so the number
        /// of distinct ones is small and bounded by the number of asset
        /// types.</para>
        /// </summary>
        private void SetColour(GameObject go, Color colour)
        {
            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;

            uint key = ((uint)(colour.r * 255) << 16) | ((uint)(colour.g * 255) << 8) | (uint)(colour.b * 255);

            if (!_materials.TryGetValue(key, out Material material) || material == null)
            {
                material = new Material(renderer.sharedMaterial) { color = colour };
                _materials[key] = material;
            }

            renderer.sharedMaterial = material;
        }
    }
}
