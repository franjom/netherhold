// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using System.Numerics;
using DK2.Art.Kmf;
using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

public class SceneFrameTests
{
    /// <summary>Records every host call; each creature's model, clip marks and points are set per test.</summary>
    private sealed class RecordingHost : ISceneFrameHost
    {
        public readonly List<string> Calls = [];
        public readonly HashSet<int> WithoutModel = [];
        public readonly Dictionary<int, int> Crossings = [];
        public readonly Dictionary<(int, uint), Vector3> Points = [];
        public bool TakesGemSparkles { get; set; } = true;
        public long Clock;

        private void Log(string call)
        {
            Calls.Add(call);
        }

        public void BeginFrame()
        {
            Log("begin");
        }

        public void AdvanceHeartHealth()
        {
            Log("hearts");
        }

        public bool PlaceCreature(CreatureId creature, in CreatureView pose, DrawPoint drawn)
        {
            Log(string.Create(CultureInfo.InvariantCulture, $"place {creature.Value} {drawn.X} {drawn.Y} {drawn.Height}"));
            return !WithoutModel.Contains(creature.Value);
        }

        public void DrawCreatureClip(CreatureId creature, in CreatureView pose, double seconds)
        {
            Log(string.Create(CultureInfo.InvariantCulture, $"clip {creature.Value} {seconds}"));
        }

        public void HideCreatureModel(CreatureId creature)
        {
            Log($"hide {creature.Value}");
        }

        public int FootstepCrossing(CreatureId creature)
        {
            Log($"marks {creature.Value}");
            return Crossings.TryGetValue(creature.Value, out int crossing) ? crossing : 0;
        }

        public void RequestFootstep(CreatureId creature, in CreatureView pose, int crossing)
        {
            Log($"footstep {creature.Value} {crossing}");
        }

        public void DrawBurningRed(CreatureId creature, in CreatureView pose)
        {
            Log($"red {creature.Value}");
        }

        public void DrawCrackle(CreatureId creature, GameTick tick)
        {
            Log($"crackle {creature.Value} {tick.Value}");
        }

        public bool TryGetControlPoint(CreatureId creature, uint point, out Vector3 render)
        {
            Log($"point {creature.Value} {point}");
            return Points.TryGetValue((creature.Value, point), out render);
        }

        public EffectTime EffectClock()
        {
            Log($"clock {Clock}");
            return new EffectTime(Clock);
        }

        public void RequestGemSparkle(FpPosition3 point)
        {
            Log($"sparkle {point.X.Raw} {point.Y.Raw} {point.Z.Raw}");
        }

        public void DrawObjects(GameTick tick, float fraction)
        {
            Log(string.Create(CultureInfo.InvariantCulture, $"objects {tick.Value} {fraction}"));
        }

        public void RefreshTorches()
        {
            Log("torches");
        }
    }

    /// <summary>Unity's <c>DungeonScene.DrawCreatureFrame</c> as it stood at f49bcbba, over the same host: the hover overlay's
    /// frame, the clamp and frame time, the Hearts, then per creature in the pose table's order the eight steps, then the
    /// objects and the torch refresh.</summary>
    private sealed class LegacyFrame(ISceneFrameHost host)
    {
        public readonly Dictionary<int, CreatureView> Poses = [];
        public readonly CreatureHeadTable Heads = new CreatureHeadTable();
        public int HiddenCreatureId;
        private readonly GemSparkleGate _gemSparkles = new GemSparkleGate();
        private double _renderSeconds;

