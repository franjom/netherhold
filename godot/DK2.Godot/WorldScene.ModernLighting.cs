// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Kmf;
using DK2.Presentation;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace DK2.GodotAdapter;

/// <summary>The modern lighting (<c>--light-modern</c>): DK2's light sources, as the classic path's
/// <see cref="ClassicWorldLightCache"/> holds them, drive pooled Forward+ <see cref="OmniLight3D"/>s chosen by the shared
/// <see cref="ModernLightSelector"/>; the nearest few torches and moving sources cast cube shadows. Opaque lit surfaces are
/// per-pixel lit (<see cref="ModernShaderCode"/>), DK2's terrain lights glow from the classic tables, and the camera
/// renders linear HDR through <see cref="ModernOutput"/>: tonemap, glow, SSAO and the distance fade as depth fog. Every
/// tunable is in <see cref="ModernLook"/>.</summary>
public partial class WorldScene
{
    /// <summary>The modern look's tunables, in one place.</summary>
    private static class ModernLook
    {
        // Lights: the cap keeps Forward+'s cluster (512 elements) clear; four shadows are a measured frame-time choice.
        public const int MaxLights = 128, MaxShadows = 4;
        // The fit to DK2's falloff, its range and energy scaled for the look.
        public static readonly ModernLightMapping Mapping = ModernLightMapping.Fitted.Scaled(range: 1.3f, energy: 0.75f);
        public const float LightSpecular = 0.25f, ShadowBias = 0.03f, ShadowNormalBias = 1.0f, ShadowBlur = 1.5f;
        public const int ShadowAtlasSize = 2048;
        // Surfaces: rough, dielectric, a low specular, so stone does not read as plastic.
        public const float Roughness = 0.85f, Specular = 0.2f;
        // Emission: DK2's terrain lights as glow (TerrainGlowFunction), lava's own glow, flames' HDR boost, and the added
        // light (tag, red, tint), linear as the blended looks take it inside DK2's light.
        public const float TerrainGlow = 0.8f;
        public const float LavaEmission = 1.6f, FlameBoost = 1.2f, AddedLightEmission = 1f;
        // Ambient: DK2's ambient key level, plus a floor so unlit stone keeps a trace of form under SSAO.
        public const float AmbientFloor = 0.06f;
        // Environment.
        public const Godot.Environment.ToneMapper Tonemap = Godot.Environment.ToneMapper.Aces;
        public const float Exposure = 1.15f, TonemapWhite = 6f;
        public const float GlowIntensity = 0.7f, GlowBloom = 0.04f, GlowThreshold = 0.95f, GlowStrength = 1f;
        public const float SsaoRadius = 0.7f, SsaoIntensity = 1.6f, SsaoPower = 1.4f, SsaoDetail = 0.5f, SsaoLightAffect = 0.35f;
        public const float Saturation = 0.82f, Contrast = 1.04f;
    }

    private static readonly StringName EmissionParameter = "emission_strength";

    private ModernLightSelector? _modernLights;
    private OmniLight3D[] _omni = [];
    private ModernPointLight[] _omniShown = [];
    private int _omniVisible;
    private float _ambientLevel;
    // The frames after the warm-up: what the modern step allocated and its time, for the exit report.
    private long _modernBytes, _modernTicks, _modernMaxTicks, _modernMovedBytes;
    private int _modernFrames, _modernMovedFrames, _modernMaxSources, _modernMaxLights, _modernMaxShadows;

    /// <summary>The modern lighting (<c>--light-modern</c>); set before <see cref="Open"/>. Off is the classic path.</summary>
    public bool Modern { get; set; }

