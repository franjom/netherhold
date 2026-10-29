// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation;

public sealed record MovieGalleryEntry(string Path, string Thumbnail, string Title, bool Available);

/// <summary>Page5's five visible rows over the native22-row table; this host currently uses English.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/front-end-menus.md#movie-gallery")]
public sealed class FrontEndMovieGallery
{
    private readonly Func<string, bool> _exists;
    private readonly Func<uint, string>? _speech;
    private readonly Func<uint, string>? _text;
    public FrontEndMovieGallery(CampaignProfile profile, Func<string, bool> exists,
        Func<uint, string>? speech = null, Func<uint, string>? text = null)
    {
        _exists = exists; _speech = speech; _text = text;
        Refresh(profile);
    }
    public void Refresh(CampaignProfile profile)
    {
        var rows = new MovieGalleryEntry[22];
        for (int movie = 0; movie < rows.Length; movie++)
        {
            bool unlocked = movie == 0;
            for (byte slot = 1; slot <= 24; slot++)
                if (profile.PlayerLevelStatus[slot - 1] == CampaignLevelStatus.Won
                    && CampaignCutscene.LevelNumber(new CampaignSlot(slot)) == movie) unlocked = true;
            string path = movie == 0 ? "Data/Movies/Intro.tgq" : movie == 20 ? "Data/Movies/Outro.tgq"
                : $"Data/Movies/CutSceneLevel{movie}.tgq";
            bool available = unlocked && _exists(path);
            string thumbnail = !available ? "Mov_Shots/M-Unavailable-0" : movie == 0 ? "Mov_Shots/M-Intro-0"
                : movie == 20 ? "Mov_Shots/M-Outro-0" : $"Mov_Shots/M-{movie - 1:000}-0";
            string title = movie == 0 ? _text?.Invoke(77) ?? "" : movie < 21
                ? _speech?.Invoke(CampaignCutscene.TitleAt(movie)) ?? "" : "";
            rows[movie] = new(path, thumbnail, title, available);
        }
        Entries = rows;
    }
    public IReadOnlyList<MovieGalleryEntry> Entries { get; private set; } = Array.Empty<MovieGalleryEntry>();
    public int FirstRow { get; private set; }
    public IReadOnlyList<MovieGalleryEntry> Visible => Entries.Skip(FirstRow).Take(5).ToArray();
    public void ScrollTo(int first) => FirstRow = Math.Clamp(first, 0, Entries.Count - 5);
}
