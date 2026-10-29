// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Media;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public class SoundRequestsTests
{
    [InstallFact]
    public void HoverAndPressResolveThroughTheirOwnCategoriesAndOneBasedEntries()
    {
        string root = Path.Combine(Dk2InstallGate.Path, "Data", "Sound", "SFX", "Global");
        var hover = new SoundResolver(BankMap.Read(File.ReadAllBytes(Path.Combine(root, "front_endBANK.map"))),
            SfxMap.Read(File.ReadAllBytes(Path.Combine(root, "front_endSFX.map"))));
        Assert.Equal(new ResolvedSound(@"Global\FrontEnd", new SoundEntryId(0)), hover.Resolve(new SoundId(0x30f), new SoundRand(1)));
        var press = new SoundResolver(BankMap.Read(File.ReadAllBytes(Path.Combine(root, "object_fe_parchmentBANK.map"))),
            SfxMap.Read(File.ReadAllBytes(Path.Combine(root, "object_fe_parchmentSFX.map"))));
        Assert.Equal(new ResolvedSound(@"Global\Gui", new SoundEntryId(7)), press.Resolve(new SoundId(0x30a), new SoundRand(1)));
        Assert.Null(hover.Resolve(new SoundId(uint.MaxValue), new SoundRand(1)));
    }

    [Fact]
    public void CollapseAndTabExpansionRequestTheirNativeSounds()
    {
        var tabs = new BottomPanelTabs();
        tabs.Toggle();
        Assert.Equal(0x135u, Assert.Single(tabs.Sounds.Pending).Id.Value);
        tabs.Sounds.Clear();
        tabs.Click(BottomPanelCategory.Rooms, false);
        Assert.Equal(new uint[] { 0x104, 0x138 }, tabs.Sounds.Pending.Select(r => r.Id.Value));
        Assert.All(tabs.Sounds.Pending, r => Assert.Equal("gui_button_tab_rooms", r.Category.Name));
    }

    [Fact]
    public void RequestsRetainOrderUntilTheConsumerClearsThem()
    {
        var requests = new SoundRequests();
        requests.Add(new SoundRequest(SoundCategory.FrontEnd, new SoundId(0x30f)));
        requests.Add(new SoundRequest(SoundCategory.FrontEndClick, new SoundId(0x30a)));
        Assert.Equal(new uint[] { 0x30f, 0x30a }, requests.Pending.Select(r => r.Id.Value));
        requests.Clear();
        Assert.Empty(requests.Pending);
    }
}
