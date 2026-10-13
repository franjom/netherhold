// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art;
using DK2.Convert;
using DK2.Data;
using DK2.TestSupport;

namespace DK2.Convert.Tests;

public class ArtCatalogueTests
{
    [InstallFact]
    public void StationaryMovementUsesTheShippedPoseFrameInsteadOfGuiIconsOrWalking()
    {
        var database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var art = Dk2ArtCatalogue.Load(Dk2InstallGate.Path);
        var entries = ArtSourceMap.ForDatabase(database, art).ToDictionary(entry => entry.Reference.Key);
        var imp = database.Creatures.Values.Single(c => c.Name == "Imp");
        Assert.Equal("Imp-PoseFrame", art.CreatureStandingPoses[imp.CreatureId].Name);
        Assert.Equal((byte)5, art.CreatureStandingPoses[imp.CreatureId].Type);
        foreach (var creature in database.Creatures.Values)
        {
            var standing = art.CreatureStandingPoses[creature.CreatureId];
            var expected = standing.Type != 0 ? standing : art.Creatures[creature.CreatureId][0];
            var entry = entries[DK2.Assets.AssetRef.ForCreature(creature.Name,
                DK2.Assets.AssetKind.Animation, "movement_idle").Key];
            if (expected.IsEmpty) Assert.False(entry.HasSource);
            else Assert.Equal(expected, entry.Source);
        }
    }

    [InstallTheory]
    [InlineData("stunned", 23, 3)]
    [InlineData("getting_up", 26, 0)]
    public void DropRecoveryUsesTheAuthoredKnockdownAndGetUpResources(string clip, int slot, int fallback)
    {
        var database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        Dk2ArtCatalogue art = Dk2ArtCatalogue.Load(Dk2InstallGate.Path);
        var entries = ArtSourceMap.ForDatabase(database, art).ToDictionary(e => e.Reference.Key);
        foreach (var creature in database.Creatures.Values)
        {
            var resources = art.Creatures[creature.CreatureId];
            var expected = resources[slot].Type != 0 ? resources[slot] : resources[fallback];
            ArtEntry entry = entries[DK2.Assets.AssetRef.ForCreature(creature.Name,
                DK2.Assets.AssetKind.Animation, clip).Key];
            if (expected.IsEmpty) Assert.False(entry.HasSource);
            else Assert.Equal(expected, entry.Source);
        }
    }

    [InstallFact]
    public void PortalEntranceUsesTheAuthoredBlockThirtyAndPreservesItsClockMetadata()
    {
        var database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        Dk2ArtCatalogue art = Dk2ArtCatalogue.Load(Dk2InstallGate.Path);
        var entries = ArtSourceMap.ForDatabase(database, art).ToDictionary(e => e.Reference.Key);
        foreach (var creature in database.Creatures.Values)
        {
            ArtEntry entry = entries[DK2.Assets.AssetRef.ForCreature(creature.Name,
                DK2.Assets.AssetKind.Animation, "entrance").Key];
            var expected = art.Creatures[creature.CreatureId][29];
            if (expected.IsEmpty) Assert.False(entry.HasSource);
            else
            {
                Assert.Equal(expected, entry.Source);
                Assert.Equal(creature.EntranceFrames, entry.Source!.FrameCount);
                Assert.Equal(creature.EntranceFramesPerSecond, entry.Source.FramesPerSecond);
            }
        }
    }

    [InstallFact]
    public void ADeadGoblinResolvesToItsCorpseMeshInsteadOfWalking()
    {
        var database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        Dk2ArtCatalogue art = Dk2ArtCatalogue.Load(Dk2InstallGate.Path);
        ArtEntry entry = ArtSourceMap.ForDatabase(database, art).Single(e =>
            e.Reference.Key == "creature.goblin.animation.dead");

        Assert.True(entry.HasSource);
        Assert.Equal("Goblin-DiePose", entry.Source!.Name);
        Assert.Equal((byte)5, entry.Source.Type);
        ArtEntry mesh = ArtSourceMap.ForDatabase(database, art).Single(e =>
            e.Reference.Key == "creature.goblin.mesh.dead");
        Assert.Equal(entry.Source, mesh.Source);
    }

    [InstallFact]
    public void LandingUsesArtTwentyFourOrItsSingleFallbackFour()
    {
        string install = Dk2InstallGate.Path;
        var database = GameDatabaseLoader.Load(install);
        Dk2ArtCatalogue art = Dk2ArtCatalogue.Load(install);
        var entries = ArtSourceMap.ForDatabase(database, art).ToDictionary(e => e.Reference.Key);
        foreach (var creature in database.Creatures.Values)
        {
            var resources = art.Creatures[creature.CreatureId];
            ArtEntry entry = entries[DK2.Assets.AssetRef.ForCreature(creature.Name,
                DK2.Assets.AssetKind.Animation, "landing").Key];
            var expected = resources[23].Type != 0 ? resources[23] : resources[3];
            if (expected.IsEmpty) Assert.False(entry.HasSource);
            else Assert.Equal(expected, entry.Source);
        }
    }

