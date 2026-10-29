// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Media;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class SoundBankTests
{
    [InstallFact]
    public void DecodedClipsAreReusedForTheSessionAndBlankEntriesAreSilent()
    {
        var bank = new SoundBank(SdtArchive.Open(Path.Combine(Dk2InstallGate.Path,
            "Data", "Sound", "SFX", "Global", "FrontEndHD.sdt")));
        var first = bank.GetClip(new SoundEntryId(0));
        Assert.NotNull(first);
        Assert.Same(first, bank.GetClip(new SoundEntryId(0)));
        Assert.Null(bank.GetClip(new SoundEntryId(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => bank.GetClip(new SoundEntryId(-1)));
    }
}