        public void Draw(GameTick tick, float fraction)
        {
            host.BeginFrame();
            fraction = fraction < 0f ? 0f : fraction > 1f ? 1f : fraction;
            _renderSeconds = PoseInterpolator.DrawSeconds(tick, fraction);
            host.AdvanceHeartHealth();
            foreach (KeyValuePair<int, CreatureView> entry in Poses)
            {
                CreatureView pose = entry.Value;
                DrawPoint drawn = PoseInterpolator.Position(pose, fraction);
                if (!host.PlaceCreature(new CreatureId(entry.Key), pose, drawn))
                {
                    continue;
                }

                host.DrawCreatureClip(new CreatureId(entry.Key), pose, _renderSeconds);
                if (entry.Key == HiddenCreatureId)
                {
                    host.HideCreatureModel(new CreatureId(entry.Key));
                }

                int crossing = host.FootstepCrossing(new CreatureId(entry.Key));
                if (crossing != 0)
                {
                    host.RequestFootstep(new CreatureId(entry.Key), pose, crossing);
                }

                if (pose.BurningRed)
                {
                    host.DrawBurningRed(new CreatureId(entry.Key), pose);
                }

                host.DrawCrackle(new CreatureId(entry.Key), tick);
                Heads.Set(new CreatureId(entry.Key), host.TryGetControlPoint(new CreatureId(entry.Key), KmfControlPoint.Head,
                    out Vector3 head) ? RenderWorld.ToNative(head) : null);
                if (pose.GemCarrier && host.TakesGemSparkles
                    && host.TryGetControlPoint(new CreatureId(entry.Key), CreatureView.GemDustSparkle.ControlPoint, out Vector3 sparkle)
                    && _gemSparkles.TryPass(new CreatureId(entry.Key), host.EffectClock()))
                {
                    host.RequestGemSparkle(RenderWorld.ToNative(sparkle));
                }
            }
            host.DrawObjects(tick, fraction);
            host.RefreshTorches();
        }
    }

    private static CreatureView Creature(int id, int x = 3, bool burning = false, bool gem = false)
    {
        return new CreatureView(id, new PlayerId(1), posX: x * Fp32.One, posY: 5 * Fp32.One, default, default, 0, height: Fp32.One / 2,
            previousPosX: (x - 1) * Fp32.One, previousPosY: 5 * Fp32.One, gemCarrier: gem, burningRed: burning);
    }

    /// <summary>Five creatures covering every branch: no model, a hidden one, footsteps before and after the wrap, burning,
    /// gem carriers with and without the sparkle point, heads present and absent.</summary>
    private static RecordingHost Staged()
    {
        var host = new RecordingHost();
        host.WithoutModel.Add(2);
        host.Crossings[3] = 1;
        host.Crossings[4] = 2;
        host.Points[(3, KmfControlPoint.Head)] = new Vector3(1.25f, 0.75f, -2.5f);
        host.Points[(4, CreatureView.GemDustSparkle.ControlPoint)] = new Vector3(4f, 1f, -6f);
        host.Points[(5, KmfControlPoint.Head)] = new Vector3(7f, 0.5f, -8f);
        host.Points[(5, CreatureView.GemDustSparkle.ControlPoint)] = new Vector3(7.5f, 0.5f, -8.5f);
        return host;
    }

    private static readonly CreatureView[] Cast =
    [
        Creature(3, x: 4, burning: true), Creature(2), Creature(5, gem: true), Creature(4, gem: true, burning: true), Creature(9, gem: true),
    ];

    [Theory]
    [InlineData(true, 4)]
    [InlineData(false, 4)]
    [InlineData(true, 0)]
    public void TheFrameDrawsInTheLegacyOrder(bool takesSparkles, int hidden)
    {
        RecordingHost legacyHost = Staged(), host = Staged();
        legacyHost.TakesGemSparkles = host.TakesGemSparkles = takesSparkles;
        var legacy = new LegacyFrame(legacyHost) { HiddenCreatureId = hidden };
        var frame = new SceneFrame(host) { HiddenCreature = new CreatureId(hidden) };
        foreach (CreatureView pose in Cast)
        {
            legacy.Poses[pose.Id] = pose;
            legacy.Heads.Add(new CreatureId(pose.Id));
            frame.Add(new CreatureId(pose.Id), pose);
        }
        // A removal and a re-add reorder the table in both: the frame draws in the pose table's order.
        legacy.Poses.Remove(2); legacy.Poses[2] = Cast[1];
        frame.Remove(new CreatureId(2), keepHead: true); frame.Add(new CreatureId(2), Cast[1]);
        float[] fractions = [.25f, 1.5f, -.5f, .75f, float.NaN];
        long[] clocks = [0, 20, 60, 61, 130];
        for (int i = 0; i < fractions.Length; i++)
        {
            legacyHost.Clock = host.Clock = clocks[i];
            legacy.Draw(new GameTick(40 + i), fractions[i]);
            frame.Draw(new GameTick(40 + i), fractions[i]);
        }
        Assert.Equal(legacyHost.Calls, host.Calls);
        foreach (CreatureView pose in Cast)
        {
            Assert.Equal(legacy.Heads.Attachment(new CreatureId(pose.Id), KmfControlPoint.Head),
                frame.Heads.Attachment(new CreatureId(pose.Id), KmfControlPoint.Head));
        }
    }

