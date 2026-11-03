// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Media;
using DK2.Data.Settings;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>What the interface requests of W8 find in the shipped maps (docs/research/sound-effects-interface.md): each request has samples in its category, and the rows marked silent have none.</summary>
public sealed class InterfaceSoundMapsTests
{
    private static string Root => Path.Combine(Dk2InstallGate.Path, "Data", "Sound", "SFX", "Global");

    private static int Samples(string category, uint id)
    {
        SfxMap map = SfxMap.Read(File.ReadAllBytes(Path.Combine(Root, category + "SFX.map")));
        return map.Groups.Where(g => g.Id.Value == id).Sum(g => g.Items.Sum(i => i.Sounds.Count));
    }

    [InstallTheory]
    [InlineData(141)]
    [InlineData(143)]
    [InlineData(144)]
    [InlineData(126)]
    public void EverySliderTestSoundTheCodeAsksHasSamplesInItsCategory(ushort slider)
    {
        var options = new OptionsPagesController(new GameSettings());
        options.Enter(FrontEndPage.Sound);
        options.BeginSliderDrag(slider);
        SoundRequest request = Assert.Single(options.Sounds.Pending);
        Assert.True(Samples(request.Category.Name, request.Id.Value) > 0, $"{request.Category.Name} 0x{request.Id.Value:X}");
    }

    [InstallTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void TheChimeHasSamplesInEverySoundSlotCategory(int slot)
    {
        SoundRequest request = FlashChime.Request(slot);
        Assert.True(Samples(request.Category.Name, request.Id.Value) > 0, request.Category.Name);
    }

    [InstallFact]
    public void TheLandSoundHasASampleInTheMapObjectsCategory()
    {
        Assert.Equal(1, Samples("object_fe_map", 0x32A));
    }

    [InstallFact]
    public void TheEffectsSliderTestIsTheParchmentsAndOptionsItselfHasNoSuchGroup()
    {
        Assert.Equal(3, Samples("object_fe_parchment", 0x31D));
        Assert.Equal(0, Samples("options", 0x31D));
    }

    [InstallFact]
    public void TheSpeechAndMusicTestsAreOneSamplePerItem()
    {
        Assert.Equal(10, Samples("options_speech", 0x345));
        Assert.Equal(16, Samples("options_music", 0x346));
    }

    [InstallTheory]
    [InlineData("gui_button_tab_new_combat", 0x32Bu)]
    [InlineData("gui_button_tab_new_combat", 0x32Cu)]
    [InlineData("gui_button_size", 0x133u)]
    [InlineData("gui_button_size", 0x134u)]
    [InlineData("gui_button_size", 0x135u)]
    [InlineData("options", 0x33Cu)]
    [InlineData("options", 0x341u)]
    [InlineData("one_shot_atmos", 0x2EAu)]
    public void TheGroupsOfTheWaitingRowsExistInTheCategoriesTheTableNames(string category, uint id)
    {
        Assert.True(Samples(category, id) > 0);
    }

    // The mana warning 0xF (0x004C1226) asks the Dungeon Heart's category: no category of the effects banks has it, so DK2 is silent there.
    [InstallFact]
    public void TheManaWarningGroupIsInNoGlobalCategoryAndGroupOneOnlyInSpeech()
    {
        foreach (string map in Directory.GetFiles(Root, "*SFX.map"))
        {
            string category = Path.GetFileNameWithoutExtension(map)[..^3];
            Assert.Equal(0, Samples(category, 0xF));
            Assert.Equal(0, Samples(category, 0x1));
        }
    }
}
