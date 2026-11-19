// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using Godot;

namespace DK2.GodotAdapter;

/// <summary>Ambient occlusion on DK2's light (<c>--light-post</c>, step 3 of the lighting direction), a modern addition
/// DK2 does not have; DK2's light terms stay. Godot's SSAO only darkens a shaded material's ambient light and every world
/// shader here is unshaded, so a full-screen quad drawn first in the transparent pass reads the opaque depth, rebuilds
/// the view-space position and normal, and multiplies the opaque colour (<see cref="AmbientOcclusionShader"/>); the
/// blended terrain, the sight fog and the effects draw over it. Off, nothing here exists.</summary>
public partial class WorldScene
{
    /// <summary>The occlusion's numbers, tuned by eye, in one place.</summary>
    private static class PostTuning
    {
        /// <summary>The occlusion radius in tiles, how much it darkens at most (0 to 1) and its samples a pixel.</summary>
        public const float OcclusionRadius = 0.35f, OcclusionStrength = 0.6f;
        public const int OcclusionSamples = 10;
        /// <summary>The occlusion's bias against a flat face's own depth steps, in tiles a tile of eye distance.</summary>
        public const float OcclusionBias = 0.008f;
        /// <summary>Beyond this eye distance in tiles the occlusion fades out (DK2's far plane is about 10).</summary>
        public const float OcclusionFar = 14f;
    }

    /// <summary>The ambient occlusion (<c>--light-post</c>). Set before <see cref="Open"/>.</summary>
    public bool PostOcclusion { get; set; }

    private MeshInstance3D? _occlusion;

    /// <summary>The occlusion quad on the camera; nothing when off.</summary>
    private void OpenPostEffects()
    {
        if (!PostOcclusion) return;
        _occlusion = new MeshInstance3D
        {
            Name = "AmbientOcclusion",
            Mesh = new QuadMesh { Size = new Vector2(2f, 2f) },
            MaterialOverride = new ShaderMaterial { Shader = new Shader { Code = AmbientOcclusionShader }, RenderPriority = -128 },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = 16384f,
            Position = new Vector3(0f, 0f, -1f),
        };
        _camera!.AddChild(_occlusion);
    }

    /// <summary>The occlusion quad's shader: an Alchemy-style estimate over a rotated spiral of depth samples (one
    /// rotation a pixel from interleaved gradient noise), blended by multiplication into the opaque colour.</summary>
    private static readonly string AmbientOcclusionShader = string.Create(System.Globalization.CultureInfo.InvariantCulture, $$"""
        shader_type spatial;
        render_mode unshaded, blend_mul, depth_draw_never, depth_test_disabled, cull_disabled, fog_disabled;
        uniform sampler2D depth_texture : hint_depth_texture, filter_nearest, repeat_disable;
        const int SAMPLES = {{PostTuning.OcclusionSamples}};
        const float RADIUS = {{PostTuning.OcclusionRadius:0.0###}}, STRENGTH = {{PostTuning.OcclusionStrength:0.0###}};
        const float BIAS = {{PostTuning.OcclusionBias:0.0###}}, FAR = {{PostTuning.OcclusionFar:0.0###}};

        void vertex() { POSITION = vec4(VERTEX.xy, 1.0, 1.0); }

        vec3 view_at(vec2 uv, mat4 inverse_projection) {
            float depth = textureLod(depth_texture, uv, 0.0).r;
            vec4 view = inverse_projection * vec4(uv * 2.0 - 1.0, depth, 1.0);
            return view.xyz / view.w;
        }

        void fragment() {
            float depth = textureLod(depth_texture, SCREEN_UV, 0.0).r;
            vec3 p = view_at(SCREEN_UV, INV_PROJECTION_MATRIX);
            float eye = -p.z;
            float occlusion = 0.0;
            if (depth > 0.0 && eye < FAR) {
                vec2 texel = 1.0 / VIEWPORT_SIZE;
                vec3 right = view_at(SCREEN_UV + vec2(texel.x, 0.0), INV_PROJECTION_MATRIX) - p;
                vec3 left = p - view_at(SCREEN_UV - vec2(texel.x, 0.0), INV_PROJECTION_MATRIX);
                vec3 up = view_at(SCREEN_UV + vec2(0.0, texel.y), INV_PROJECTION_MATRIX) - p;
                vec3 down = p - view_at(SCREEN_UV - vec2(0.0, texel.y), INV_PROJECTION_MATRIX);
                vec3 n = normalize(cross(abs(right.z) < abs(left.z) ? right : left, abs(up.z) < abs(down.z) ? up : down));
                if (dot(n, p) > 0.0) n = -n;
                vec2 radius = vec2(PROJECTION_MATRIX[0][0], PROJECTION_MATRIX[1][1]) * (RADIUS * 0.5 / eye);
                float turn = 6.2831853 * fract(52.9829189 * fract(dot(FRAGCOORD.xy, vec2(0.06711056, 0.00583715))));
                float sum = 0.0;
                for (int i = 0; i < SAMPLES; i++) {
                    float t = (float(i) + 0.5) / float(SAMPLES);
                    float angle = float(i) * 2.3999632 + turn;
                    vec2 offset = vec2(cos(angle), sin(angle)) * radius * t;
                    vec3 v = view_at(SCREEN_UV + offset, INV_PROJECTION_MATRIX) - p;
                    float vv = dot(v, v);
                    float reach = max(0.0, 1.0 - vv / (RADIUS * RADIUS));
                    sum += max(0.0, dot(v, n) - BIAS * eye) / (vv + 0.0001) * reach;
                }
                occlusion = clamp(sum * RADIUS / float(SAMPLES), 0.0, 1.0) * (1.0 - smoothstep(FAR * 0.7, FAR, eye));
            }
            ALBEDO = vec3(1.0 - STRENGTH * occlusion);
        }
        """);
}