    [Fact]
    public void OneCreaturesStepsAreInOrder()
    {
        RecordingHost host = Staged();
        var frame = new SceneFrame(host) { HiddenCreature = new CreatureId(4) };
        frame.Add(new CreatureId(4), Creature(4, gem: true, burning: true));
        frame.Draw(new GameTick(8), .5f);
        Assert.Equal(
        [
            "begin", "hearts", "place 4 2.5 5 0.5", "clip 4 2.125", "hide 4", "marks 4", "footstep 4 2", "red 4", "crackle 4 8",
            "point 4 5000", "point 4 10001", "clock 0", "sparkle 16384 24576 4096", "objects 8 0.5", "torches",
        ], host.Calls);
        Assert.Equal(2.125, frame.DrawSeconds);
    }

    [Fact]
    public void ACreatureWithoutAModelIsPlacedAndSkipped()
    {
        RecordingHost host = Staged();
        var frame = new SceneFrame(host);
        frame.Add(new CreatureId(2), Creature(2, burning: true));
        frame.Draw(new GameTick(1), 1f);
        Assert.Equal(["begin", "hearts", "place 2 3 5 0.5", "objects 1 1", "torches"], host.Calls);
    }

    [Fact]
    public void TheHeadIsTheDrawnPointInNativeUnitsOrNone()
    {
        RecordingHost host = Staged();
        var frame = new SceneFrame(host);
        frame.Add(new CreatureId(3), Creature(3));
        frame.Add(new CreatureId(4), Creature(4));
        Assert.Equal(EffectAttachment.NotFound, frame.Heads.Attachment(new CreatureId(3), KmfControlPoint.Head));
        frame.Draw(new GameTick(1), 0f);
        Assert.Equal(EffectAttachment.At(new FpPosition3(new Fp32(5120), new Fp32(10240), new Fp32(3072))),
            frame.Heads.Attachment(new CreatureId(3), KmfControlPoint.Head));
        Assert.Equal(EffectAttachment.NotFound, frame.Heads.Attachment(new CreatureId(4), KmfControlPoint.Head));
    }

    [Fact]
    public void TheSparkleIsPacedByTheQuantumAndForgottenWithItsCreature()
    {
        RecordingHost host = Staged();
        var frame = new SceneFrame(host);
        frame.Add(new CreatureId(4), Creature(4, gem: true));
        int Sparkles()
        {
            return host.Calls.Count(call => call.StartsWith("sparkle", StringComparison.Ordinal));
        }

        frame.Draw(new GameTick(1), 0f);
        host.Clock = GemSparkleGate.QuantumMilliseconds - 1;
        frame.Draw(new GameTick(1), 0f);
        Assert.Equal(1, Sparkles());
        frame.Remove(new CreatureId(4), keepHead: false);
        frame.Add(new CreatureId(4), Creature(4, gem: true));
        frame.Draw(new GameTick(1), 0f);
        Assert.Equal(2, Sparkles());
    }

