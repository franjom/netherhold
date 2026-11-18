// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Presentation;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace DK2.GodotAdapter;

/// <summary>Real torch shadows on DK2's per-pixel light (<c>--light-shadows</c> with <c>--light-perpixel</c>), a modern
/// addition DK2 does not have: the <see cref="ClassicTorchShadowSlots.MaximumTorches"/> torches nearest the focus each get
/// a distance map looking down from the torch (a <see cref="SubViewport"/> whose wide perspective camera only sees the
/// casters' layers; the world shaders write distances there, <see cref="ShadowFragment"/>), and the
/// per-pixel light multiplies only that torch's DK2 term by its visibility (<see cref="ShadowLightFunctions"/>). A map
/// texel holds the nearest caster's distance and its darkening: DK2's strength (<see cref="ClassicMeshShadow.Strength"/>)
/// with a creature's or object's fade from DK2's caster rule (<see cref="ClassicShadowSourceSelector"/>: the three nearest
/// torches within 2.5 tiles, fading from the fourth), and the terrain's over the 2.5-tile range. Off, nothing here exists
/// and every shader is the per-pixel one, byte for byte.</summary>
public partial class WorldScene
{
    /// <summary>The render layers (13 to 16) a shadow view's camera sees, one bit a view; casters carry all of them and
    /// layer 1. A camera without layer 1 is a shadow view (the shaders read <c>CAMERA_VISIBLE_LAYERS</c>). One view a
    /// torch, looking straight down over <see cref="ShadowFieldOfView"/>: a dual paraboloid misplaced the terrain's large
    /// triangles, and the floor and the creatures are below the torch; a receiver outside the view is unshadowed.</summary>
    private const int ShadowLayerShift = 12, ShadowViews = ClassicTorchShadowSlots.MaximumTorches, ShadowMapSize = 512;
    private const float ShadowFieldOfView = 150f; // DK2_SHADOW_TAN in ShadowFunctions: tan(75 degrees)
    private const uint ShadowCasterLayers = 1u | (((1u << ShadowViews) - 1u) << ShadowLayerShift);
    /// <summary>A map's reach from its torch in tiles: past DK2's 2.5 horizontally, where every strength is zero, with the
    /// height above; distances are stored over it.</summary>
    private const float ShadowRange = 4f; // DK2_SHADOW_RANGE in ShadowFunctions
    /// <summary>How far from the focus a torch may be and still get a map, in tiles.</summary>
    private const float ShadowReach = 14f;

    private static readonly StringName ShadowFadeParameter = "dk2_shadow_fade";
    private static readonly StringName[] ShadowMapGlobals =
        ["dk2_shadow_map0", "dk2_shadow_map1", "dk2_shadow_map2", "dk2_shadow_map3"];
    private static readonly StringName[] ShadowLightGlobals =
        ["dk2_shadow_light0", "dk2_shadow_light1", "dk2_shadow_light2", "dk2_shadow_light3"];

    private readonly Dictionary<int, NVector3> _shadowTorches = new();
    private int[] _shadowKeys = [];
    private NVector3[] _shadowPositions = [];
    private bool _shadowTorchesChanged;
    private readonly int[] _shadowChosen = new int[ClassicTorchShadowSlots.MaximumTorches];
    private readonly int[] _shadowSlotKeys = [-1, -1, -1, -1];
    private readonly NVector3[] _shadowSlotPositions = new NVector3[ClassicTorchShadowSlots.MaximumTorches];
    private readonly ClassicShadowSourceSelector _shadowSelector = new();
    private SubViewport[]? _shadowViewports;
    private Camera3D[]? _shadowCameras;

    /// <summary>Real torch shadows (<c>--light-shadows</c>), drawn only with <see cref="PerPixelLight"/>. Set before
    /// <see cref="Open"/>.</summary>
    public bool TorchShadows { get; set; }

    private bool TorchShadowsActive => TorchShadows && PerPixelLight;

