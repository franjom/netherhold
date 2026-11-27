// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Security.Cryptography;
using System.Text.Json;
using DK2.Art.Textures;
using DK2.Core;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>GPU readback versus the independent CPU oracle, using synthetic assets and all minimap marker kinds.</summary>
public partial class MinimapParity : Node
{
    public override async void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        string output = LaunchArguments.Value(args, "--parity-out") ?? throw new InvalidOperationException("--parity-out required");
        Directory.CreateDirectory(output);
        try
        {
            byte[] rock = new byte[8 * 8 * 4];
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
            {
                int p = (y * 8 + x) * 4; rock[p] = (byte)(x * 197 + y * 97 + 17); rock[p + 1] = (byte)(x * 37 + y * 101 + 29);
                rock[p + 2] = (byte)(x * 71 + y * 53 + 83); rock[p + 3] = 255;
            }
            }

            var backdrop = new RgbaImage(8, 8, rock);
            uint[] colours = new uint[37];
            for (int i = 0; i < colours.Length; i++)
            {
                colours[i] = 0xFF000000 | ((uint)(i * 97 & 255) << 16)
                | ((uint)(i * 53 & 255) << 8) | (uint)(i * 37 & 255);
            }

            var palette = new MinimapPalette(colours);
            var grid = new MinimapClassGrid(83, 47);
            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    grid.Set(new TilePos(x, y), new MinimapColourClass((ushort)(x % 11 == 0 ? 0x224 : y % 13 == 0 ? 0x100 : (x + y * 3) % 25)));
                }
            }

            MinimapMarker[] markers =
            [
                new(MinimapMarkerKind.Creature, At(40.5f, 24.75f), 0xFF123456),
                new(MinimapMarkerKind.Trap, At(40.5f, 24.75f), 0xFFEDCBA9),
                new(MinimapMarkerKind.VerticalDoor, At(41.25f, 23.75f), 0xFF51D732),
                new(MinimapMarkerKind.HorizontalDoor, At(38.75f, 25.5f), 0xFFEFD31A),
                new(MinimapMarkerKind.Ring, At(40.5f, 24.75f), 0xFF9696FF, 7, ReportingDistanceTiles: 5),
                new(MinimapMarkerKind.Ring, At(38.25f, 22.5f), 0xFFDC32FF, 1),
                new(MinimapMarkerKind.Ring, At(42.25f, 22.5f), 0xFFA14BFF, 0),
                new(MinimapMarkerKind.Rectangle, At(37, 20), 0xFF5413F1, EndPosition: At(45, 28)),
                new(MinimapMarkerKind.Ring, At(0, 0), 0xFFDC32FF, 29),
            ];
            var gpu = new MinimapGpuDisc(); gpu.Open(backdrop); AddChild(gpu);
            var oracle = new MinimapDiscImage(backdrop);
            var rows = new List<object>(); int failures = 0, index = 0;
            foreach (int diameter in new[] { 1, 96, 128, 239, 257, 317 })
            {
                foreach (ushort heading in new ushort[] { 0, 173, 512, 1024, 1537, 2047 })
                {
                    foreach (int zoom in new[] { -1, 0, 1, 2, 3, 4 })
                    {
                        foreach (bool withMarkers in new[] { false, true })
                    {
                        MinimapMarker[] drawn = withMarkers ? markers : [];
                            FpPosition3 focus = At(40.5f, 24.75f); var scale = new MinimapZoom(zoom);
                        oracle.Render(diameter, grid, palette, drawn, scale, focus, new Dk2Angle(heading), new TilePos(12, 10), new GameTick(31));
                        gpu.Render(diameter, grid, palette, drawn, scale, focus, new Dk2Angle(heading), new TilePos(12, 10), new GameTick(31), true);
                        for (int f = 0; f < 3; f++)
                            {
                                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                            }

                            using Image actual = gpu.Texture.GetImage(); actual.Convert(Image.Format.Rgba8);
                        if (actual.GetWidth() != Math.Max(2, diameter) || actual.GetHeight() != Math.Max(2, diameter))
                            {
                                throw new InvalidOperationException("Minimap target size did not update.");
                            }

                            byte[] pixels = actual.GetData(), expected = oracle.Pixels.ToArray();
                        if (diameter == 1)
                        {
                            for (int p = 4; p < pixels.Length; p++)
                                {
                                    if (pixels[p] != pixels[p % 4])
                                    {
                                        throw new InvalidOperationException("One-pixel padding differs.");
                                    }
                                }

                                pixels = pixels.AsSpan(0, 4).ToArray();
                        }
                        int different = 0, max = 0;
                        for (int p = 0; p < expected.Length; p++)
                            {
                                if (pixels[p] != expected[p]) { different++; max = Math.Max(max, Math.Abs(pixels[p] - expected[p])); }
                            }

                            if (different != 0)
                        {
                            failures++; actual.SavePng(Path.Combine(output, $"actual-{index}.png"));
                            using var reference = Image.CreateFromData(diameter, diameter, false, Image.Format.Rgba8, expected);
                            reference.SavePng(Path.Combine(output, $"oracle-{index}.png"));
                        }
                        bool? displayEqual = null;
                        if (heading == 173 && zoom == 2 && withMarkers)
                        {
                            displayEqual = await DisplayMatches(gpu.Texture, oracle, output, index);
                            if (displayEqual == false)
                                {
                                    failures++;
                                }
                            }
                        rows.Add(new { index, diameter, heading, zoom, withMarkers, different, max, displayEqual,
                            oracleSha256 = Convert.ToHexString(SHA256.HashData(expected)),
                            gpuSha256 = Convert.ToHexString(SHA256.HashData(pixels)) });
                        GD.Print("minimap parity ", index, ": d", diameter, " heading ", heading, " zoom ", zoom, ", ", different, " differing channels, max ", max);
                        index++;
                    }
                    }
                }
            }

            foreach (TilePos dimensions in new[] { new TilePos(12, 8), new TilePos(8, 12) })
            {
                var changedGrid = new MinimapClassGrid(dimensions.X, dimensions.Y);
                for (int y = 0; y < changedGrid.Height; y++)
                {
                    for (int x = 0; x < changedGrid.Width; x++)
                    {
                        changedGrid.Set(new TilePos(x, y), new MinimapColourClass(20));
                    }
                }

                oracle.Render(128, changedGrid, palette, [], MinimapZoom.WholeMap, default, default, null, default);
                gpu.Render(128, changedGrid, palette, [], MinimapZoom.WholeMap, default, default, null, default, true);
                for (int f = 0; f < 3; f++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }

                using Image resized = gpu.Texture.GetImage(); resized.Convert(Image.Format.Rgba8);
                byte[] actualBytes = resized.GetData(), expectedBytes = oracle.Pixels.ToArray();
                int differing = 0;
                for (int b = 0; b < actualBytes.Length; b++)
                {
                    if (actualBytes[b] != expectedBytes[b])
                    {
                        differing++;
                    }
                }

                if (differing != 0)
                {
                    failures++;
                }

                rows.Add(new { index, diameter = 128, heading = 0, zoom = -1, withMarkers = false,
                    different = differing, max = differing == 0 ? 0 : 255, dimensions,
                    oracleSha256 = Convert.ToHexString(SHA256.HashData(expectedBytes)),
                    gpuSha256 = Convert.ToHexString(SHA256.HashData(actualBytes)) });
                index++;
            }
            RemoveChild(gpu); AddChild(gpu);
            for (int f = 0; f < 3; f++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }

            using (Image reparented = gpu.Texture.GetImage())
            {
                reparented.Convert(Image.Format.Rgba8);
                bool reparentEqual = reparented.GetData().AsSpan().SequenceEqual(oracle.Pixels);
                if (!reparentEqual)
                {
                    failures++;
                }

                File.WriteAllText(Path.Combine(output, "lifecycle.json"), JsonSerializer.Serialize(new { reparentEqual }));
            }
            File.WriteAllText(Path.Combine(output, "parity.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
            GetTree().Quit(failures == 0 ? 0 : 1);
        }
        catch (Exception e) { GD.PushError(e.ToString()); GetTree().Quit(2); }
    }

    private async Task<bool> DisplayMatches(Texture2D texture, MinimapDiscImage oracle, string output, int index)
    {
        var viewport = new SubViewport
        {
            Size = new Vector2I(384, 352), Disable3D = true, World2D = new World2D(), UseHdr2D = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            CanvasItemDefaultTextureFilter = Godot.Viewport.DefaultCanvasItemTextureFilter.Linear,
        };
        viewport.AddChild(new ColorRect { Size = new Vector2(384, 352), Color = new Color(.125f, .25f, .375f) });
        using var reference = Image.CreateFromData(oracle.Diameter, oracle.Diameter, false, Image.Format.Rgba8, oracle.Pixels);
        using var referenceTexture = ImageTexture.CreateFromImage(reference);
        var quad = new TextureRect
        {
            Position = new Vector2(2.25f, 3.5f), Size = new Vector2(oracle.Diameter * .9375f, oracle.Diameter * .875f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            Texture = referenceTexture,
        };
        viewport.AddChild(quad); AddChild(viewport);
        viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        for (int f = 0; f < 3; f++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        using Image expected = viewport.GetTexture().GetImage();
        quad.Texture = texture; viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        for (int f = 0; f < 3; f++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        using Image actual = viewport.GetTexture().GetImage();
        bool equal = actual.GetData().AsSpan().SequenceEqual(expected.GetData());
        if (!equal)
        {
            expected.SavePng(Path.Combine(output, $"display-oracle-{index}.png"));
            actual.SavePng(Path.Combine(output, $"display-gpu-{index}.png"));
        }
        viewport.QueueFree();
        return equal;
    }

    private static FpPosition3 At(float x, float y) => new(new Fp32((int)(x * 4096)), new Fp32((int)(y * 4096)), default);
}
