// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>The creature overview's install art, shared by the Unity and Godot adapters (migration plan step 1b). The pins
/// are the shipped Creatures.kwd records read at their fixed offsets: a wrong offset changes a portrait or an order here.</summary>
public sealed class InstallCreatureOverviewArtTests
{
    private static CreatureOverviewType Type(byte id)
    {
        return new(id);
    }

    [InstallFact]
    public void PortraitsOrdersAndNamesComeFromTheCreatureRecordsAndText()
    {
        GameDatabase database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        using var sources = new InstallHudSources(Dk2InstallGate.Path);
        var art = new InstallCreatureOverviewArt(Dk2InstallGate.Path, database, sources.Texture, sources.Strings.Resolve);

        Assert.Equal(@"gui\creatures\evil\imp-bg", art.Portrait(Type(1)));
        Assert.Equal(1, art.Order(Type(1)));
        Assert.Equal(@"gui\creatures\evil\goblin-bg", art.Portrait(Type(7)));
        Assert.Equal(2, art.Order(Type(7)));
        Assert.Equal(@"gui\creatures\evil\black_knight-BG", art.Portrait(Type(22)));
        Assert.Equal(13, art.Order(Type(22)));
        // The heroes' princes use forward slashes in the record.
        Assert.Equal("gui/creatures/good/prince-00-bg", art.Portrait(Type(2)));
        Assert.Equal(29, art.Order(Type(2)));
        // A unique type has no portrait of its own; the overview counts it under its base type.
        Assert.Equal("", art.Portrait(Type(33)));
        Assert.Equal(33, art.Order(Type(33)));

        // Text.str's name (Imp's id 2771), whatever the fallback.
        Assert.Equal("Imp", art.Name(Type(1), "\u0001fallback"));
        Assert.Equal("Goblin", art.Name(Type(7), "\u0001fallback"));
        DK2.Art.Textures.RgbaImage portrait = art.Image(art.Portrait(Type(1)));
        Assert.True(portrait.Width > 0 && portrait.Height > 0);
    }
}
