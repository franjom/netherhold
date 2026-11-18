// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text.RegularExpressions;

namespace DK2.Presentation.Tests;

/// <summary>Each engine's shader copies <see cref="ClassicVertexLight"/> by hand, so its text is pinned here: every formula
/// statement of the point, instance and radial terms, the bound skip and the per-flag sum, whitespace-normalised and
/// counted where it repeats. Unity's HLSL is read as it ships; Godot's GLSL is the template in WorldScene.Lighting.cs after
/// <see cref="ClassicLightTableLayout.Substitute"/>, the string the adapter compiles.</summary>
public class ClassicVertexLightShaderTextTests
{
    private const string PointLighting = "unity/DK2.Unity/Assets/DK2/Resources/Shaders/ClassicPointLighting.hlsl";
    private const string VertexLit = "unity/DK2.Unity/Assets/DK2/Resources/Shaders/ClassicVertexLit.shader";
    private const string GodotLighting = "godot/DK2.Godot/WorldScene.Lighting.cs";

    public static readonly TheoryData<string, int> HlslTerms = new()
    {
        { "float3 toLight = positionRadiusSquared.xyz - vertex;", 1 },
        { "float distanceSquared = dot(toLight, toLight);", 1 },
        { "if (radiusSquared <= 0 || distanceSquared >= radiusSquared) return true;", 2 },
        { "if (radiusSquared <= 0 || distanceSquared >= radiusSquared) return 0;", 1 },
        { "float incidence = dot(normal, toLight) * colourIntensity.w;", 1 },
        { "if ((asuint(incidence) & 0x80000000u) != 0) return true;", 2 },
        { "int index = (int)round(distanceSquared * 16.0 - 0.49999);", 3 },
        { "if (index < 0 || index >= 256) return false;", 2 },
        { "if (index < 0 || index >= 256) return 0;", 1 },
        { "if (index == 0) { if (incidence == 0) return false; contribution = colourIntensity.rgb; return true; }", 2 },
        { "float lookup = 0.25 + 16.0 / index;", 1 },
        { "float factor = incidence * lookup * (radiusSquared - distanceSquared) / radiusSquared;", 1 },
        { "contribution = colourIntensity.rgb * min(factor, 1.0);", 1 },
        { "return min(floor(max(normalizedLightSum, 0.0) * 255.0 + 0.00001), 255.0) / 255.0;", 1 },
        { "float3 delta = positionRadiusSquared.xyz - sampleWS;", 2 },
        { "float distanceSquared = (delta.x * delta.x + delta.y * delta.y) + delta.z * delta.z;", 1 },
        { "float3 direction = mul(transpose(basis), delta);", 1 },
        { "float3 d = direction - (vertexOS - centre);", 1 },
        { "float incidence = ((d.x * normalOS.x + d.z * normalOS.z) + d.y * normalOS.y) * colourIntensity.w;", 1 },
        { "float attenuation = (radiusSquared - distanceSquared) * (0.25 + 16.0 / index) * (1.0 / radiusSquared);", 1 },
        { "if (!(attenuation > 0)) return true;", 1 },
        { "contribution = colourIntensity.rgb * min(incidence * attenuation, 1.0);", 1 },
        { "float distanceSquared = dot(delta, delta), radiusSquared = positionRadiusSquared.w;", 1 },
        { "if (index == 0) return colourIntensity.rgb;", 1 },
        { "float factor = (0.25 + 16.0 / index) * (radiusSquared - distanceSquared) * colourIntensity.w * (1.0 / radiusSquared);", 1 },
        { "return colourIntensity.rgb * min(factor, 1.0);", 1 },
    };

