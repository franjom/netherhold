// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text.Json;
using DK2.Presentation;
using Godot;
namespace DK2.GodotAdapter;
/// <summary>Runs the production point-light function on the GPU across the first distance-bin boundary.
/// The fragment path must be continuous; the faithful vertex path must retain its original jump.</summary>
public partial class TorchLightProbe : Node
{
    public override async void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        string output = LaunchArguments.Value(args, "--torch-probe-out") ?? throw new ArgumentException("--torch-probe-out required");
        Directory.CreateDirectory(output);
        string source = Godot.FileAccess.GetFileAsString("res://Shaders/WorldLight.gdshaderinc");
        string functions = Function(source, "int dk2_bin(") + Function(source, "bool dk2_negative(") + Function(source, "bool dk2_point(");
        var results = new List<object>();
        bool pass = true;
        foreach (bool pixel in new[] { false, true })
        {
            string shader = "shader_type canvas_item; render_mode blend_disabled;\n" + (pixel ? "#define DK2_PER_PIXEL\n" : "") + functions + """
                void fragment() {
                    float d2 = 0.045 + UV.x * 0.04;
                    vec3 point = vec3(-sqrt(d2 - 0.0001), -0.01, 0.0);
                    int row = int(UV.y * 4.0);
                    vec3 normal = vec3(0.0, row == 1 ? -1.0 : 1.0, 0.0);
                    vec4 light = vec4(0.0, 0.0, 0.0, row == 2 ? 0.04 : 4.0);
                    vec4 tint = row == 3 ? vec4(0.25, 0.5, 1.0, 1.0) : vec4(1.0);
                    vec3 c; dk2_point(point, normal, light, tint, c);
                    COLOR = vec4(c, 1.0);
                }
                """;
            var viewport = new SubViewport { Size = new Vector2I(256, 4), Disable3D = true, UseHdr2D = false,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            viewport.AddChild(new ColorRect { Size = new Vector2(256, 4),
                Material = new ShaderMaterial { Shader = new Shader { Code = shader } } });
            AddChild(viewport);
            for (int i = 0; i < 3; i++)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }

            using Image image = viewport.GetTexture().GetImage();
            image.SavePng(Path.Combine(output, pixel ? "pixel.png" : "vertex.png"));
            float jump = 0;
            for (int x = 1; x < 256; x++)
            {
                jump = Math.Max(jump, Math.Abs(image.GetPixel(x, 0).R - image.GetPixel(x - 1, 0).R));
            }

            float middle = image.GetPixel(112, 0).R;
            float first = image.GetPixel(0, 0).R, last = image.GetPixel(255, 0).R;
            bool dark = true;
            for (int x = 0; x < 256; x++)
            {
                dark &= image.GetPixel(x, 1).R == 0 && image.GetPixel(x, 2).R == 0;
            }

            Color tint = image.GetPixel(112, 3);
            bool good = dark && Math.Abs(tint.R - 0.04f) < 0.008f && Math.Abs(tint.G - 0.08f) < 0.008f
                && Math.Abs(tint.B - 0.16f) < 0.008f
                && (pixel ? jump <= 0.012f && Math.Abs(middle - 0.160f) < 0.012f
                    && Math.Abs(first - 0.222f) < 0.012f && Math.Abs(last - 0.118f) < 0.012f : jump > 0.5f);
            pass &= good;
            results.Add(new { pixel, jump, middle, first, last, dark, tintR = tint.R, tintG = tint.G, tintB = tint.B, pass = good });
            viewport.QueueFree();
        }
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(results));
        GD.Print("Torch light GPU probe: ", pass ? "PASS" : "FAIL");
        GetTree().Quit(pass ? 0 : 1);
    }
    private static string Function(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal), open = source.IndexOf('{', start), end = open + 1, depth = 1;
        while (depth > 0) { if (source[end] == '{') { depth++; } else if (source[end] == '}') { depth--; } end++; }
        return source[start..end] + "\n";
    }
}
