// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Data.Text;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>The front end's glue each engine runs the same way: the campaign map over its stores, typed hi-score names, the campaign map's pick table,
/// the scenery's creature clock and the movie gallery over an install.</summary>
public static class FrontEndHost
{
    /// <summary>The campaign map over loaded stores, in the order both adapters start it: the map on the live profile and
    /// <paramref name="hiScores"/>, its table tracked by <paramref name="hiScoreStore"/>, <paramref name="profileChanged"/>
    /// subscribed to <see cref="CampaignProfileStore.Changed"/>, then the profile tracked. The caller unsubscribes it.</summary>
    public static CampaignMapController OpenCampaign(FrontEndSession session, CampaignProfileStore profiles, HiScoreStore hiScoreStore,
        IReadOnlyList<HiScoreEntry> hiScores, Func<CampaignSlot, CampaignBriefing> briefing, Action<CampaignProfile> profileChanged)
    {
        if (profiles is null)
        {
            throw new ArgumentNullException(nameof(profiles));
        }

        if (hiScoreStore is null)
        {
            throw new ArgumentNullException(nameof(hiScoreStore));
        }

        var map = new CampaignMapController(session, profiles.Profile!, profiles.SecretFiveAvailable, briefing, hiScores);
        hiScoreStore.Track(map);
        profiles.Changed += profileChanged;
        profiles.Track(map);
        return map;
    }

    /// <summary>A frame's typed text into the hi-score entry: backspace erases, Enter (<c>\n</c> or <c>\r</c>) commits
    /// and drops the rest of the text, any other character is typed.</summary>
    public static void TypeName(CampaignMapController map, ReadOnlySpan<char> typed)
    {
        if (map is null)
        {
            throw new ArgumentNullException(nameof(map));
        }

        for (int i = 0; i < typed.Length; i++)
        {
            char character = typed[i];
            if (character == '\b')
            {
                map.EraseEntry();
            }
            else if (character == '\n' || character == '\r') { map.CommitEntry(); return; }
            else
            {
                map.TypeEntry(character);
            }
        }
    }

    /// <summary>Refills <paramref name="slots"/> from the base scenery, keyed by view id: a map piece picks the slot of
    /// its piece number, a marker its own slot.</summary>
    public static void FillPickSlots(IReadOnlyList<FrontEndProp> scenery, Dictionary<int, CampaignSlot> slots)
    {
        if (scenery is null)
        {
            throw new ArgumentNullException(nameof(scenery));
        }

        if (slots is null)
        {
            throw new ArgumentNullException(nameof(slots));
        }

        slots.Clear();
        for (int i = 0; i < scenery.Count; i++)
        {
            FrontEndProp prop = scenery[i];
            if (prop.MapPiece is DK2.Assets.FrontEndMapPiece piece)
            {
                slots[prop.View.Id] = new CampaignSlot((byte)piece);
            }
            else if (prop.MarkerSlot is CampaignSlot slot)
            {
                slots[prop.View.Id] = slot;
            }
        }
    }

    /// <summary>The scenery creatures' turn and the fraction into it after <paramref name="elapsedSeconds"/> of front end,
    /// at DK2's <see cref="SimulationTiming.OriginalTicksPerGameSecond"/>.</summary>
    public static (GameTick Tick, float Fraction) CreatureFrame(double elapsedSeconds)
    {
        double turns = elapsedSeconds * SimulationTiming.OriginalTicksPerGameSecond;
        return (new GameTick((long)Math.Floor(turns)), (float)(turns - Math.Floor(turns)));
    }

    /// <summary>The movie gallery for <paramref name="profile"/>: a movie is available when its file is in the install,
    /// and its title comes from <c>Speech.str</c> (none when the table is missing).</summary>
    public static FrontEndMovieGallery MovieGallery(CampaignProfile profile, string install, Func<uint, string> text)
    {
        string speechPath = Path.Combine(LevelSubtitles.TextFolder(install), "Speech.str");
        Dk2StringTable? movieTitles = File.Exists(speechPath) ? Dk2StringTable.Open(speechPath) : null;
        return new FrontEndMovieGallery(profile, path => File.Exists(Path.Combine(install, path)),
            id => movieTitles != null && movieTitles.TryResolve(id, out string title) ? title : "", text);
    }
}