    private void OpenModernLighting()
    {
        if (!Modern) return;
        _modernLights = new ModernLightSelector(ModernLook.MaxLights, ModernLook.MaxShadows, ModernLook.Mapping);
        _omni = new OmniLight3D[ModernLook.MaxLights];
        _omniShown = new ModernPointLight[ModernLook.MaxLights];
        for (int i = 0; i < _omni.Length; i++)
        {
            _omni[i] = new OmniLight3D
            {
                Name = "Light" + i,
                Visible = false,
                OmniShadowMode = OmniLight3D.ShadowMode.Cube,
                LightSpecular = ModernLook.LightSpecular,
                ShadowBias = ModernLook.ShadowBias,
                ShadowNormalBias = ModernLook.ShadowNormalBias,
                ShadowBlur = ModernLook.ShadowBlur,
                OmniAttenuation = ModernLook.Mapping.Attenuation,
            };
            AddChild(_omni[i]);
            _omniShown[i] = Unknown;
        }
        Viewport viewport = GetViewport();
        viewport.PositionalShadowAtlasSize = ModernLook.ShadowAtlasSize;
        // One quadrant of four 512 shadows holds the shadowed lights (MaxShadows); the rest are spare and small.
        viewport.SetPositionalShadowAtlasQuadrantSubdiv(0, Viewport.PositionalShadowAtlasQuadrantSubdiv.Subdiv4);
        viewport.SetPositionalShadowAtlasQuadrantSubdiv(1, Viewport.PositionalShadowAtlasQuadrantSubdiv.Subdiv16);
        viewport.SetPositionalShadowAtlasQuadrantSubdiv(2, Viewport.PositionalShadowAtlasQuadrantSubdiv.Disabled);
        viewport.SetPositionalShadowAtlasQuadrantSubdiv(3, Viewport.PositionalShadowAtlasQuadrantSubdiv.Disabled);
        // Half-size, low-quality SSAO: the dungeon's broad contact shadows, at a fraction of the medium preset's cost.
        RenderingServer.EnvironmentSetSsaoQuality(RenderingServer.EnvironmentSsaoQuality.Low, true, 0.5f, 2, 50f, 300f);
        RenderingServer.PositionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.SoftLow);
    }

    /// <summary>The camera's environment in the modern look: linear HDR, ACES (AgX came out greyer, its blues starved),
    /// glow, SSAO, DK2's ambient level as a flat colour (no sky), and ADR-0522's distance fade as black depth fog over the
    /// far plane's second half.</summary>
    private Godot.Environment ModernOutput() => new()
    {
        BackgroundMode = Godot.Environment.BGMode.ClearColor,
        AmbientLightSource = Godot.Environment.AmbientSource.Color,
        AmbientLightColor = AmbientColour(_ambientLevel),
        AmbientLightEnergy = 1f,
        ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
        TonemapMode = ModernLook.Tonemap,
        TonemapExposure = ModernLook.Exposure,
        TonemapWhite = ModernLook.TonemapWhite,
        GlowEnabled = true,
        GlowIntensity = ModernLook.GlowIntensity,
        GlowStrength = ModernLook.GlowStrength,
        GlowBloom = ModernLook.GlowBloom,
        GlowHdrThreshold = ModernLook.GlowThreshold,
        GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Softlight,
        SsaoEnabled = true,
        SsaoRadius = ModernLook.SsaoRadius,
        SsaoIntensity = ModernLook.SsaoIntensity,
        SsaoPower = ModernLook.SsaoPower,
        SsaoDetail = ModernLook.SsaoDetail,
        SsaoLightAffect = ModernLook.SsaoLightAffect,
        FogEnabled = true,
        FogMode = Godot.Environment.FogModeEnum.Depth,
        FogLightColor = Colors.Black,
        FogDensity = 1f,
        FogSkyAffect = 0f,
        FogDepthCurve = 1f,
        FogDepthBegin = 10f,
        FogDepthEnd = 20f,
        AdjustmentEnabled = true,
        AdjustmentBrightness = 1f,
        AdjustmentContrast = ModernLook.Contrast,
        AdjustmentSaturation = ModernLook.Saturation,
    };

    private static Color AmbientColour(float level)
    {
        float value = level + ModernLook.AmbientFloor;
        return new Color(value, value, value);
    }

    /// <summary>The ambient keys' level as the environment's ambient colour (modern).</summary>
    private void SetModernAmbient(float level)
    {
        _ambientLevel = level;
        if (_camera?.Environment is Godot.Environment environment) environment.AmbientLightColor = AmbientColour(level);
    }

    /// <summary>The fade's far plane as the depth fog's range: ADR-0522's 0.5 to 1 of it.</summary>
    private void SetModernFade(float far)
    {
        if (_camera?.Environment is not Godot.Environment environment) return;
        environment.FogDepthBegin = 0.5f * far;
        environment.FogDepthEnd = far;
    }

    /// <summary>What a pool slot is known to show before its first write, and after it was hidden: NaN everywhere, so
    /// every property is written on the slot's next use (a black light, zero energy and zero range included).</summary>
    private static readonly ModernPointLight Unknown = new(new NVector3(float.NaN), new NVector3(float.NaN), float.NaN, float.NaN,
        false, ModernLightRole.Dynamic, Active: false);

    /// <summary>The frame's modern lights from the prepared cache: the selection's slots for the view's focus written into
    /// the pooled lights of the same index, each property only when it changed; an empty slot hides its light.
    /// Allocation-free once the selector's buffers have grown. <paramref name="moved"/> files the frame's allocation as the
    /// camera's.</summary>
    private void PrepareModernLights(bool moved)
    {
        if (_modernLights == null || _worldLights == null) return;
        long started = System.Diagnostics.Stopwatch.GetTimestamp(), before = GC.GetAllocatedBytesForCurrentThread();
        _modernLights.Select(_worldLights, Focus);
        ReadOnlySpan<ModernPointLight> slots = _modernLights.Slots;
        for (int i = 0; i < slots.Length; i++)
        {
            ModernPointLight light = slots[i];
            ref ModernPointLight shown = ref _omniShown[i];
            OmniLight3D node = _omni[i];
            if (!light.Active)
            {
                if (shown.Active || i < _omniVisible) node.Visible = false;
                shown = Unknown;
                continue;
            }
            bool fresh = !shown.Active;
            if (fresh) node.Visible = true;
            if (fresh || light.Position != shown.Position) node.Position = ToGodot(RightHandedFrame.Point(light.Position));
            if (fresh || light.Colour != shown.Colour) node.LightColor = new Color(light.Colour.X, light.Colour.Y, light.Colour.Z);
            if (fresh || light.Energy != shown.Energy) node.LightEnergy = light.Energy;
            if (fresh || light.Range != shown.Range) node.OmniRange = light.Range;
            if (fresh || light.CastsShadow != shown.CastsShadow) node.ShadowEnabled = light.CastsShadow;
            shown = light;
        }
        for (int i = slots.Length; i < _omniVisible; i++)
        {
            if (_omniShown[i].Active) _omni[i].Visible = false;
            _omniShown[i] = Unknown;
        }
        _omniVisible = slots.Length;
        long ticks = System.Diagnostics.Stopwatch.GetTimestamp() - started, bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (_modernFrames++ >= DrawWarmUp)
        {
            if (moved) { _modernMovedBytes += bytes; _modernMovedFrames++; }
            else
            {
                _modernBytes += bytes;
                _modernTicks += ticks;
                _modernMaxTicks = Math.Max(_modernMaxTicks, ticks);
            }
        }
        _modernMaxSources = Math.Max(_modernMaxSources, _modernLights.SourceCount);
        _modernMaxLights = Math.Max(_modernMaxLights, _modernLights.Count);
        _modernMaxShadows = Math.Max(_modernMaxShadows, _modernLights.ShadowCount);
    }

    private void ReportModernLighting()
    {
        if (_modernLights == null) return;
        int still = ModernStillFrames;
        GD.Print("DK2.Godot modern lighting: ", _modernFrames, " frames, up to ", _modernMaxSources, " sources, ", _modernMaxLights,
            " lights and ", _modernMaxShadows, " shadowed (now ", _modernLights.SourceCount, ", ", _modernLights.Count, ", ",
            _modernLights.ShadowCount, "); after the first ", DrawWarmUp, " frames ", _modernBytes, " B allocated in ", still,
            " with the camera still and ", _modernMovedBytes, " B in ", _modernMovedFrames, " that moved it; ",
            Milliseconds(_modernTicks / Math.Max(1, still)), " ms mean and ", Milliseconds(_modernMaxTicks), " ms max with it still");
    }

    private int ModernStillFrames => Math.Max(0, _modernFrames - DrawWarmUp - _modernMovedFrames);

    /// <summary>The modern step's counters for the performance report (<see cref="AddPerformanceCounters"/>).</summary>
    private void AddModernLightingCounters(List<KeyValuePair<string, double>> into)
    {
        if (_modernLights == null) return;
        int still = ModernStillFrames;
        Add(into, "modern.stillFrames", still);
        Add(into, "modern.stillMeanMs", Ms(_modernTicks) / Math.Max(1, still));
        Add(into, "modern.stillMaxMs", Ms(_modernMaxTicks));
        Add(into, "modern.stillBytes", _modernBytes);
        Add(into, "modern.movedFrames", _modernMovedFrames);
        Add(into, "modern.movedBytes", _modernMovedBytes);
        Add(into, "modern.maxSources", _modernMaxSources);
        Add(into, "modern.maxLights", _modernMaxLights);
        Add(into, "modern.maxShadows", _modernMaxShadows);
    }

    /// <summary>Whether a model of this kind casts shadows in the modern look: not a torch, nor an object carrying its own
    /// light (which sits inside it and would be shut in).</summary>
    private GeometryInstance3D.ShadowCastingSetting ModelShadow(bool selfLit) =>
        Modern && !selfLit ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;

    /// <summary>The modern shader of a look: the opaque lit ones per-pixel lit with rough dielectric stone, an inverted terrain
    /// top normal turned up (as the classic fill turns it), DK2's terrain glow and the added light (tag, red, tint) as
    /// emission; every other look keeps the classic vertex light and blend, read from sRGB textures into linear
    /// light. The environment pass's coordinates stay classic.</summary>
    private static string ModernShaderCode(KmfBlend blend, bool fullLight, string mode, string alpha, bool model, bool environment)
    {
        const string Head = "uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic, repeat_disable;\n"
            + "uniform vec4 base_colour : source_color = vec4(1.0);\n"
            + "uniform vec3 added_light = vec3(0.0);\n"
            + "uniform float emission_strength = 0.0;\n";
        if (blend == KmfBlend.Opaque && !fullLight && !environment)
            return "shader_type spatial;\nrender_mode cull_back, skip_vertex_transform, " + mode
                + ", diffuse_lambert, specular_schlick_ggx;\n" + Head + LightFunctions + TerrainGlowFunction
                + "void vertex() {\nvec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;\n"
                + "vec3 nws = normalize(MODEL_NORMAL_MATRIX * NORMAL);\n"
                + (model ? "vec4 bounds = CUSTOM0;\nvec2 anchor = CUSTOM1.xy;\nvec2 kind = CUSTOM1.zw;\n"
                    : "vec4 bounds = vec4(0.0);\nvec2 anchor = CUSTOM0.xy;\nvec2 kind = vec2(0.0);\n")
                // DK2's own normal for its glow; a terrain top's, inverted in DK2, turned up for Godot's lights.
                // A shadow pass draws depth alone: the glow's table loop is skipped there.
                + "dk2_glow = IN_SHADOW_PASS ? vec3(0.0) : dk2_terrain_glow(anchor, bounds, kind, MODEL_MATRIX, world, nws);\n"
                + (model ? "" : "if (anchor.y == 1.0 && NORMAL.y < -0.5) NORMAL = -NORMAL;\n")
                + PlaceOutputs + "}\n"
                + "void fragment() {\nvec4 c = texture(albedo_texture, UV) * base_colour;\nALBEDO = c.rgb;\n"
                + "ROUGHNESS = " + Float(ModernLook.Roughness) + ";\nMETALLIC = 0.0;\nSPECULAR = " + Float(ModernLook.Specular) + ";\n"
                + "EMISSION = c.rgb * (pow(dk2_glow, vec3(2.2)) * " + Float(ModernLook.TerrainGlow) + " + pow(added_light, vec3(2.2)) * "
                + Float(ModernLook.AddedLightEmission) + " + vec3(emission_strength));\n}\n";
        return "shader_type spatial;\nrender_mode unshaded, cull_back, skip_vertex_transform, " + mode + ";\n" + Head
            + (fullLight ? PlacedVertex : LightFunctions + LitVertex(model, environment))
            + "void fragment() {\nvec4 c = texture(albedo_texture, UV) * base_colour;\n"
            // DK2's gamma-space light, as linear light.
            + (fullLight ? "" : "c.rgb *= pow(dk2_diffuse, vec3(2.2));\n")
            + "ALBEDO = c.rgb;\n" + alpha + "}\n";
    }

    /// <summary>DK2's terrain lights (flags <see cref="ClassicLightFlags.Terrain"/>) from the classic tables, as the
    /// classic vertex step sums them (<see cref="LightFunctions"/>' point and radial terms) with DK2's own normal and
    /// clamp: the glow of earth, gems, lava and rooms, drawn as emission. Their sources sit at their tile's light height,
    /// mostly under the tile's top, which DK2 lights through its inverted top normal; a per-pixel light cannot. On such a
    /// top every source counts, torches below it included, since none of Godot's lights reaches it from there.</summary>
    private static readonly string TerrainGlowFunction = ClassicLightTableLayout.Substitute("""
        varying vec3 dk2_glow;
        vec3 dk2_terrain_glow(vec2 anchor, vec4 bounds, vec2 kind, mat4 model, vec3 pws, vec3 nws) {
            // A top DK2 lights through its inverted normal takes every source from below; anything else, terrain ones.
            bool top = kind.x != 1.0 && anchor.y == 1.0 && nws.y < -0.5;
            vec3 origin = model[3].xyz;
            int tile = dk2_tile(anchor, origin);
            if (tile < 0) return vec3(0.0);
            int count = min(int(dk2_texel(tile, 0).r), MAXIMUM_SOURCES);
            vec3 sum = vec3(0.0);
            mat3 basis = mat3(model);
            vec3 s = origin + basis * bounds.xyz;
            for (int slot = 0; slot < count; slot++) {
                vec4 cf = dk2_texel(tile, 2 + slot * 2);
                uint packed_flags = uint(cf.a);
                uint flags = packed_flags >> 8u;
                if (!top && flags != TERRAIN_FLAGSu) continue;
                vec4 pr = dk2_texel(tile, 1 + slot * 2);
                vec4 ci = vec4(cf.rgb, float(packed_flags & 255u) / 128.0);
                vec3 c;
                // The classic sum's bound cull and per-flag terms (dk2_table_sum); a model takes the radial term at its
                // origin. The instance term needs flag 0x100, which no terrain source has.
                if (kind.y == 1.0) {
                    float radius = sqrt(pr.w) + bounds.w * length(basis * vec3(1.0, 0.0, 0.0));
                    vec3 delta = pr.xyz - s;
                    if (dot(delta, delta) >= radius * radius) continue;
                }
                if (kind.x == 1.0) {
                    if ((flags & 4u) == 4u) sum += dk2_radial(origin, pr, ci);
                } else if (dk2_point(pws, nws, pr, ci, c)) {
                    if ((flags & 96u) == 96u) sum += c;
                    if ((flags & 160u) == 160u || (flags & 1u) == 1u) sum += c;
                }
            }
            return min(sum, vec3(1.0));
        }
        """).Replace("TERRAIN_FLAGS", ((uint)ClassicLightFlags.Terrain).ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static string Float(float value) => value.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture);
}
