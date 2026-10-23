// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;

namespace DK2.Oracle.Tests;

public class OriginalCameraControlsTests
{
    [OracleFact]
    public void ViewportCropDoesNotChangeTheNativePixelLens()
    {
        const uint argument = OriginalCpu.Scratch + 0x1000;
        foreach (int height in new[] { 1200, 900 })
        {
            var values = new float[21];
            values[3] = values[7] = values[11] = 1;
            values[12] = 640;
            values[17] = 1600;
            values[18] = height;
            values[19] = 20;
            var writes = values.Select((v, i) => (argument + (uint)(i * 4), BitConverter.SingleToUInt32Bits(v))).ToArray();
            var result = OriginalCpu.Run(new OriginalCpu.Request[]
            {
                new(0x0058A6F0, [argument], writes, [0x0077F4BC, 0x0077939C]),
            })[0];
            Assert.Null(result.Fault);
            var camera = new ClassicCamera(new CameraThing(new byte[84])
            {
                CameraId = 3, Zoom = 40960, MinZoom = 16384, MaxZoom = 53248,
                Lens = 8192, AnglePitch = 220, Flags = 0x26,
            }) { ReferenceScreenWidth = 1600 };
            var pose = camera.Pose(System.Numerics.Vector3.Zero, new CameraViewport(1600, 1600, height));
            double focal = height / (2 * Math.Tan(pose.VerticalFieldOfView * Math.PI / 360));
            Assert.Equal(BitConverter.UInt32BitsToSingle(result.Reads[0]), focal / 1600, 6);
            Assert.Equal(BitConverter.UInt32BitsToSingle(result.Reads[1]), focal / height, 6);
        }
    }

    [OracleFact]
    public void OpposingPacketsMatchNativeClampAtBothMapEdges()
    {
        const uint bridge = OriginalCpu.Scratch + 0x1000, table = OriginalCpu.Scratch + 0x100;
        const uint modeStub = OriginalCpu.Scratch + 0x200, argument = OriginalCpu.Scratch + 0x300;
        const uint cameraAddress = bridge + 0x3E5, focusX = cameraAddress + 0xD68;
        foreach (bool maximumEdge in new[] { false, true })
        {
            int start = maximumEdge ? 4095 : 0, first = maximumEdge ? 1 : -1;
            var setup = new List<(uint, uint)>
            {
                (bridge, table), (table + 0xCC, modeStub),
                (modeStub, 0x000003B8), (modeStub + 4, 0x0000C300),
                (bridge + 0x1165, 40960), (bridge + 0x1169, 0),
                (cameraAddress + 0xD92, 0), (cameraAddress + 0xDD8, 0),
                (focusX, (uint)start), (cameraAddress + 0xD6C, 0), (cameraAddress + 0xD70, 4095),
                (cameraAddress + 0xD38, 0), (cameraAddress + 0xD3C, 0), (cameraAddress + 0xD40, 4096),
                (cameraAddress + 0xD44, 4096), (cameraAddress + 0xD48, 4096), (cameraAddress + 0xD4C, 4096),
                (0x006F2590, 4), (0x0079570C, 0), (0x00795710, 20),
                (argument, unchecked((uint)first)), (argument + 4, 0), (argument + 8, 0),
            };
            var results = OriginalCpu.Run(new OriginalCpu.Request[]
            {
                new(0x00442140, [argument], setup.ToArray(), Ecx: bridge),
                new(0x00442140, [argument], [(argument, unchecked((uint)-first))], [focusX], Ecx: bridge),
            });
            foreach (var result in results) Assert.Null(result.Fault);
            var camera = new ClassicCamera(new CameraThing(new byte[84])
            {
                CameraId = 3, Zoom = 40960, MinZoom = 16384, MaxZoom = 53248,
                Lens = 8192, AnglePitch = 220, Flags = 0x26,
            });
            var controls = new ClassicCameraControls(camera, bounds: new CameraFocusBounds(
                System.Numerics.Vector3.Zero, new System.Numerics.Vector3(4095 / 4096f, 0, 0)));
            var focus = controls.Advance(new System.Numerics.Vector3(start / 4096f, 0, 0),
                maximumEdge ? ClassicCameraKeys.Left : ClassicCameraKeys.Right, TimeSpan.FromMilliseconds(20),
                maximumEdge ? ClassicCameraKeys.Right : ClassicCameraKeys.Left);
            Assert.Equal(results[1].Reads[0] / 4096f, focus.X);
            Assert.Equal((maximumEdge ? 3895 : 200) / 4096f, focus.X);
        }
    }

