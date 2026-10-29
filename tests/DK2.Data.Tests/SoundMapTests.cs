// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Media;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class SoundMapTests
{
    [InstallFact]
    public void FrontEndNamesTheGuiAndFrontEndArchives() =>
        Assert.Equal(new[] { @"Global\Gui", @"Global\FrontEnd" },
            BankMap.Read(File.ReadAllBytes(Map("front_endBANK.map"))).Archives.Select(a => a.Name));

    [InstallTheory]
    [InlineData("front_end", 4)]
    [InlineData("options", 18)]
    [InlineData("object_fe_parchment", 13)]
    public void ShippedGroupCountsMatchTheByteSurvey(string bank, int count) =>
        Assert.Equal(count, SfxMap.Read(File.ReadAllBytes(Map(bank + "SFX.map"))).Groups.Count);

    [Fact]
    public void TruncatedAndWrongSignatureMapsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => BankMap.Read(new byte[28]));
        Assert.Throws<InvalidDataException>(() => SfxMap.Read(new byte[28]));
    }

    private static string Map(string name) => Path.Combine(Dk2InstallGate.Path, "Data", "Sound", "SFX", "Global", name);
}
