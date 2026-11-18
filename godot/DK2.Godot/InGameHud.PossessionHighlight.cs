// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using Godot;
namespace DK2.GodotAdapter;

public partial class InGameHud
{
    private readonly PossessionHighlight _possessionHighlight = new();
    private StatusFlowers? _entryFlowers;
    private GameDatabase? _entryFlowerDatabase;
    private readonly StatusFlowerLayer[] _entryFlowerLayers = new StatusFlowerLayer[StatusFlowerLayers.MaxLayers];
    private readonly List<MeshInstance3D> _entryFlowerMeshes = new();
    private readonly Dictionary<string, ImageTexture> _entryFlowerTextures = new();
    private Shader? _entryFlowerShader;
    private WorldScene? _entryFlowerWorld;
    private void DrawPossessionHighlight(WorldHoverHit? pointerHit, HoverFeedback feedback, bool entering)
    {
        if (_simulation == null || _world == null || _possession == null) return;
        WorldHoverHit? target = PossessionHighlight.EntryPointerTarget(pointerHit, entering,
            feedback.Tinted, _tools.SpellArmed(_input) && feedback.Placement.Valid)
            ?? _possessionHighlight.Capture(_simulation.World, _simulation.Database, _viewer, _possession);
        if (target is { } hit)
            _world.DrawHoverOverlay(hit);
    }

    private void DrawPossessionEntryBadge(long milliseconds, WorldHoverHit? pointerHit, HoverFeedback feedback, bool entering)
    {
        if (_entryFlowerWorld != _world) { _entryFlowerMeshes.Clear(); _entryFlowerWorld = _world; }
        foreach (MeshInstance3D mesh in _entryFlowerMeshes) mesh.Visible = false;
        if (_simulation == null || _world?.View is not MinimapCameraView view || _sources == null) return;
        WorldHoverHit? hit = PossessionHighlight.EntryPointerTarget(pointerHit, entering, ordinaryTargetAllowed: true);
        if (hit is not { Kind: WorldHoverKind.Creature } creatureHit) return;
        if (_entryFlowerDatabase != _simulation.Database)
        {
            _entryFlowerDatabase = _simulation.Database;
            _entryFlowers = new(_simulation.Database, _viewer);
        }
        foreach (StatusFlower flower in _entryFlowers!.Capture(_simulation.World, _simulation.CurrentTick,
            new CreatureQueryTarget(creatureHit.EntityId), milliseconds))
        {
            if (!_world.TryGetCreaturePosition(new(flower.CreatureId), out System.Numerics.Vector3 feet)) continue;
            System.Numerics.Vector3 anchor = feet + System.Numerics.Vector3.UnitY * (flower.Height.Raw / (float)Fp32.One);
            int count = StatusFlowerLayers.Build(flower, _entryFlowerLayers);
            for (int i = 0; i < count; i++)
            {
                StatusFlowerLayer layer = _entryFlowerLayers[i];
                while (_entryFlowerMeshes.Count <= i)
                {
                    _entryFlowerShader ??= new Shader { Code = EntryFlowerShader };
                    var node = new MeshInstance3D { Mesh = new QuadMesh { Size = Vector2.One },
                        MaterialOverride = new ShaderMaterial { Shader = _entryFlowerShader, RenderPriority = 100 + i },
                        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                    _world.AddChild(node); _entryFlowerMeshes.Add(node);
                }
                MeshInstance3D mesh = _entryFlowerMeshes[i];
                (System.Numerics.Vector3 at, float scale) = StatusFlowerRules.LayerPlacement(anchor,
                    view.Position, view.Forward, layer.Index, StatusFlowerLayers.CentreLift);
                System.Numerics.Vector3 forward = System.Numerics.Vector3.Normalize(view.Forward);
                System.Numerics.Vector3 right = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(view.Up, forward));
                System.Numerics.Vector3 up = System.Numerics.Vector3.Cross(forward, right);
                float size = StatusFlowerRules.WorldSize(layer.Name, possessing: true) * scale;
                mesh.Transform = new Transform3D(new Basis(FlowerVector(RightHandedFrame.Direction(right)),
                    FlowerVector(RightHandedFrame.Direction(up)), FlowerVector(-RightHandedFrame.Direction(forward))).Scaled(Vector3.One * size),
                    FlowerVector(RightHandedFrame.Point(at)));
                var material = (ShaderMaterial)mesh.MaterialOverride;
                material.SetShaderParameter("art", EntryFlowerTexture(layer.Name));
                material.SetShaderParameter("tint", new Color(flower.Colour.Red / 255f, flower.Colour.Green / 255f, flower.Colour.Blue / 255f));
                material.SetShaderParameter("ring", layer.Ring);
                material.SetShaderParameter("wedge", layer.Wedge);
                mesh.Visible = true;
            }
        }
    }

    private ImageTexture EntryFlowerTexture(string name)
    {
        if (_entryFlowerTextures.TryGetValue(name, out ImageTexture? texture)) return texture;
        var rgba = _sources!.Texture(StatusFlowerLayers.Folder + name);
        using Image image = Image.CreateFromData(rgba.Width, rgba.Height, false, Image.Format.Rgba8, rgba.Pixels);
        texture = ImageTexture.CreateFromImage(image); _entryFlowerTextures.Add(name, texture);
        return texture;
    }

    private static Vector3 FlowerVector(System.Numerics.Vector3 vector) => new(vector.X, vector.Y, vector.Z);

    private const string EntryFlowerShader = """
        shader_type spatial;
        render_mode unshaded, cull_disabled, depth_draw_never, blend_mix;
        uniform sampler2D art : source_color, filter_linear, repeat_disable;
        uniform vec4 tint : source_color = vec4(1.0);
        uniform bool ring = false;
        uniform float wedge = -1.0;
        void fragment() {
            vec4 texel = texture(art, UV);
            if (ring) texel.rgb = vec3(clamp(dot(texel.rgb, vec3(0.299, 0.587, 0.114)) * 2.0, 0.0, 1.0));
            vec4 colour = texel * tint;
            if (wedge >= 0.0) {
                vec2 d = vec2(UV.x - 0.5, 0.5 - UV.y);
                float turn = atan(d.x, d.y) / 6.2831853;
                if (turn < 0.0) turn += 1.0;
                if (turn >= wedge) colour.rgb *= 0.5;
            }
            ALBEDO = colour.rgb;
            ALPHA = colour.a;
        }
        """;
}
