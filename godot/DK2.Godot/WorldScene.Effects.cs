// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art;
using DK2.Assets;
using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Presentation;
using DK2.Simulation;
using Godot;

namespace DK2.GodotAdapter;

public partial class WorldScene : ISimulationEventSink
{
    private EffectDrawList? _effects;
    private IEffectSpriteProvider? _effectSprites;
    private ISimulationEventSink? _effectDownstream;
    private GameSimulation? _effectSimulation;
    private readonly Dictionary<AssetRef, EffectSpriteBatch?> _effectBatches = new();
    private readonly EffectQuadBuffer _effectQuad = new();

    /// <summary>Observe the real shot/effect requests before the first turn; camera entry itself emits nothing.</summary>
    public void OpenEffects(GameSimulation simulation, WorldView view, PlayerId viewer, ulong seed)
    {
        _effectSimulation = simulation;
        _effectDownstream = simulation.Events;
        WorldState world = simulation.World;
        GameDatabase database = simulation.Database;
        _effects = new EffectDrawList(database, seed,
            clock: () => new EffectTime(_frameMilliseconds),
            visible: request => WorldEffectVisibility.Construction(world, view, request),
            effectVisible: request => WorldEffectVisibility.Effect(world, view, simulation.Navigation, viewer, request),
            floorHeight: point => view.GroundHeightAt(world, point),
            captureEdges: request => CaptureEffectLayout.Boundary(world, database, request),
            attachment: (id, _) => EffectAttachment.AtPreviousBody(world.CreatureById(id),
                world.CreatureById(id) is CreatureState creature ? view.CreatureEffectGround(world, creature) : default),
            shotAttachment: id => ShotEffectAttachment.Resolve(world, simulation.Navigation, id, _lightFraction),
            cameraVisible: EffectInPlacedCamera,
            terrainFlags: tile => world.Contains(tile) ? database.Terrain[world.Tile(tile).TerrainId].Flags : 0,
            solidTile: tile => !world.Contains(tile) || NavBytes.IsSolid(simulation.Navigation.NavByte(tile.X, tile.Y)),
            shotVisible: shot => WorldEffectVisibility.Shot(world, view, shot),
            creatureGroundHeight: id => world.CreatureById(id.Value) is CreatureState creature
                ? view.CreatureEffectGround(world, creature) : null,
            viewer: viewer);
        simulation.ObserveWith(new VisibilityEventSink(view, world, viewer, this));
    }

    public void Emit(in SimEvent simEvent)
    {
        _effectDownstream?.Emit(simEvent);
        _effects?.Model.Emit(simEvent);
    }

    private bool EffectInPlacedCamera(ViewEffectRequest request)
    {
        if (View == null) return true;
        var point = RenderWorld.Ground(request.Position.X.Raw / 4096f,
            request.Position.Y.Raw / 4096f, request.Position.Z.Raw / 4096f);
        ViewFrustum frustum = PlacedFrustum;
        // The same one-tile sphere as DungeonRunner.IsPointInCamera.
        for (int i = 0; i < 6; i++)
            if (System.Numerics.Plane.DotCoordinate(frustum[i], point) < -1f) return false;
        return true;
    }

    private void AdvanceEffects()
    {
        if (_effects == null || _effectSimulation == null) return;
        _effects.CaptureShots(_effectSimulation.World.Shots, _lightFraction);
        _worldLights?.UpdateEffects(_effects.Advance(), _torchFlags.IsCheapTorch());
    }

    private void DrawEffects()
    {
        if (_effects == null || _art == null || _camera == null) return;
        _effectSprites ??= new InstallEffectSpriteProvider(_effectSimulation!.Database, _art.LoadTexture);
        _effects.TileLights ??= _worldLights!.TryGetVisibleTable;
        foreach (EffectSpriteBatch? batch in _effectBatches.Values) batch?.Begin();
        // The draw list is in RenderWorld's left-handed frame; reflect only at the upload boundary.
        System.Numerics.Vector3 right = RightHandedFrame.Point(new(_camera.GlobalBasis.X.X, _camera.GlobalBasis.X.Y, _camera.GlobalBasis.X.Z));
        System.Numerics.Vector3 up = RightHandedFrame.Point(new(_camera.GlobalBasis.Y.X, _camera.GlobalBasis.Y.Y, _camera.GlobalBasis.Y.Z));
        EffectParticleStream particles = _effects.Particles;
        for (int i = 0; i < particles.Count; i++)
        {
            ConstructionParticle particle = particles[i];
            if (!PossessionTileVisible(new TilePos(particle.XRaw >> 12, particle.YRaw >> 12))) continue;
            if (EffectDrawList.Classify(particle.Definition, particle.MeshEntry) != EffectGroupKind.Sprite) continue;
            AssetRef key = _effects.GroupKey(particle);
            if (!_effectBatches.TryGetValue(key, out EffectSpriteBatch? batch))
            {
                batch = EffectSpriteBatch.Create(this, _effectSprites, key, particle.Definition);
                _effectBatches.Add(key, batch);
            }
            if (batch == null) continue;
            _effectQuad.Clear();
            if (batch.Shape.IsFlat) _effects.AddFlatQuad(_effectQuad, particle, _effects.Fraction, batch.Frames);
            else
            {
                var at = EffectDrawList.Position(particle, _effects.Fraction);
                EffectBillboard billboard = _effects.Billboard(particle, at, batch.Shape, right, up);
                EffectDrawList.AddBillboardQuad(_effectQuad, particle, billboard, right, up, batch.Frames);
            }
            batch.Add(_effectQuad);
        }
        foreach (EffectSpriteBatch? batch in _effectBatches.Values) batch?.End();
    }

