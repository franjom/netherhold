// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text.RegularExpressions;

namespace DK2.Presentation.Tests;

/// <summary>Pins the point, instance and radial lighting terms, bound skip and per-flag sum
/// in the expanded Godot shader, with and without torch shadows.</summary>
public class ClassicVertexLightShaderTextTests
{
    private const string GodotLighting = "godot/DK2.Godot/Shaders/WorldLight.gdshaderinc";
    private const string GodotWorld = "godot/DK2.Godot/Shaders/World.gdshaderinc";
    private const string PerPixel = "DK2_PER_PIXEL", Shadows = "DK2_TORCH_SHADOWS";

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
    [MemberData(nameof(Glsl))]
    public void GodotsCompiledShaderKeepsTheReferencesFormulas(string statement, int times)
    {
        Assert.Equal(times, Count(GodotShader(), statement));
    }

    /// <summary>With the torch shadows on, the same formulas; the sum is checked by the next test.</summary>
    [Theory]
    [MemberData(nameof(Glsl))]
    public void GodotsShadowedShaderKeepsTheReferencesFormulas(string statement, int times)
    {
        if (statement.StartsWith("if (kind.x == 1.0)", StringComparison.Ordinal))
        {
            return;
        }

        Assert.Equal(times, Count(GodotShader(Shadows), statement));
    }

    /// <summary>With the torch shadows on, each of the four torch terms the sum adds is weighed by its shadow
    /// visibility; without them, none is.</summary>
    [Fact]
    public void GodotsShadowedSumWeighsEveryTorchTerm()
    {
        string shader = GodotShader(Shadows);
        Assert.Equal(1, Count(shader, "if (kind.x == 1.0) { if ((flags & 4u) == 4u) sum += (dk2_radial(origin, pr, ci)) * vis; if ((flags & 256u) == 256u && dk2_instance(s, centre, vos, nos, basis, pr, ci, c)) sum += (c) * vis; } else if (dk2_point(pws, nws, pr, ci, c)) { if ((flags & 96u) == 96u) sum += (c) * vis; if ((flags & 160u) == 160u || (flags & 1u) == 1u) sum += (c) * vis; }"));
        Assert.Equal(1, Count(shader, "float vis = dk2_shadow_visibility(pr.xyz, svis);"));
        Assert.DoesNotContain("vis", GodotShader(), StringComparison.Ordinal);
    }

    /// <summary>The include's three numbers are the layout's, so the shader reads the texels the tables write, and none is
    /// left unexpanded.</summary>
    [Fact]
    public void GodotsShaderNumbersAreTheTableLayouts()
    {
        string source = Read(GodotLighting).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains($"#define TEXELS_PER_TILE {ClassicLightTableLayout.TexelsPerTile}\n", source);
        Assert.Contains($"#define TILES_PER_ROW {ClassicLightTableLayout.TilesPerTextureRow}\n", source);
        Assert.Contains($"#define MAXIMUM_SOURCES {ClassicLightTable.MaximumSources}\n", source);
        string shader = GodotShader();
        Assert.DoesNotContain("TEXELS_PER_TILE", shader);
        Assert.DoesNotContain("TILES_PER_ROW", shader);
        Assert.DoesNotContain("MAXIMUM_SOURCES", shader);
    }

    /// <summary>Both of Godot's light steps call the one pinned <c>dk2_light</c>: the classic vertex step and the
    /// <c>--light-perpixel</c> fragment step, so the formulas above are the ones either evaluates.</summary>
    [Fact]
    public void GodotsVertexAndPerPixelStepsBothEvaluateThePinnedLight()
    {
        string vertex = Normalise(Preprocess(Read(GodotWorld)));
        Assert.Contains("dk2_diffuse = dk2_light(anchor, bounds, kind, VERTEX, NORMAL, MODEL_MATRIX, world, nws, added, CAMERA_POSITION_WORLD);", vertex);
        Assert.DoesNotContain("dk2_diffuse_px", vertex);
        string pixel = Normalise(Preprocess(Read(GodotWorld), PerPixel));
        Assert.Contains("vec3 dk2_diffuse_px = dk2_light(dk2_anchor_kind.xy, dk2_bounds, dk2_anchor_kind.zw, dk2_vos, dk2_nos_px, dk2_model, dk2_world, dk2_nws_px, added, CAMERA_POSITION_WORLD);", pixel);
        // The tile anchor and model transform are flat: an interpolated tile index could round to a neighbour's table.
        Assert.Contains("varying flat vec4 dk2_anchor_kind;", pixel);
        Assert.Contains("varying flat vec4 dk2_m0;", pixel);
        Assert.Contains("vec3 dk2_diffuse_px = dk2_light(dk2_anchor_kind.xy, dk2_bounds, dk2_anchor_kind.zw, dk2_vos, dk2_nos_px, dk2_model, dk2_world, dk2_nws_px, added, CAMERA_POSITION_WORLD, dk2_svis);",
            Normalise(Preprocess(Read(GodotWorld), PerPixel, Shadows)));
    }