    [InstallFact]
    public void ASpellCastUsesArtSix()
    {
        string install = Dk2InstallGate.Path;
        var database = GameDatabaseLoader.Load(install);
        Dk2ArtCatalogue art = Dk2ArtCatalogue.Load(install);
        var entries = ArtSourceMap.ForDatabase(database, art).ToDictionary(e => e.Reference.Key);
        foreach (var creature in database.Creatures.Values)
        {
            var resources = art.Creatures[creature.CreatureId];
            ArtEntry entry = entries[DK2.Assets.AssetRef.ForCreature(creature.Name,
                DK2.Assets.AssetKind.Animation, "casting").Key];
            var expected = resources[5].Type != 0 ? resources[5] : resources[4];
            if (expected.IsEmpty) Assert.False(entry.HasSource);
            else Assert.Equal(expected, entry.Source);
        }
    }

    [InstallFact]
    public void AGiantFightUsesTheMeleeResourceInsteadOfWalking()
    {
        string install = Dk2InstallGate.Path;
        var database = GameDatabaseLoader.Load(install);
        Dk2ArtCatalogue art = Dk2ArtCatalogue.Load(install);
        var giant = database.CreatureByName("Giant")!;
        ArtEntry entry = ArtSourceMap.ForDatabase(database, art).Single(e =>
            e.Reference.Key == "creature.giant.animation.fighting");
        Assert.Equal(art.Creatures[giant.CreatureId][4], entry.Source);
    }

    [InstallTheory]
    [InlineData("running", 1)]
    [InlineData("claiming", 12)]
    [InlineData("reinforcing", 11)]
    public void ImpActionsResolveToTheAuthoredWorkAndRunResources(string action, int fileSlot)
    {
        string install = Dk2InstallGate.Path;
        Dk2ArtCatalogue art = Dk2ArtCatalogue.Load(install);
        var database = GameDatabaseLoader.Load(install);
        ArtEntry entry = ArtSourceMap.ForDatabase(database, art).Single(e =>
            e.Reference.Key == "creature.imp.animation." + action);
        Dk2ArtResource requested = art.Creatures[1][fileSlot];
        Assert.Equal(action == "running" && requested.Type == 0 ? art.Creatures[1][0] : requested, entry.Source);
        Assert.True(entry.HasSource);
        if (action != "running") Assert.NotEqual(art.Creatures[1][0], entry.Source);
    }

    [InstallFact]
    public void ShippedArtRecordsMatchGameplayDefinitionCounts()
    {
        string install = Dk2InstallGate.Path;

        Dk2ArtCatalogue art = Dk2ArtCatalogue.Load(install);
        var database = GameDatabaseLoader.Load(install);

        Assert.Equal(database.Terrain.Count, art.Terrains.Count);
        Assert.Equal(database.Rooms.Count, art.Rooms.Count);
        Assert.Equal(database.Creatures.Count, art.Creatures.Count);
        Assert.Equal(database.Objects.Count, art.Objects.Count);
        Assert.Equal("ROCK WALL", art.Terrains[2][1].Name);
        Assert.Equal("Imp_Idle1", art.Creatures[1][19].Name);
        Assert.Equal("Gold Pile", art.Objects[1][0].Name);
    }

    [InstallFact]
    public void MealsResolveToArtThirteenOrItsIdleFallback()
    {
        string install = Dk2InstallGate.Path;
        Dk2ArtCatalogue art = Dk2ArtCatalogue.Load(install);
        var database = GameDatabaseLoader.Load(install);
        foreach (var creature in database.Creatures.Values)
        {
            ArtEntry entry = ArtSourceMap.ForDatabase(database, art).Single(e =>
                e.Reference.Key == DK2.Assets.AssetRef.ForCreature(creature.Name,
                    DK2.Assets.AssetKind.Animation, "eating").Key);
            var resources = art.Creatures[creature.CreatureId];
            Dk2ArtResource expected = resources[12].Type != 0 ? resources[12] : resources[7];
            if (expected.IsEmpty) Assert.False(entry.HasSource);
            else Assert.Equal(expected, entry.Source);
        }
    }

    /// <summary>A KMF clip's rate and frame count are its descriptor's words at <c>+0x48</c> and
    /// <c>+0x44</c>, the same fields the
    /// gameplay reader takes for the knockdown clock (catalogue index 23 is slot 24, Q442).</summary>
    [InstallFact]
    public void AnAnimationResourceCarriesItsDescriptorsRate()
    {
        string install = Dk2InstallGate.Path;
        Dk2ArtCatalogue art = Dk2ArtCatalogue.Load(install);
        var database = GameDatabaseLoader.Load(install);

        Dk2ArtResource knockdown = art.Creatures[1][23];
        Assert.Equal(6, knockdown.Type);
        Assert.True(knockdown.FramesPerSecond > 0);
        Assert.Equal(database.Creatures[1].KnockdownAnimation.FramesPerSecond, knockdown.FramesPerSecond);
        Assert.Equal(database.Creatures[1].KnockdownAnimation.FrameCount, knockdown.FrameCount);
        Assert.Equal(database.Creatures[1].KnockdownAnimation.Flags, knockdown.AnimationFlags);
        Assert.Equal(database.Creatures[1].KnockdownAnimation.StartSegmentFrames, knockdown.StartSegmentFrames);
        Assert.Equal(database.Creatures[1].KnockdownAnimation.EndSegmentFrames, knockdown.EndSegmentFrames);
    }
}