    /// <summary>The shadow views and the globals the shaders read; nothing when the shadows are off.</summary>
    private void OpenShadows()
    {
        if (!TorchShadowsActive) return;
        _shadowViewports = new SubViewport[ShadowViews];
        _shadowCameras = new Camera3D[ShadowViews];
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = Colors.White,
            TonemapMode = Godot.Environment.ToneMapper.Linear,
            TonemapExposure = 1f,
            TonemapWhite = 1f,
        };
        for (int view = 0; view < ShadowViews; view++)
        {
            var viewport = new SubViewport
            {
                Name = "TorchShadow" + view,
                Size = new Vector2I(ShadowMapSize, ShadowMapSize),
                // Float output, no sRGB encoding: the texels are distances (renderer_scene_render_rd.cpp, convert_to_srgb).
                UseHdr2D = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
                PositionalShadowAtlasSize = 0,
            };
            var camera = new Camera3D
            {
                Fov = ShadowFieldOfView,
                Near = 0.01f,
                Far = ShadowRange,
                CullMask = 1u << (ShadowLayerShift + view),
                Environment = environment,
            };
            viewport.AddChild(camera);
            AddChild(viewport);
            camera.Current = true;
            _shadowViewports[view] = viewport;
            _shadowCameras[view] = camera;
            Global(ShadowMapGlobals[view], RenderingServer.GlobalShaderParameterType.Sampler2D, viewport.GetTexture());
        }
        for (int slot = 0; slot < ShadowLightGlobals.Length; slot++)
        {
            _shadowSlotKeys[slot] = -1;
            Global(ShadowLightGlobals[slot], RenderingServer.GlobalShaderParameterType.Vec4, Vector4.Zero);
        }
    }

    /// <summary>A torch's light for the shadow slots (<see cref="SetTorchLight"/>), null when unlit or gone.</summary>
    private void SetShadowTorch(int floorTileIndex, ClassicPointLight? light)
    {
        if (!TorchShadowsActive) return;
        if (light is ClassicPointLight lit) _shadowTorches[floorTileIndex] = lit.Position;
        else if (!_shadowTorches.Remove(floorTileIndex)) return;
        _shadowTorchesChanged = true;
    }

    /// <summary>The frame's shadow slots and each caster's fades: the nearest torches to the focus get the views (placed
    /// and rendered only while held; a slot's global names its light as the tables hold it), and each creature, object
    /// and structure near one of them takes DK2's fade for that torch when it is among its three. Allocation-free once
    /// the torches are listed.</summary>
    private void PrepareShadows()
    {
        if (_shadowViewports == null || _shadowCameras == null) return;
        if (_shadowTorchesChanged)
        {
            _shadowTorchesChanged = false;
            if (_shadowKeys.Length != _shadowTorches.Count)
            {
                _shadowKeys = new int[_shadowTorches.Count];
                _shadowPositions = new NVector3[_shadowTorches.Count];
            }
            int i = 0;
            foreach (KeyValuePair<int, NVector3> torch in _shadowTorches)
            {
                _shadowKeys[i] = torch.Key;
                _shadowPositions[i++] = torch.Value;
            }
        }
        int count = ClassicTorchShadowSlots.Choose(Focus, _shadowKeys, _shadowPositions, ShadowReach, _shadowChosen);
        for (int slot = 0; slot < ClassicTorchShadowSlots.MaximumTorches; slot++)
        {
            int key = slot < count ? _shadowKeys[_shadowChosen[slot]] : -1;
            if (key == _shadowSlotKeys[slot]) continue;
            _shadowSlotKeys[slot] = key;
            if (key < 0)
            {
                RenderingServer.GlobalShaderParameterSet(ShadowLightGlobals[slot], Vector4.Zero);
                _shadowViewports[slot].RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
                continue;
            }
            NVector3 at = _shadowPositions[_shadowChosen[slot]];
            _shadowSlotPositions[slot] = at;
            // Godot's frame, as LightTables writes a source's position: Z negated.
            var point = new Vector3(at.X, at.Y, -at.Z);
            RenderingServer.GlobalShaderParameterSet(ShadowLightGlobals[slot], new Vector4(point.X, point.Y, point.Z, 1f));
            // Looking down (-Y), up along -Z: dk2_shadow_sample's projection.
            _shadowCameras[slot].GlobalTransform = new Transform3D(
                new Basis(new Vector3(1, 0, 0), new Vector3(0, 0, -1), new Vector3(0, 1, 0)), point);
            _shadowViewports[slot].RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        }
        foreach (KeyValuePair<int, CreatureSlot> entry in _creatures) ShadowFades(entry.Value.Slot);
        foreach (KeyValuePair<int, ModelSlot> entry in _objects) ShadowFades(entry.Value);
        foreach (KeyValuePair<StructureId, ModelSlot> entry in _structures) ShadowFades(entry.Value);
    }

    /// <summary>A caster's fade per slot: zero unless that slot's torch is one of its DK2 sources; written to its
    /// instance only when it changed.</summary>
    private void ShadowFades(ModelSlot slot)
    {
        NVector3 centre = slot.ShadowCentre;
        const float range = ClassicMeshShadow.MaximumDistance * ClassicMeshShadow.MaximumDistance;
        bool near = false;
        for (int i = 0; i < ClassicTorchShadowSlots.MaximumTorches && !near; i++)
            near = _shadowSlotKeys[i] >= 0 && NVector3.DistanceSquared(centre, _shadowSlotPositions[i]) < range;
        Vector4 fades = default;
        if (near)
        {
            _shadowSelector.Begin(centre);
            for (int i = 0; i < _shadowKeys.Length; i++) _shadowSelector.Offer(_shadowKeys[i], _shadowPositions[i]);
            fades = new Vector4(Fade(0), Fade(1), Fade(2), Fade(3));
        }
        if (fades == slot.ShadowFades) return;
        slot.ShadowFades = fades;
        slot.Node.SetInstanceShaderParameter(ShadowFadeParameter, fades);

        float Fade(int i) => _shadowSlotKeys[i] >= 0 ? _shadowSelector.FadeFor(_shadowSlotKeys[i]) : 0f;
    }

    /// <summary>The per-pixel light with each torch term times its shadow visibility (<c>dk2_shadow_visibility</c>);
    /// everything else is <see cref="LightFunctions"/> unchanged. Built on first use: static initialisers across the
    /// partial files run in no defined order.</summary>
    private static string ShadowLightFunctions => s_shadowLightFunctions ??= WithShadows(LightFunctions);
    private static string? s_shadowLightFunctions;

    private static string WithShadows(string functions)
    {
        string result = functions
            .Replace("vec3 dk2_table_sum(", ShadowFunctions + "vec3 dk2_table_sum(")
            .Replace("vec3 c;\n", "vec3 c;\nfloat vis = dk2_shadow_visibility(pr.xyz, pws, nws);\n")
            .Replace("sum += dk2_radial(origin, pr, ci);", "sum += dk2_radial(origin, pr, ci) * vis;")
            .Replace("sum += c;", "sum += c * vis;");
        if (result.Split("* vis;").Length != 5) throw new InvalidOperationException("The light functions changed shape.");
        return result;
    }

    /// <summary>The maps and their lookup: a receiver's distance from the torch against the nearest caster's in the
    /// view's texel toward it (2x2 taps, bilinear), darkened by that caster's strength.</summary>
    private static readonly string ShadowFunctions = $$"""
        global uniform sampler2D dk2_shadow_map0;
        global uniform sampler2D dk2_shadow_map1;
        global uniform sampler2D dk2_shadow_map2;
        global uniform sampler2D dk2_shadow_map3;
        global uniform vec4 dk2_shadow_light0;
        global uniform vec4 dk2_shadow_light1;
        global uniform vec4 dk2_shadow_light2;
        global uniform vec4 dk2_shadow_light3;
        const float DK2_SHADOW_RANGE = 4.0;
        const float DK2_SHADOW_TAN = 3.7320508;
        const int DK2_SHADOW_SIZE = {{ShadowMapSize}};
        vec4 dk2_shadow_light(int t) {
            return t == 0 ? dk2_shadow_light0 : t == 1 ? dk2_shadow_light1 : t == 2 ? dk2_shadow_light2 : dk2_shadow_light3;
        }
        vec2 dk2_shadow_texel(int t, ivec2 at) {
            if (t == 0) return texelFetch(dk2_shadow_map0, at, 0).rg;
            if (t == 1) return texelFetch(dk2_shadow_map1, at, 0).rg;
            if (t == 2) return texelFetch(dk2_shadow_map2, at, 0).rg;
            return texelFetch(dk2_shadow_map3, at, 0).rg;
        }
        float dk2_shadow_tap(int t, ivec2 at, float reach) {
            vec2 s = dk2_shadow_texel(t, clamp(at, ivec2(0), ivec2(DK2_SHADOW_SIZE - 1)));
            return s.r * DK2_SHADOW_RANGE < reach ? s.g : 0.0;
        }
        float dk2_shadow_sample(int t, vec3 l, vec3 p, vec3 n) {
            // Normal offset and slope bias by the texel's size at the receiver: a surface the light grazes covers many
            // distances in one texel. The normal is turned toward the light (DK2's terrain tops carry inverted ones).
            vec3 to_light = normalize(l - p);
            vec3 facing = dot(n, to_light) < 0.0 ? -n : n;
            float cosine = max(dot(facing, to_light), 0.1);
            float texel = length(p - l) * 2.0 * DK2_SHADOW_TAN / float(DK2_SHADOW_SIZE);
            vec3 d = p + facing * (1.5 * texel) - l;
            float len = length(d);
            if (len >= DK2_SHADOW_RANGE || d.y > -0.0001) return 1.0;
            vec2 ndc = d.xz / (-d.y * DK2_SHADOW_TAN);
            if (any(greaterThan(abs(ndc), vec2(1.0)))) return 1.0;
            vec2 at = (ndc * 0.5 + 0.5) * float(DK2_SHADOW_SIZE) - 0.5;
            ivec2 base = ivec2(floor(at));
            vec2 f = at - vec2(base);
            float reach = len - (0.02 + texel * (1.0 + min(sqrt(1.0 - cosine * cosine) / cosine, 8.0)));
            float shade = mix(mix(dk2_shadow_tap(t, base, reach), dk2_shadow_tap(t, base + ivec2(1, 0), reach), f.x),
                mix(dk2_shadow_tap(t, base + ivec2(0, 1), reach), dk2_shadow_tap(t, base + ivec2(1, 1), reach), f.x), f.y);
            return 1.0 - shade;
        }
        float dk2_shadow_visibility(vec3 source, vec3 p, vec3 n) {
            for (int t = 0; t < 4; t++) {
                vec4 l = dk2_shadow_light(t);
                if (l.w > 0.0 && all(lessThan(abs(l.xyz - source), vec3(0.001)))) return dk2_shadow_sample(t, l.xyz, p, n);
            }
            return 1.0;
        }

        """;

    /// <summary>A caster's per-slot fade (models only; the terrain's is the range).</summary>
    private static string ShadowCasterDeclarations(bool model) =>
        model ? "instance uniform vec4 dk2_shadow_fade = vec4(0.0);\n" : "";

    /// <summary>The fragment step's start with the shadows on: in a shadow view an opaque lit surface on the view's side
    /// writes its distance from the torch over the range and its strength, and every other surface draws nothing; the open branch is
    /// the ordinary fragment, closed by <see cref="ShaderFor"/>.</summary>
    private static string ShadowFragment(bool caster, bool model) =>
        "if ((CAMERA_VISIBLE_LAYERS & 1u) == 0u) {\n"
        + (caster
            ? "int t = int(findLSB(CAMERA_VISIBLE_LAYERS >> 12u));\n"
                + "vec3 l = dk2_shadow_light(t).xyz;\nvec3 d = dk2_world - l;\n"
                + (model
                    ? "vec3 centre = (mat4(dk2_m0, dk2_m1, dk2_m2, dk2_m3) * vec4(dk2_bounds.xyz, 1.0)).xyz;\n"
                        + "float fade = dk2_shadow_fade[t];\n"
                        + "float strength = fade > 0.0 ? min(0.7, 1.5 * (fade - length(centre.xz - l.xz)) / fade) : 0.0;\n"
                    : "float strength = min(0.7, 1.5 * (2.5 - length(d.xz)) / 2.5);\n")
                + "if (strength <= 0.0) discard;\n"
                + "ALBEDO = vec3(length(d) / DK2_SHADOW_RANGE, strength, 0.0);\n"
            : "discard;\n")
        + "} else {\n";
}