    public static readonly TheoryData<string, int> HlslSum = new()
    {
        { "if (input.tileAnchor.y >= 1) tile = (int)input.tileAnchor.x;", 1 },
        { "int x = (int)floor(origin.x), y = (int)floor(-origin.z);", 1 },
        { "if (x < 0 || x >= _DK2TileLightGridWidth || y < 0) return 0;", 1 },
        { "if (tile < 0 || tile >= _DK2TileLightGridCount) return 0;", 1 },
        { "uint count = min(_DK2TileLightCounts[tile], 24u);", 1 },
        { "float3 centre = mul(basis, input.lightBounds.xyz);", 1 },
        { "float3 sampleWS = TransformObjectToWorld(float3(0, 0, 0)) + centre;", 1 },
        { "TileSource source = _DK2TileLightSources[tile * 24 + slot];", 1 },
        { "uint flags = source.intensityFlags >> 8;", 1 },
        { "float4 colourIntensity = float4(source.colour, (source.intensityFlags & 255u) / 128.0);", 1 },
        { "if (input.lightKind.y == 1) { float drawScale = length(mul(basis, float3(1, 0, 0))); float radius = sqrt(source.positionRadiusSquared.w) + input.lightBounds.w * drawScale; float3 delta = source.positionRadiusSquared.xyz - sampleWS; if (dot(delta, delta) >= radius * radius) continue; }", 1 },
        { "if (input.lightKind.x == 1) { if ((flags & 4u) == 4u) sum += DK2RadialContribution(TransformObjectToWorld(float3(0, 0, 0)), source.positionRadiusSquared, colourIntensity); if ((flags & 256u) == 256u && DK2InstancePointContribution(sampleWS, centre, input.positionOS.xyz, input.normalOS, basis, source.positionRadiusSquared, colourIntensity, contribution)) sum += contribution; } else if (DK2PointContribution(positionWS, normalWS, source.positionRadiusSquared, colourIntensity, contribution)) { if ((flags & 0x60u) == 0x60u) sum += contribution; if ((flags & 0xA0u) == 0xA0u || (flags & 1u) == 1u) sum += contribution; }", 1 },
        { "output.diffuse = DK2PackedDiffuse(sum);", 1 },
    };

    public static readonly TheoryData<string, int> Glsl = new()
    {
        { $"vec4 dk2_texel(int tile, int texel) {{ int w = dk2_light_grid.x; int x = tile % w; return texelFetch(dk2_light_tables, ivec3((x % {ClassicLightTableLayout.TilesPerTextureRow}) * {ClassicLightTableLayout.TexelsPerTile} + texel, x / {ClassicLightTableLayout.TilesPerTextureRow}, tile / w), 0); }}", 1 },
        { "int dk2_bin(float d2) { return int(roundEven(d2 * 16.0 - 0.49999)); }", 1 },
        { "bool dk2_negative(float x) { return (floatBitsToUint(x) & 2147483648u) != 0u; }", 1 },
        { "vec3 to_light = pr.xyz - v;", 1 },
        { "float d2 = dot(to_light, to_light);", 1 },
        { "if (pr.w <= 0.0 || d2 >= pr.w) return true;", 2 },
        { "if (pr.w <= 0.0 || d2 >= pr.w) return vec3(0.0);", 1 },
        { "float incidence = dot(n, to_light) * ci.w;", 1 },
        { "if (dk2_negative(incidence)) return true;", 2 },
        { "int index = dk2_bin(d2);", 3 },
        { "if (index < 0 || index >= 256) return false;", 2 },
        { "if (index < 0 || index >= 256) return vec3(0.0);", 1 },
        { "if (index == 0) { if (incidence == 0.0) return false; c = ci.rgb; return true; }", 2 },
        { "float factor = incidence * (0.25 + 16.0 / float(index)) * (pr.w - d2) / pr.w;", 1 },
        { "c = ci.rgb * min(factor, 1.0);", 1 },
        { "vec3 delta = pr.xyz - s;", 3 },
        { "float d2 = (delta.x * delta.x + delta.y * delta.y) + delta.z * delta.z;", 1 },
        { "vec3 d = transpose(basis) * delta - (vos - centre);", 1 },
        { "float incidence = ((d.x * nos.x + d.z * nos.z) + d.y * nos.y) * ci.w;", 1 },
        { "float attenuation = (pr.w - d2) * (0.25 + 16.0 / float(index)) * (1.0 / pr.w);", 1 },
        { "if (!(attenuation > 0.0)) return true;", 1 },
        { "c = ci.rgb * min(incidence * attenuation, 1.0);", 1 },
        { "float d2 = dot(delta, delta);", 1 },
        { "if (index == 0) return ci.rgb;", 1 },
        { "float factor = (0.25 + 16.0 / float(index)) * (pr.w - d2) * ci.w * (1.0 / pr.w);", 1 },
        { "return ci.rgb * min(factor, 1.0);", 1 },
        { "if (anchor.y >= 1.0) tile = int(anchor.x);", 1 },
        { "int x = int(floor(origin.x)); int y = int(floor(origin.z)); if (x < 0 || x >= w || y < 0) return -1; tile = y * w + x;", 1 },
        { "return (tile < 0 || tile >= count) ? -1 : tile;", 1 },
        { $"int count = min(int(dk2_texel(tile, 0).r), {ClassicLightTable.MaximumSources});", 1 },
        { "vec3 centre = basis * bounds.xyz;", 1 },
        { "vec3 s = origin + centre;", 1 },
        { "vec4 pr = dk2_texel(tile, 1 + slot * 2);", 1 },
        { "vec4 cf = dk2_texel(tile, 2 + slot * 2);", 1 },
        { "uint flags = packed_flags >> 8u;", 1 },
        { "vec4 ci = vec4(cf.rgb, float(packed_flags & 255u) / 128.0);", 1 },
        { "if (kind.y == 1.0) { float scale = length(basis * vec3(1.0, 0.0, 0.0)); float radius = sqrt(pr.w) + bounds.w * scale; vec3 delta = pr.xyz - s; if (dot(delta, delta) >= radius * radius) continue; }", 1 },
        { "if (kind.x == 1.0) { if ((flags & 4u) == 4u) sum += dk2_radial(origin, pr, ci); if ((flags & 256u) == 256u && dk2_instance(s, centre, vos, nos, basis, pr, ci, c)) sum += c; } else if (dk2_point(pws, nws, pr, ci, c)) { if ((flags & 96u) == 96u) sum += c; if ((flags & 160u) == 160u || (flags & 1u) == 1u) sum += c; }", 1 },
        { "vec3 dk2_packed(vec3 s) { return min(floor(max(s, vec3(0.0)) * 255.0 + 0.00001), vec3(255.0)) / 255.0; }", 1 },
    };

