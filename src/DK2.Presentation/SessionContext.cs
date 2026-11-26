// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Campaign;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>The running session as each adapter fills it: the level and how it was started (a save's header fields), the
/// campaign slot it plays (<see cref="CampaignSlot.None"/> unless started from the campaign map) and the carry already
/// written to the profile. <see cref="SessionSaver"/> and the level end read it.</summary>
public sealed record SessionContext(string LevelName, GameSessionMode Mode, FrontEndPage ReturnPage)
{
    public CampaignSlot PlayedSlot { get; set; } = CampaignSlot.None;
    public LevelMap? ImportedTerrain { get; init; }

    /// <summary>The world's <see cref="CampaignCarry"/> as last written to the profile by <see cref="ApplyCarry"/>; a
    /// resumed session starts from the restored world's.</summary>
    public CampaignCarry CarryApplied { get; set; }

    /// <summary>A campaign level started from the front end's map, which records its result in the profile.</summary>
    public bool LaunchedFromFrontEnd => StartedFromFrontEnd(Mode, ReturnPage);

    /// <summary>The session a level scene starts from the request that replaced the last scene: a
    /// loaded save or a restart (<paramref name="resumed"/>) first, then the campaign map's launch (<paramref name="launched"/>),
    /// else the scripted arguments' level (Level1 when none), started as the map starts one with
    /// <paramref name="fromMap"/> and directly, returning to no page, without it.</summary>
    public static SessionContext Starting(GameSessionRequest? resumed, GameSessionRequest? launched, string? levelArgument,
        bool fromMap)
    {
        GameSessionRequest? request = resumed ?? launched;
        if (request is not null)
        {
            return new SessionContext(request.LevelName, request.Mode, request.ReturnPage) { ImportedTerrain = request.ImportedTerrain };
        }

        string level = levelArgument ?? "Level1";
        return new SessionContext(level, GameSessionMode.Campaign,
            fromMap ? GameSessionRequest.Campaign(level).ReturnPage : FrontEndPage.None);
    }

    /// <summary><see cref="LaunchedFromFrontEnd"/> for a session's mode and return page.</summary>
    public static bool StartedFromFrontEnd(GameSessionMode mode, FrontEndPage returnPage) =>
        mode == GameSessionMode.Campaign && returnPage == FrontEndPage.CampaignResults;

    /// <summary>Plays the profile's played slot when it names this level; a profile that names another map (edited
    /// between sessions) records nothing.</summary>
    public void PlaySlotOf(CampaignProfile profile)
    {
        CampaignSlot slot = CampaignProgression.PlayedSlot(profile ?? throw new ArgumentNullException(nameof(profile)));
        PlayedSlot = string.Equals(slot.LevelName, LevelName, StringComparison.OrdinalIgnoreCase) ? slot : CampaignSlot.None;
    }

    /// <summary>The profile writes specials 129 and 140 make in DK2 the moment they act: each Locate Hidden Land since the
    /// last call unlocks the played map's hidden land, a stored creature replaces the profile's, and the profile is saved.
    /// Nothing happens while <paramref name="carry"/> equals <see cref="CarryApplied"/>.</summary>
    public void ApplyCarry(CampaignProfileStore profiles, CampaignCarry carry)
    {
        if (profiles is null)
        {
            throw new ArgumentNullException(nameof(profiles));
        }

        CampaignCarry applied = CarryApplied;
        if (carry.HiddenLandFound == applied.HiddenLandFound && carry.TransferStored == applied.TransferStored
            && carry.TransferCreatureTypeId == applied.TransferCreatureTypeId
            && carry.TransferCreatureLevel == applied.TransferCreatureLevel)
        {
            return;
        }

        for (int i = applied.HiddenLandFound; i < carry.HiddenLandFound; i++)
        {
            if (profiles.Profile!.LevelNumber is >= 1 and <= 30)
            {
                profiles.Profile = CampaignProgression.LocateHiddenLand(profiles.Profile, new CampaignSlot(profiles.Profile.LevelNumber));
            }
        }

        if (carry.TransferStored)
        {
            profiles.Profile = profiles.Profile! with
            {
                TransferCreatureId = carry.TransferCreatureTypeId, TransferCreatureLevel = carry.TransferCreatureLevel,
            };
        }

        CarryApplied = carry;
        profiles.Save();
    }
}
