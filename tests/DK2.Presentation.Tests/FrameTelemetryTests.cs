// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;

namespace DK2.Presentation.Tests;

public sealed class FrameTelemetryTests
{
    // One timestamp tick a microsecond, so 1000 ticks are a millisecond.
    private const long Frequency = 1_000_000;

    private static string[] Lines(FrameTelemetry telemetry)
    {
        var text = new StringWriter();
        telemetry.WriteCsv(text);
        return text.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public void TheHeaderIsUnityRecordersColumns()
    {
        // tools/perf/draw_clock.py and tools/tester-package read these names.
        Assert.Equal("frame,play_seconds,frame_ms,update_ms,ticks,tick_ms,sound_ms,wait_tick_ms,script_ms,input_ms,present_ms,"
            + "animated_frame_ms,chunk_rebuild_ms,lights_ms,hud_ms,gc_collections,render_tick,cam_x,cam_y,cam_z,path",
            FrameTelemetry.Header);
        Assert.Equal(FrameTelemetry.Header, Lines(new FrameTelemetry(Frequency, 4)).Single());
    }

    [Fact]
    public void ARowHoldsItsIntervalPartsTicksClockCameraAndPath()
    {
        var telemetry = new FrameTelemetry(Frequency, 4);
        telemetry.BeginFrame(10_000, gen0Collections: 7);
        telemetry.EndFrame(11_000, new FrameTelemetryEnd(0, 0, 1, Vector3.Zero, true, 0, 7));
        telemetry.BeginFrame(26_000, gen0Collections: 7);
        telemetry.Spent(FrameTelemetryPart.Sound, 26_000, 26_250);
        telemetry.Spent(FrameTelemetryPart.Sound, 27_000, 27_250);
        telemetry.Spent(FrameTelemetryPart.Present, 27_250, 29_250);
        telemetry.Spent(FrameTelemetryPart.Hud, 29_250, 30_000);
        telemetry.EndFrame(31_000, new FrameTelemetryEnd(2, 1.25, 1.0625, new Vector3(14.5f, 9, -24.5f), false, .5, 9));

        string[] lines = Lines(telemetry);
        Assert.Equal(3, lines.Length);
        string zeroParts = string.Join(",", Enumerable.Repeat("0.000", 9));
        Assert.Equal("0,0.000,0.000,1.000,0,0.000," + zeroParts + ",0,1.0000,0.0000,0.0000,0.0000,1", lines[1]);
        Assert.Equal("1,0.016,16.000,5.000,2,1.250,0.500,0.000,0.000,0.000,2.000,0.000,0.500,0.000,0.750,2,1.0625,14.5000,9.0000,-24.5000,0",
            lines[2]);
        Assert.Equal(.016, telemetry.PlaySeconds, 9);
    }

    [Fact]
    public void AFrameThatNeverEndsLeavesNoRowAndItsPartsDoNotCarry()
    {
        var telemetry = new FrameTelemetry(Frequency, 4);
        telemetry.BeginFrame(0, 0);
        telemetry.Spent(FrameTelemetryPart.Script, 0, 5_000);
        telemetry.BeginFrame(16_000, 0);
        telemetry.EndFrame(17_000, new FrameTelemetryEnd(1, 0, 0, Vector3.Zero, false, 0, 0));

        Assert.Equal(1, telemetry.Count);
        Assert.Equal("0.000", Lines(telemetry)[1].Split(',')[8]);
        Assert.Equal("16.000", Lines(telemetry)[1].Split(',')[2]);
    }

    [Fact]
    public void AFullTelemetryCountsTheRestAsDroppedAndWritesTheFirst()
    {
        var telemetry = new FrameTelemetry(Frequency, 2);
        for (int i = 0; i < 5; i++)
        {
            telemetry.BeginFrame(i * 16_000, 0);
            telemetry.Spent(FrameTelemetryPart.Lights, i * 16_000, i * 16_000 + 1_000);
            telemetry.EndFrame(i * 16_000 + 2_000, new FrameTelemetryEnd(1, 0, i, Vector3.Zero, false, 0, 0));
        }
        Assert.Equal((2, 3), (telemetry.Count, telemetry.Dropped));
        Assert.Equal(3, Lines(telemetry).Length);
        Assert.Throws<InvalidOperationException>(() =>
            new FrameTelemetry(Frequency, 1).EndFrame(0, new FrameTelemetryEnd(0, 0, 0, Vector3.Zero, false, 0, 0)));
    }

    [Fact]
    public void MetadataListsTheEngineLinesThenTheRun()
    {
        var telemetry = new FrameTelemetry(Frequency, 2);
        telemetry.BeginFrame(0, 0);
        telemetry.EndFrame(1_000, new FrameTelemetryEnd(0, 0, 0, Vector3.Zero, false, 0, 0));
        var text = new StringWriter();
        telemetry.WriteMetadata(text, [new KeyValuePair<string, string>("Godot", "4.8"), new KeyValuePair<string, string>("Resolution", "1920x1080")], "Level8", 1);
        Assert.Equal("Godot=4.8\nResolution=1920x1080\nLevel=Level8\nSeed=1\nSamples=1\nDropped=0\nPlaySeconds=0.0\n", text.ToString());
    }

    [Fact]
    public void ADirectoryWithAFramesOrMetadataFileHoldsACapture()
    {
        string directory = Path.Combine(Path.GetTempPath(), "dk2-telemetry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Assert.False(FrameTelemetry.HoldsCapture(directory));
            File.WriteAllText(Path.Combine(directory, FrameTelemetry.MetadataFile), "");
            Assert.True(FrameTelemetry.HoldsCapture(directory));
            File.Delete(Path.Combine(directory, FrameTelemetry.MetadataFile));
            File.WriteAllText(Path.Combine(directory, FrameTelemetry.FramesFile), "");
            Assert.True(FrameTelemetry.HoldsCapture(directory));
        }
        finally { Directory.Delete(directory, true); }
    }
}