    [Theory]
    [MemberData(nameof(HlslTerms))]
    public void UnitysTermsKeepTheReferencesFormulas(string statement, int times) =>
        Assert.Equal(times, Count(Normalise(Read(PointLighting)), statement));

    [Theory]
    [MemberData(nameof(HlslSum))]
    public void UnitysSumKeepsTheReferencesFormulas(string statement, int times) =>
        Assert.Equal(times, Count(Normalise(Read(VertexLit)), statement));

    [Theory]
    [MemberData(nameof(Glsl))]
    public void GodotsCompiledShaderKeepsTheReferencesFormulas(string statement, int times) =>
        Assert.Equal(times, Count(GodotShader(), statement));

    [Fact]
    public void GodotsCompiledShaderHasNoPlaceholderLeft()
    {
        string shader = GodotShader();
        Assert.DoesNotContain("TEXELS_PER_TILE", shader);
        Assert.DoesNotContain("TILES_PER_ROW", shader);
        Assert.DoesNotContain("MAXIMUM_SOURCES", shader);
    }

    /// <summary>The GLSL template of WorldScene.Lighting.cs after the substitution the adapter runs, normalised.</summary>
    private static string GodotShader()
    {
        string source = Read(GodotLighting);
        const string open = "ClassicLightTableLayout.Substitute(\"\"\"";
        int start = source.IndexOf(open, StringComparison.Ordinal);
        Assert.True(start >= 0, "WorldScene.Lighting.cs no longer builds its shader through ClassicLightTableLayout.Substitute.");
        start += open.Length;
        int end = source.IndexOf("\"\"\");", start, StringComparison.Ordinal);
        return Normalise(ClassicLightTableLayout.Substitute(source[start..end]));
    }

    /// <summary>Line comments dropped and every run of whitespace one space.</summary>
    private static string Normalise(string text) =>
        Regex.Replace(Regex.Replace(text, @"//[^\n]*", " "), @"\s+", " ");

    private static int Count(string text, string statement)
    {
        int count = 0;
        for (int at = text.IndexOf(statement, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(statement, at + statement.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static string Read(string path) => File.ReadAllText(Path.Combine(RepositoryRoot(), path));

    private static string RepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DK2.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("No DK2.slnx above the test binaries.");
    }
}