    /// <summary>The shadow lookup's numbers are the shadow views': the map size, the range and the half field of view's
    /// tangent, written out in WorldShadows.gdshaderinc and declared in WorldScene.Shadows.cs.</summary>
    [Fact]
    public void GodotsShadowLookupHoldsTheShadowViewsNumbers()
    {
        string views = Read("godot/DK2.Godot/WorldScene.Shadows.cs"), lookup = Read("godot/DK2.Godot/Shaders/WorldShadows.gdshaderinc");
        Assert.Equal(Number(views, @"ShadowMapSize = (\d+);"), Number(lookup, @"const int DK2_SHADOW_SIZE = (\d+);"));
        Assert.Equal(Number(views, @"ShadowRange = ([\d.]+)f;"), Number(lookup, @"const float DK2_SHADOW_RANGE = ([\d.]+);"));
        Assert.Equal(Math.Tan(Number(views, @"ShadowFieldOfView = ([\d.]+)f;") * Math.PI / 360.0),
            Number(lookup, @"const float DK2_SHADOW_TAN = ([\d.]+);"), 6);
    }

    /// <summary>Each render state's world shader has the line WorldScene.ShaderFor puts the light mode's defines before,
    /// ahead of its include, so the defines reach the shader body.</summary>
    [Fact]
    public void EveryWorldShaderTakesTheLightModeBeforeItsInclude()
    {
        string[] files = Directory.GetFiles(Path.Combine(RepositoryRoot(), "godot/DK2.Godot/Shaders"), "World*.gdshader");
        Assert.Equal(5, files.Length);
        foreach (string file in files)
        {
            string[] lines = File.ReadAllLines(file);
            int marker = Array.FindIndex(lines, line => line.StartsWith("// DK2_LIGHT_MODE", StringComparison.Ordinal));
            int include = Array.FindIndex(lines, line => line == "#include \"res://Shaders/World.gdshaderinc\"");
            Assert.True(marker >= 0 && include > marker, Path.GetFileName(file));
        }
    }

    /// <summary>The liquids' shader (WorldLiquid.gdshader, DK2_LIQUID) waves each vertex as LiquidSurfaceWave.Vertex does:
    /// the phase index in integers, the height as rest plus signed amplitude times the two sines, the same grid and the
    /// same triangle sum; its sine array holds the longer table. The numbers are uniforms set from LiquidSurfaceWave.</summary>
    [Fact]
    public void GodotsLiquidWaveIsTheSharedVertexWave()
    {
        string liquid = Read("godot/DK2.Godot/Shaders/WorldLiquid.gdshader");
        Assert.Contains("#define DK2_BLEND_OPAQUE", liquid);
        Assert.Contains("#define DK2_LIQUID", liquid);
        string world = Normalise(Preprocess(Read(GodotWorld), "DK2_LIQUID"));
        Assert.Contains($"uniform float liquid_sines[{LiquidSurfaceWave.MaximumSines}];", world);
        foreach (string statement in new[]
        {
            "return liquid_sines[(tile * liquid_phase.x + vertex * liquid_phase.y + liquid_frame) & liquid_phase.z];",
            "grid[j * 3 + i] = vec3(float(x) + float(i) * 0.5, liquid_rest + liquid_amplitude * (row + column[i]), -(float(y) + float(j) * 0.5));",
            "if (v == a || v == b || v == d) sum += normalize(cross(grid[b] - grid[a], grid[d] - grid[a]));",
            "if (v == a || v == d || v == c) sum += normalize(cross(grid[d] - grid[a], grid[c] - grid[a]));",
            "dk2_liquid_wave(CUSTOM1, liquid_position, liquid_normal); VERTEX = liquid_position; NORMAL = liquid_normal;",
        })
        {
            Assert.Contains(statement, world);
        }

        Assert.DoesNotContain("liquid_sines", Normalise(Preprocess(Read(GodotWorld))));
    }

