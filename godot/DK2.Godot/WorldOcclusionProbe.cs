// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using System.Security.Cryptography;
using DK2.Presentation;
using Godot;
namespace DK2.GodotAdapter;
/// <summary>Analytic floor/wall corner: AO is horizontally constant, so row variance measures sampling noise.</summary>
public partial class WorldOcclusionProbe : Node
{
    public override async void _Ready()
    {
        string output = LaunchArguments.Value(OS.GetCmdlineUserArgs(), "--occlusion-probe-out")
            ?? throw new ArgumentException("--occlusion-probe-out required");
        Directory.CreateDirectory(output);
        bool negative = LaunchArguments.Value(OS.GetCmdlineUserArgs(), "--occlusion-negative") == "1";
        string[] hashes = new string[2];
        var viewport = new SubViewport { Size = new Vector2I(512, 384), OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(viewport);
        var material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = Colors.White };
        viewport.AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(12, 12) },
            Position = new Vector3(0, -1, -3), MaterialOverride = material });
        viewport.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(12, 6, .1f) },
            Position = new Vector3(0, 2, -3.05f), MaterialOverride = material });
        var camera = new Camera3D { Position = new Vector3(0, 1, 2), Current = true, Near = .05f,
            Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = Colors.White, TonemapMode = Godot.Environment.ToneMapper.Linear } };
        viewport.AddChild(camera);
        camera.LookAt(new Vector3(0, -0.25f, -3));
        double[] rms = new double[2], mean = new double[2];
        bool resize256 = false, restore512 = false, reparent = false;
        for (int mode = 0; mode < 2; mode++)
        {
            var host = new WorldOcclusionHost { Denoise = mode == 1 && !negative };
            camera.AddChild(host);
            for (int i = 0; i < 32; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using Image image = viewport.GetTexture().GetImage();
            string imagePath = Path.Combine(output, mode == 0 ? "raw.png" : "filtered.png");
            image.SavePng(imagePath);
            hashes[mode] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(imagePath)));
            double variance = 0, dark = 0; int count = 0;
            for (int y = 20; y < 364; y++)
            {
                double row = 0;
                for (int x = 80; x < 432; x++) row += image.GetPixel(x, y).R;
                row /= 352;
                for (int x = 80; x < 432; x++)
                {
                    double value = image.GetPixel(x, y).R;
                    variance += (value - row) * (value - row); dark += 1 - value; count++;
                }
            }
            rms[mode] = Math.Sqrt(variance / count); mean[mode] = dark / count;
            if (mode == 1)
            {
                byte[] original = image.GetData();
                viewport.Size = new Vector2I(256, 192);
                for (int i = 0; i < 4; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using (Image small = viewport.GetTexture().GetImage())
                {
                    resize256 = ValidContact(small, 256, 192);
                    small.SavePng(Path.Combine(output, "resize256.png"));
                }
                viewport.Size = new Vector2I(512, 384);
                for (int i = 0; i < 4; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using (Image restored = viewport.GetTexture().GetImage())
                {
                    restore512 = ValidContact(restored, 512, 384) && original.AsSpan().SequenceEqual(restored.GetData());
                    restored.SavePng(Path.Combine(output, "restore512.png"));
                }
                var target = new Node3D();
                viewport.AddChild(target);
                camera.Reparent(target, false);
                for (int i = 0; i < 4; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using (Image moved = viewport.GetTexture().GetImage())
                {
                    reparent = ValidContact(moved, 512, 384) && original.AsSpan().SequenceEqual(moved.GetData());
                    moved.SavePng(Path.Combine(output, "reparent.png"));
                }
                camera.Reparent(viewport, false);
                for (int i = 0; i < 4; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using Image returned = viewport.GetTexture().GetImage();
                reparent &= original.AsSpan().SequenceEqual(returned.GetData());
                target.QueueFree();
            }
            camera.RemoveChild(host);
            host.Free();
        }
        bool pass = resize256 && restore512 && reparent && rms[0] > 0.0005 && rms[1] < rms[0] * .65 && mean[1] > mean[0] * .75 && mean[1] < mean[0] * 1.25;
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { rms, mean, hashes, negative, resize256, restore512, reparent, pass }));
        GD.Print("Occlusion GPU probe: ", pass ? "PASS" : "FAIL", " raw/filtered RMS ", rms[0], "/", rms[1]);
        GetTree().Quit(pass ? 0 : 1);
    }
    private static bool ValidContact(Image image, int width, int height)
    {
        if (image.GetWidth() != width || image.GetHeight() != height) return false;
        int dark = 0;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                Color c = image.GetPixel(x, y);
                if (!float.IsFinite(c.R) || !float.IsFinite(c.G) || !float.IsFinite(c.B)) return false;
                if (c.R < .99f) dark++;
            }
        return dark > 10;
    }
}