    [OracleFact]
    public void FirstScrollFrameMatchesNativeTimedAndCoastingHandlers()
    {
        const uint bridge = OriginalCpu.Scratch + 0x1000, table = OriginalCpu.Scratch + 0x100;
        const uint modeStub = OriginalCpu.Scratch + 0x200, argument = OriginalCpu.Scratch + 0x300;
        const uint cameraAddress = bridge + 0x3E5;
        uint focusX = cameraAddress + 0xD68, focusY = cameraAddress + 0xD6C;
        var requests = new List<OriginalCpu.Request>();
        var cases = new List<(int Result, System.Numerics.Vector3 Expected, string Label)>();
        foreach (ushort yaw in new ushort[] { 0, 1, 256, 512, 1023, 1536, 2047 })
            foreach (int zoom in new[] { 16384, 40960, 53247 })
                foreach (int milliseconds in new[] { 16, 33, 2500 })
                    foreach (bool fast in new[] { false, true })
                        foreach (var directions in new[] { ClassicCameraKeys.Right, ClassicCameraKeys.Left,
                            ClassicCameraKeys.Up, ClassicCameraKeys.Down, ClassicCameraKeys.Right | ClassicCameraKeys.Up })
                        {
                            var camera = new ClassicCamera(new CameraThing(new byte[84])
                            {
                                CameraId = 3, Zoom = zoom, MinZoom = 16384, MaxZoom = 53248,
                                Lens = 8192, AnglePitch = 220, Flags = 0x26,
                            }, new Dk2Angle(yaw));
                            var controls = new ClassicCameraControls(camera);
                            var expected = controls.Advance(System.Numerics.Vector3.Zero, directions | (fast ? ClassicCameraKeys.Fast : 0),
                                TimeSpan.FromMilliseconds(milliseconds));
                            var setup = new List<(uint, uint)>
                            {
                                (bridge, table), (table + 0xCC, modeStub),
                                // mov eax,3; ret: only the virtual mode query is replaced.
                                (modeStub, 0x000003B8), (modeStub + 4, 0x0000C300),
                                (bridge + 0x1165, (uint)zoom), (bridge + 0x1169, yaw),
                                (cameraAddress + 0xD92, 0), (cameraAddress + 0xDD8, 0),
                                (focusX, 0), (focusY, 0), (cameraAddress + 0xD70, 4095),
                                (cameraAddress + 0xD38, unchecked((uint)-1_000_000)),
                                (cameraAddress + 0xD3C, unchecked((uint)-1_000_000)),
                                (cameraAddress + 0xD40, 4096),
                                (cameraAddress + 0xD44, 1_000_000), (cameraAddress + 0xD48, 1_000_000),
                                (cameraAddress + 0xD4C, 4096),
                                (0x006F2590, 4), (0x0079570C, 0), (0x00795710, (uint)milliseconds),
                            };
                            int coastX = 0, coastY = 0;
                            foreach ((ClassicCameraKeys key, int x, int y) in new[]
                            {
                                (ClassicCameraKeys.Left, -1, 0), (ClassicCameraKeys.Right, 1, 0),
                                (ClassicCameraKeys.Up, 0, -1), (ClassicCameraKeys.Down, 0, 1),
                            })
                            {
                                if ((directions & key) == 0) continue;
                                var writes = new List<(uint, uint)>(setup)
                                {
                                    (argument, unchecked((uint)x)), (argument + 4, unchecked((uint)y)),
                                    (argument + 8, fast ? 1u : 0u),
                                };
                                requests.Add(new(0x00442140, [argument], writes.ToArray(), Ecx: bridge));
                                setup.Clear(); coastX += x * 2; coastY += y * 2;
                            }
                            requests.Add(new(0x00441FB0, [argument],
                                [(argument, unchecked((uint)(coastX * 64))),
                                 (argument + 4, unchecked((uint)(coastY * 64))), (argument + 8, 0)],
                                [focusX, focusY, cameraAddress + 0xD70], Ecx: bridge));
                            cases.Add((requests.Count - 1, expected, $"yaw={yaw}, zoom={zoom}, ms={milliseconds}, fast={fast}, keys={directions}"));
                        }
        var results = OriginalCpu.Run(requests);
        for (int i = 0; i < results.Count; i++) Assert.True(results[i].Fault is null, $"call{i}: {results[i].Fault}");
        foreach (var sample in cases)
        {
            var actual = results[sample.Result].Reads;
            Assert.True(sample.Expected.X == unchecked((int)actual[0]) / 4096f, sample.Label + " x");
            Assert.True(sample.Expected.Z == -unchecked((int)actual[1]) / 4096f, sample.Label + " z");
            Assert.Equal(4095u, actual[2]);
        }
    }
}
