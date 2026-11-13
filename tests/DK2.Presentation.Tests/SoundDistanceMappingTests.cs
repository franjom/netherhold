// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Media;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>The QMixer distance mapping <c>SoundInstance_BindVoice</c> (<c>0x0060CDB0</c>) builds for a voice: one tile at
/// full volume, the item's own maximum (<c>+0x22</c>, mixer units of two per tile, floored to 2.1) and scale 0.5.</summary>
public sealed class SoundDistanceMappingTests
{
    [Theory]
    [InlineData(0f, 2.1f)]
    [InlineData(1f, 2.1f)]
    [InlineData(2f, 2.1f)]
    [InlineData(2.0001f, 2.0001f)]
    [InlineData(float.NaN, 2.1f)]
    [InlineData(20f, 20f)]
    public void TheMaximumIsTheItemsFloatFlooredAt2Point1(float stored, float expected)
    {
        var bytes = new byte[34];
        BitConverter.GetBytes(stored).CopyTo(bytes, 0x22 - 8);
        var item = new SoundItem([], [], bytes);

        Assert.Equal(new SoundDistanceMapping(2f, expected, 0.5f), SoundDistanceMapping.Of(item));
    }

    /// <summary>DK2's mapping (2, max, 0.5) gives 4 / (d + 2) between one tile and the maximum, full volume
    /// within one tile and silence past the maximum (QMIXER.DLL <c>0x18009150</c>/<c>0x18009460</c>).</summary>
    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(2f, 1f)]
    [InlineData(6f, 0.5f)]
    [InlineData(14f, 0.25f)]
    [InlineData(20f, 4f / 22f)]
    [InlineData(20.01f, 0f)]
    public void TheFactorIsFullWithinOneTileFallsAsFourOverDPlusTwoAndCutsOffPastTheMaximum(float d, float factor) =>
        Assert.Equal(factor, new SoundDistanceMapping(2f, 20f, 0.5f).Factor(d), 5);

    [Fact]
    public void TheRolloffCurveFollowsTheFactorThenStepsToSilencePastTheMaximum()
    {
        var mapping = new SoundDistanceMapping(2f, 20f, 0.5f);
        var points = new List<(float X, float Y)>();
        mapping.RolloffPoints(points);

        Assert.Equal((0f, 1f), points[0]);
        Assert.Contains(points, p => Math.Abs(p.X * mapping.CurveRange - 2f) < 1e-4f && p.Y == 1f);
        Assert.Equal((1f, 0f), points[^1]);
        (float atMax, float factorAtMax) = points[^2];
        Assert.Equal(20f / mapping.CurveRange, atMax, 5);
        Assert.Equal(mapping.Factor(20f), factorAtMax, 5);
        Assert.All(points.Take(points.Count - 1), p => Assert.Equal(mapping.Factor(p.X * mapping.CurveRange), p.Y, 4));
    }

    /// <summary>The shipped maps: an Imp's spatial sounds reach ten tiles (20 units), and the Stone Knight carries some
    /// that reach fifty (100 units).</summary>
    [InstallFact]
    public void ShippedCreatureSoundsCarryTheirOwnMaximum()
    {
        Assert.Contains(Maxima("creature_imp"), m => m == 20f);
        Assert.Contains(Maxima("creature_stone_knight"), m => m == 100f);
    }

    /// <summary>A group whose flag word (group <c>+0x10</c>) has <c>0x200</c> is head-relative (instance flag 8) and gets no
    /// mapping (<c>0x0060CDB0</c>); every other group gets one.</summary>
    [InstallFact]
    public void AHeadRelativeGroupHasNoMapping()
    {
        string root = Path.Combine(Dk2InstallGate.Path, "Data", "Sound", "SFX", "Global");
        int headRelative = 0, spatial = 0;
        foreach (string sfx in Directory.GetFiles(root, "*SFX.map"))
        {
            SfxMap map = SfxMap.Read(File.ReadAllBytes(sfx));
            var resolver = new SoundResolver(BankMap.Empty, map);
            foreach (SoundGroup group in map.Groups.Where(g => g.Items.Count > 0))
            {
                bool flagged = (BitConverter.ToUInt16(group.Unknown0x08, 8) & 0x200) != 0;
                Assert.Equal(flagged, resolver.DistanceMappingOf(group.Id) is null);
                if (flagged) headRelative++; else spatial++;
            }
        }
        Assert.True(headRelative > 0 && spatial > 0, $"head-relative {headRelative}, spatial {spatial}");
    }

    private static float[] Maxima(string category)
    {
        string root = Path.Combine(Dk2InstallGate.Path, "Data", "Sound", "SFX", "Global");
        var resolver = new SoundResolver(BankMap.Read(File.ReadAllBytes(Path.Combine(root, category + "BANK.map"))),
            SfxMap.Read(File.ReadAllBytes(Path.Combine(root, category + "SFX.map"))));
        SfxMap map = SfxMap.Read(File.ReadAllBytes(Path.Combine(root, category + "SFX.map")));
        return map.Groups.Select(g => resolver.DistanceMappingOf(g.Id)).OfType<SoundDistanceMapping>()
            .Select(m => m.Maximum).ToArray();
    }
}