    private void FreeEffects()
    {
        foreach (EffectSpriteBatch? batch in _effectBatches.Values) batch?.Free();
        _effectBatches.Clear();
    }

    private sealed class EffectSpriteBatch
    {
        private readonly MultiMesh _instances;
        private readonly ArrayMesh _mesh;
        private readonly ShaderMaterial _material;
        private readonly ImageTexture _texture;
        private readonly Shader _shader;
        private readonly Rid _instance;
        private readonly List<Transform3D> _transforms = new();
        private readonly List<Color> _colours = new(), _uvs = new();
        public EffectSpriteShape Shape { get; }
        public int Frames { get; }

        private EffectSpriteBatch(WorldScene scene, ImageTexture texture, EffectSpriteResource sprite,
            EffectElementDef definition, int frames)
        {
            _texture = texture;
            Shape = EffectSpriteShape.Of(definition, sprite);
            Frames = frames;
            string blend = sprite.Blend == EffectSpriteBlend.Additive ? "blend_add" : "blend_mix";
            string alpha = sprite.Blend == EffectSpriteBlend.Alpha ? "c.a" : "1.0";
            _shader = new Shader
            {
                Code = "shader_type spatial;\nrender_mode unshaded, cull_disabled, depth_draw_never, " + blend + ";\n"
                    + "uniform sampler2D frame_texture : filter_linear, repeat_disable;\n"
                    + PossessionFadeDeclarations
                    + "void vertex() {\nUV = vec2(mix(INSTANCE_CUSTOM.x, INSTANCE_CUSTOM.y, UV.x), mix(INSTANCE_CUSTOM.z, INSTANCE_CUSTOM.w, 1.0 - UV.y));\n"
                    + "dk2_possession_brightness = dk2_possession_fade((MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz, PROJECTION_MATRIX);\n}\n"
                    + "void fragment() {\nvec4 c = texture(frame_texture, UV) * COLOR;\n"
                    + PossessionFadeFragment + "ALBEDO = c.rgb;\nALPHA = " + alpha + ";\n}\n",
            };
            _material = new ShaderMaterial { Shader = _shader };
            _material.SetShaderParameter("frame_texture", texture);
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = new[] { Vector3.Zero, Vector3.Right, Vector3.Right + Vector3.Up, Vector3.Up };
            arrays[(int)Mesh.ArrayType.TexUV] = new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0) };
            arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, 2, 0, 2, 3 };
            _mesh = new ArrayMesh();
            _mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            _mesh.SurfaceSetMaterial(0, _material);
            _instances = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true, UseCustomData = true, Mesh = _mesh, CustomAabb = scene._worldBox };
            _instance = RenderingServer.InstanceCreate2(_instances.GetRid(), scene.GetWorld3D().Scenario);
            RenderingServer.InstanceGeometrySetCastShadowsSetting(_instance, RenderingServer.ShadowCastingSetting.Off);
        }

        public static EffectSpriteBatch? Create(WorldScene scene, IEffectSpriteProvider provider, AssetRef key, EffectElementDef definition)
        {
            if (!provider.TryResolve(key, out ResolvedAsset asset) || !provider.TryGetSprite(asset, out EffectSpriteResource sprite)
                || !provider.TryLoadFrame(asset, 0, out var first)) return null;
            int frames = sprite.StoredFrameCount, width = first.Width, height = first.Height;
            byte[] atlas = new byte[width * frames * height * 4];
            for (int frame = 0; frame < frames; frame++)
            {
                if (!provider.TryLoadFrame(asset, frame, out var image) || image.Width != width || image.Height != height)
                    throw new InvalidOperationException("Effect sprite frames must have equal dimensions.");
                for (int row = 0; row < height; row++)
                    Buffer.BlockCopy(image.Pixels, row * width * 4, atlas, (row * width * frames + frame * width) * 4, width * 4);
            }
            using Image pixels = Image.CreateFromData(width * frames, height, false, Image.Format.Rgba8, atlas);
            return new EffectSpriteBatch(scene, ImageTexture.CreateFromImage(pixels), sprite, definition, frames);
        }

        public void Begin() { _transforms.Clear(); _colours.Clear(); _uvs.Clear(); }

        public void Add(EffectQuadBuffer quad)
        {
            var vertices = quad.Vertices;
            Vector3 origin = ToGodot(RightHandedFrame.Point(vertices[0]));
            Vector3 x = ToGodot(RightHandedFrame.Point(vertices[1])) - origin;
            Vector3 y = ToGodot(RightHandedFrame.Point(vertices[3])) - origin;
            if (x.LengthSquared() <= 0 || y.LengthSquared() <= 0) return;
            _transforms.Add(new Transform3D(new Basis(x, y, x.Cross(y).Normalized()), origin));
            EffectColour tint = quad.Colours[0];
            _colours.Add(new Color(tint.Red / 255f, tint.Green / 255f, tint.Blue / 255f));
            _uvs.Add(new Color(quad.Uvs[0].X, quad.Uvs[1].X, quad.Uvs[0].Y, quad.Uvs[3].Y));
        }

        public void End()
        {
            int count = _transforms.Count;
            if (_instances.InstanceCount < count)
                _instances.InstanceCount = Math.Max(32, Math.Max(count, _instances.InstanceCount * 2));
            _instances.VisibleInstanceCount = count;
            for (int i = 0; i < count; i++)
            {
                _instances.SetInstanceTransform(i, _transforms[i]);
                _instances.SetInstanceColor(i, _colours[i]);
                _instances.SetInstanceCustomData(i, _uvs[i]);
            }
        }

        public void Free()
        {
            RenderingServer.FreeRid(_instance);
            _instances.Dispose(); _mesh.Dispose(); _material.Dispose(); _texture.Dispose(); _shader.Dispose();
        }
    }
}
