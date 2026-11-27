// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>The bottom panel's install art, shared by the Unity and Godot adapters (migration plan step 1b). The pins are
/// the shipped KWD records read at their fixed offsets: a wrong offset changes a name, an order or an icon here.</summary>
public sealed class InstallBottomPanelArtTests
{
    private const string Fallback = "\u0001fallback";

    private static BottomPanelChoiceKey Spell(byte id)
    {
        return new(BottomPanelChoiceKind.Spell, id);
    }

    [InstallFact]
    public void SpellsDoorsAndTrapsReadTheirNamesOrdersAndIconsFromTheRecords()
    {
        GameDatabase database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        using var art = new InstallBottomPanelArt(Dk2InstallGate.Path, database);

        // Text.str's name, not the record's internal one ("Sight Of Evil").
        Assert.Equal("Sight of Evil", art.Name(Spell(1), Fallback));
        Assert.Equal("Create Imp", art.Name(Spell(4), Fallback));
        Assert.Equal(1, art.Order(Spell(4)));
        Assert.Equal(2, art.Order(Spell(6)));
        Assert.Equal(5, art.Order(Spell(1)));
        Assert.Equal(@"gui\spells\thunderbolt", art.Icon(Spell(6)));
        Assert.Equal(@"gui\spells\thunderbolt-2", art.UpgradedIcon(Spell(6)));

        var steel = new BottomPanelChoiceKey(BottomPanelChoiceKind.Door, 2);
        Assert.Equal("Steel Door", art.Name(steel, Fallback));
        Assert.Equal(4, art.Order(steel));
        Assert.Equal(@"gui\doors\steel_door", art.Icon(steel));
        var sentry = new BottomPanelChoiceKey(BottomPanelChoiceKind.Trap, 3);
        Assert.Equal("Sentry Trap", art.Name(sentry, Fallback));
        Assert.Equal(1, art.Order(sentry));
        Assert.Equal(@"gui\traps\sentry", art.Icon(sentry));
        foreach (string icon in new[] { art.Icon(steel)!, art.Icon(sentry)!, art.UpgradedIcon(Spell(6))! })
        {
            DK2.Art.Textures.RgbaImage image = art.Image(icon);
            Assert.True(image.Width > 0 && image.Height > 0, icon);
        }
    }

    [InstallFact]
    public void StoredSpecialsAndRoomsTakeTheirNameFromTextAndTheirIconFromTheRightArtSlot()
    {
        GameDatabase database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        using var art = new InstallBottomPanelArt(Dk2InstallGate.Path, database);

        // The record calls special 120 "Destroy Walls"; Text.str's name is "Weaken Walls". Its icon is art #1.
        var weaken = new BottomPanelChoiceKey(BottomPanelChoiceKind.Special, 120);
        Assert.Equal("Weaken Walls", art.Name(weaken, Fallback));
        Assert.Equal(@"GUI\Specials\SP-Destroy_Walls", art.Icon(weaken));
        var library = new BottomPanelChoiceKey(BottomPanelChoiceKind.Room, 6);
        Assert.Equal("Library", art.Name(library, Fallback));
        Assert.Equal(@"gui\rooms\dark_library", art.Icon(library));
        foreach (string icon in new[] { art.Icon(weaken)!, art.Icon(library)! })
        {
            DK2.Art.Textures.RgbaImage image = art.Image(icon);
            Assert.True(image.Width > 0 && image.Height > 0, icon);
        }
    }

    [InstallFact]
    public void EveryPanelChoiceHasANamedIconAndSpellOrdersDiffer()
    {
        GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(Dk2InstallGate.Path, "Level1");
        using var art = new InstallBottomPanelArt(Dk2InstallGate.Path, database);
        var view = new BottomPanelView(database, PlayerIds.FirstKeeper, art.Name, art.Order);
        Assert.NotEmpty(view.Choices);
        foreach (BottomPanelChoice choice in view.Choices)
        {
            Assert.False(string.IsNullOrEmpty(art.Icon(choice.Key)), choice.Key.ToString());
            Assert.NotEqual(Fallback, art.Name(choice.Key, Fallback));
        }
        Assert.True(database.KeeperSpells.Values.Select(s => art.Order(Spell(s.KeeperSpellId))).Distinct().Count() > 1);
        BottomPanelChoiceKey room = view.Choices.First(c => c.Kind == BottomPanelChoiceKind.Room).Key;
        DK2.Art.Textures.RgbaImage icon = art.Image(art.Icon(room)!);
        Assert.True(icon.Width > 0 && icon.Height > 0);
        Assert.NotNull(art.Font(1080));
    }

    [Fact]
    public void RejectsMissingSourcesOrDatabase()
    {
        Assert.Throws<ArgumentNullException>(() => new InstallBottomPanelArt((InstallHudSources)null!, null!));
        Assert.Throws<ArgumentNullException>(() => new InstallBottomPanelArt("x", null!));
        using var sources = new InstallHudSources(Path.Combine(Path.GetTempPath(), "dk2-none-" + Guid.NewGuid().ToString("N")));
        Assert.Throws<ArgumentNullException>(() => new InstallBottomPanelArt(sources, null!));
    }
}
