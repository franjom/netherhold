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
    /// <para>Coordinates: the grid lies on Unity's XZ plane, Y up; tile
    /// <c>(x, y)</c> centres at <c>(x + 0.5, 0, y + 0.5)</c> and a creature's
    /// <see cref="Fp32"/> position divides by <see cref="Fp32.One"/> — here and
    /// nowhere else (ADR-0001). Spawning centres a creature in its tile, so the
    /// two conventions agree.</para>
    /// </remarks>
    public sealed class DungeonScene : IRenderTarget
    {
        /// <summary>A tile whose terrain fills its cell. One whole tile, so a wall is a
        /// wall. Public because <see cref="TilePicker"/> needs the same number to work
        /// out what the pointer is over.</summary>
        public const float SolidHeight = 1f;

        /// <summary>A tile you could stand on: thin enough that a creature on it reads as
        /// standing on it.</summary>
        public const float OpenHeight = 0.06f;

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

        public DungeonScene(Transform root, int worldWidth)
        {
            _root = root;
            _worldWidth = worldWidth;
        }

        public void CreateTile(int tileIndex, in TileView tile, in ResolvedAsset ground, in ResolvedAsset furniture)
        {
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
            GameObject go = Instantiate(mesh, $"creature {creatureId}");
            if (go == null) return;

            _creatures[creatureId] = go;
            PositionCreature(go, creature, mesh.Look);
        }

        public void UpdateCreature(int creatureId, in CreatureView creature, in ResolvedAsset mesh, in ResolvedAsset animation)
        {
            if (!_creatures.TryGetValue(creatureId, out GameObject go) || go == null)
            {
                CreateCreature(creatureId, creature, mesh, animation);
                return;
            }

            PositionCreature(go, creature, mesh.Look);

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

            _creatures.Remove(creatureId);
        }

        public void CreateObject(int objectId, in ObjectView instance, in ResolvedAsset mesh)
        {
            GameObject go = Instantiate(mesh, $"object {objectId}");
            if (go == null) return;

            _objects[objectId] = go;
            PositionObject(go, instance, mesh.Look);
        }

        public void UpdateObject(int objectId, in ObjectView instance, in ResolvedAsset mesh)
        {
            if (!_objects.TryGetValue(objectId, out GameObject go) || go == null)
            {
                CreateObject(objectId, instance, mesh);
                return;
            }

            PositionObject(go, instance, mesh.Look);
        }

        public void RemoveObject(int objectId)
        {
            if (_objects.TryGetValue(objectId, out GameObject go) && go != null)
                Object.Destroy(go);

            _objects.Remove(objectId);
        }

        /// <summary>A wall torch: a point light, and its mesh when it has one. How DK2's light
        /// numbers map onto Unity's (colour over 255, intensity over 128) is this renderer's
        /// choice, not a claim about the original (ADR-0391).</summary>
        public void CreateTorch(int floorTileIndex, in TorchView torch, in ResolvedAsset mesh)
        {
            // Unity's fake null makes ?? unsafe on its objects, hence the explicit tests.
            GameObject go = Instantiate(mesh, $"torch {floorTileIndex}");
            if (go == null) go = new GameObject($"torch {floorTileIndex}");
            else go.transform.localScale = new Vector3(0.1f, 0.25f, 0.1f); // a placeholder cube is a whole tile
            go.transform.SetParent(_root, false);
            go.transform.localPosition = new Vector3(
                torch.PosX / (float)Fp32.One, torch.Height / (float)Fp32.One, torch.PosY / (float)Fp32.One);
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
        }

        /// <summary>Destroys everything. Pair with <see cref="ScenePresenter.Resync"/>.</summary>
        public void Clear()
        {
            foreach (GameObject go in _tiles.Values) if (go != null) Object.Destroy(go);
            foreach (GameObject go in _furniture.Values) if (go != null) Object.Destroy(go);
            foreach (GameObject go in _creatures.Values) if (go != null) Object.Destroy(go);
            foreach (GameObject go in _objects.Values) if (go != null) Object.Destroy(go);
            foreach (GameObject go in _torches.Values) if (go != null) Object.Destroy(go);

            _tiles.Clear();
            _furniture.Clear();
            _creatures.Clear();
            _objects.Clear();
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
            go.transform.position = new Vector3(tile.Position.X + 0.5f, 0.2f, tile.Position.Y + 0.5f);
            _furniture[tileIndex] = go;
        }

        /// <summary>Makes an object for a resolved asset. Not implemented:
        /// loading a file-backed asset. A <see cref="ResolvedAsset.FilePath"/>
        /// points at converter output (glTF binary and PNG from
        /// <c>dk2tools convert</c>) and Unity ships no runtime glTF importer, so
        /// one has to be chosen and added; until then a file resolves to the same
        /// primitive a placeholder does and <see cref="SawFileBackedAssets"/>
        /// records that it happened, so the runner can say so on screen.</summary>
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
                        $"DK2: '{asset.Reference}' resolved to a file ({asset.FilePath}) but no " +
                        "loader exists yet — drawing a placeholder. See DungeonScene.Instantiate.");
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
            float height = solid ? SolidHeight : OpenHeight;

            go.transform.localScale = new Vector3(1f, height, 1f);

            // Sit on the ground plane rather than centred on it, so a slab is
            // flush and a block's base is level with it.
            go.transform.position = new Vector3(x + 0.5f, height / 2f, y + 0.5f);
        }

        private static PlaceholderLook LookOf(in ResolvedAsset asset) => asset.IsPlaceholder
            ? asset.Look
            : PlaceholderProvider.LookFor(asset.Reference);

        private void PositionCreature(GameObject go, in CreatureView creature, in PlaceholderLook look)
        {
            float height = look.HeightPermille / 1000f;
            if (height <= 0f) height = 0.5f;

            go.transform.localScale = new Vector3(0.4f, height, 0.4f);
            go.transform.position = new Vector3(
                creature.PosX / (float)Fp32.One,
                height / 2f,
                creature.PosY / (float)Fp32.One);
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
        private void PositionObject(GameObject go, in ObjectView instance, in PlaceholderLook look)
        {
            float height = look.HeightPermille / 1000f;
            if (height <= 0f) height = 0.25f;
            height *= 0.5f;

            go.transform.localScale = new Vector3(0.5f, height, 0.5f);
            go.transform.position = new Vector3(
                instance.Tile.X + 0.5f, height / 2f, instance.Tile.Y + 0.5f);
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