    /// <summary>Godot places every world vertex, and in each light mode the world shader writes VERTEX and discards, so
    /// Godot's depth prepass draws it with its own shader rather than the default material, whose depths the opaque
    /// pass's EQUAL test rejected on transformed models (docs/TRAPS.md; audit item 16).</summary>
    [Fact]
    public void EveryWorldShaderKeepsItsOwnDepthPrepass()
    {
        foreach (string file in Directory.GetFiles(Path.Combine(RepositoryRoot(), "godot/DK2.Godot/Shaders"), "World*.gdshader"))
        {
            Assert.DoesNotContain("skip_vertex_transform", File.ReadAllText(file), StringComparison.Ordinal);
        }

        foreach (string[] defined in new[] { Array.Empty<string>(), [PerPixel], [PerPixel, Shadows] })
        {
            string world = Normalise(Preprocess(Read(GodotWorld), defined));
            Assert.Matches(@"void vertex\(\) \{((?!void fragment\(\)).)*\bVERTEX = ", world);
            Assert.Matches(@"void fragment\(\) \{.*\bdiscard;", world);
        }
    }

    private static double Number(string text, string pattern)
    {
        Match match = Regex.Match(text, pattern);
        Assert.True(match.Success, pattern);
        return double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>WorldLight.gdshaderinc as Godot's preprocessor expands it with <paramref name="defined"/>, normalised.</summary>
    private static string GodotShader(params string[] defined)
    {
        return Normalise(Preprocess(Read(GodotLighting), defined));
    }

    /// <summary>The preprocessor directives the world shader's includes use: <c>#ifdef</c>, <c>#ifndef</c>, <c>#else</c>,
    /// <c>#endif</c>, object-like and one-argument <c>#define</c>s expanded by whole words; <c>#include</c> dropped.</summary>
    private static string Preprocess(string text, params string[] defined)
    {
        var objects = new Dictionary<string, string>();
        var functions = new Dictionary<string, (string Parameter, string Body)>();
        var lines = new List<string>();
        var stack = new Stack<(bool Outer, bool Condition)>();
        bool active = true;
        foreach (string raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string line = raw.Trim();
            Match condition = Regex.Match(line, @"^#(ifdef|ifndef)\s+(\w+)");
            if (condition.Success)
            {
                bool holds = Array.IndexOf(defined, condition.Groups[2].Value) >= 0 == (condition.Groups[1].Value == "ifdef");
                stack.Push((active, holds));
                active = active && holds;
            }
            else if (line.StartsWith("#else", StringComparison.Ordinal))
            {
                active = stack.Peek().Outer && !stack.Peek().Condition;
            }
            else if (line.StartsWith("#endif", StringComparison.Ordinal))
            {
                active = stack.Pop().Outer;
            }
            else if (!active || line.StartsWith("#include", StringComparison.Ordinal))
            {
                continue;
            }
            else if (Regex.Match(line, @"^#define\s+(\w+)\((\w+)\)\s+(.*)$") is { Success: true } function)
            {
                functions[function.Groups[1].Value] = (function.Groups[2].Value, function.Groups[3].Value);
            }
            else if (Regex.Match(line, @"^#define\s+(\w+)\s+(.*)$") is { Success: true } value)
            {
                objects[value.Groups[1].Value] = value.Groups[2].Value;
            }
            else
            {
                lines.Add(raw);
            }
        }
        Assert.Empty(stack);
        string result = string.Join("\n", lines);
        foreach (KeyValuePair<string, string> macro in objects)
        {
            result = Regex.Replace(result, $@"\b{macro.Key}\b", macro.Value);
        }

        foreach (KeyValuePair<string, (string Parameter, string Body)> macro in functions)
        {
            for (int at = result.IndexOf(macro.Key + "(", StringComparison.Ordinal); at >= 0;
                 at = result.IndexOf(macro.Key + "(", at, StringComparison.Ordinal))
            {
                int open = at + macro.Key.Length, close = open, depth = 0;
                for (; close < result.Length; close++)
                {
                    depth += result[close] == '(' ? 1 : result[close] == ')' ? -1 : 0;
                    if (depth == 0)
                    {
                        break;
                    }
                }
                string argument = result[(open + 1)..close];
                result = result[..at] + Regex.Replace(macro.Value.Body, $@"\b{macro.Value.Parameter}\b", argument) + result[(close + 1)..];
            }
        }

        return result;
    }

    /// <summary>Line comments dropped and every run of whitespace one space.</summary>
    private static string Normalise(string text)
    {
        return Regex.Replace(Regex.Replace(text, @"//[^\n]*", " "), @"\s+", " ");
    }

    private static int Count(string text, string statement)
    {
        int count = 0;
        for (int at = text.IndexOf(statement, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(statement, at + statement.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static string Read(string path)
    {
        return File.ReadAllText(Path.Combine(RepositoryRoot(), path));
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DK2.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("No DK2.slnx above the test binaries.");
    }
}