    [Fact]
    public void ARemovedCreatureIsGoneAndAKeptHeadStays()
    {
        RecordingHost host = Staged();
        var frame = new SceneFrame(host);
        frame.Add(new CreatureId(3), Creature(3));
        frame.Add(new CreatureId(5), Creature(5));
        frame.Draw(new GameTick(1), 0f);
        frame.Remove(new CreatureId(3), keepHead: true);
        frame.Remove(new CreatureId(5), keepHead: false);
        Assert.Equal(0, frame.Creatures.Count);
        Assert.NotEqual(EffectAttachment.Gone, frame.Heads.Attachment(new CreatureId(3), KmfControlPoint.Head));
        Assert.Equal(EffectAttachment.Gone, frame.Heads.Attachment(new CreatureId(5), KmfControlPoint.Head));
        // A new look of the same creature keeps the head it had.
        frame.Add(new CreatureId(3), Creature(3, x: 9));
        Assert.Equal(9 * Fp32.One, frame.Creatures[new CreatureId(3)].PosX);
        Assert.NotEqual(EffectAttachment.NotFound, frame.Heads.Attachment(new CreatureId(3), KmfControlPoint.Head));
        host.Calls.Clear();
        frame.Draw(new GameTick(2), 0f);
        Assert.DoesNotContain(host.Calls, call => call.StartsWith("place 5", StringComparison.Ordinal));
    }

    [Fact]
    public void SetReplacesTheViewAndClearDropsEverythingButTheHidden()
    {
        var frame = new SceneFrame(Staged()) { HiddenCreature = new CreatureId(3) };
        frame.Add(new CreatureId(3), Creature(3));
        frame.Set(new CreatureId(3), Creature(3, x: 7));
        Assert.True(frame.Creatures.TryGetValue(new CreatureId(3), out CreatureView pose));
        Assert.Equal(7 * Fp32.One, pose.PosX);
        frame.SetFrameTime(new GameTick(10), .5f);
        Assert.Equal(2.625, frame.DrawSeconds);
        frame.Clear();
        Assert.Equal(0, frame.Creatures.Count);
        Assert.Equal(0d, frame.DrawSeconds);
        Assert.Equal(EffectAttachment.Gone, frame.Heads.Attachment(new CreatureId(3), KmfControlPoint.Head));
        Assert.Equal(new CreatureId(3), frame.HiddenCreature);
    }

    /// <summary>A host that draws nothing, for the allocation check.</summary>
    private sealed class QuietHost : ISceneFrameHost
    {
        public void BeginFrame() { }
        public void AdvanceHeartHealth() { }
        public bool PlaceCreature(CreatureId creature, in CreatureView pose, DrawPoint drawn)
        {
            return true;
        }

        public void DrawCreatureClip(CreatureId creature, in CreatureView pose, double seconds) { }
        public void HideCreatureModel(CreatureId creature) { }
        public int FootstepCrossing(CreatureId creature)
        {
            return 1;
        }

        public void RequestFootstep(CreatureId creature, in CreatureView pose, int crossing) { }
        public void DrawBurningRed(CreatureId creature, in CreatureView pose) { }
        public void DrawCrackle(CreatureId creature, GameTick tick) { }

        public bool TryGetControlPoint(CreatureId creature, uint point, out Vector3 render)
        {
            render = new Vector3(creature.Value, 1f, -2f);
            return true;
        }

        public bool TakesGemSparkles
        {
            get
            {
                return true;
            }
        }

        public EffectTime EffectClock()
        {
            return new EffectTime(0);
        }

        public void RequestGemSparkle(FpPosition3 point) { }
        public void DrawObjects(GameTick tick, float fraction) { }
        public void RefreshTorches() { }
    }

    [Fact]
    public void ADrawnFrameAllocatesNothing()
    {
        var frame = new SceneFrame(new QuietHost()) { HiddenCreature = new CreatureId(4) };
        foreach (CreatureView pose in Cast)
        {
            frame.Add(new CreatureId(pose.Id), pose);
        }

        frame.Draw(new GameTick(1), .5f);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10; i++)
        {
            frame.Draw(new GameTick(2 + i), .5f);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
