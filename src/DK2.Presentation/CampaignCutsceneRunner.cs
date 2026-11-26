// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Media;
using DK2.Data.Model;
using DK2.Data.Text;

namespace DK2.Presentation;

/// <summary>What <see cref="CampaignCutsceneRunner"/> asks of the adapter: the speech queue and the movie player.</summary>
public interface ICampaignCutsceneHost
{
    /// <summary>A line is playing or waiting in the speech queue.</summary>
    bool SpeechBusy { get; }

    /// <summary>Queues a Mentor-bank line after clearing the lines waiting.</summary>
    void QueueSpeech(SoundId speech);

    /// <summary>The movie last started has not finished.</summary>
    bool MoviePlaying { get; }

    /// <summary>Starts a movie, a path relative to the install.</summary>
    void PlayMovie(string relativePath, string install);
}

/// <summary>The won-level title card and cutscene before the front end (0x0053B980). The title follows a pause,
/// then its voiced line and another pause, then each movie in turn. The clock is unscaled seconds.</summary>
public sealed class CampaignCutsceneRunner(ICampaignCutsceneHost host)
{
    private readonly ICampaignCutsceneHost _host = host ?? throw new ArgumentNullException(nameof(host));
    private int _titlePhase;
    private string? _install;
    private float _titleEnds;
    private int _movie = -1;

    /// <summary>The cutscene playing, or null.</summary>
    public CampaignCutscene? Cutscene { get; private set; }

    /// <summary>The title card's text, or null when it has none.</summary>
    public string? Title { get; private set; }

    public bool Playing => Cutscene != null;

    /// <summary>The title card's voiced line, the Mentor-bank id after the title's text, or null when the card has no title;
    /// a host may decode it ahead (<see cref="ClassicSoundMixer.PrefetchSpeech"/>) so it starts when it is queued.</summary>
    public SoundId? TitleSpeech => Cutscene != null && Title != null ? new SoundId(Cutscene.TitleTextId + 1) : null;

    /// <summary>The title to draw this frame, or null once the card is over.</summary>
    public string? VisibleTitle => Cutscene != null && _titlePhase < 3 ? Title : null;

    /// <summary>The screen is black: from the card until a movie's first picture is up, and between movies, since
    /// nothing else hides the front end beneath.</summary>
    public bool Blackout(bool movieHasFrame) => Cutscene != null && !movieHasFrame;

    /// <summary>The cutscene after the profile's won map, if any (<see cref="CampaignCutscene.AfterLevel"/>).</summary>
    /// <param name="install">The install the movies are played from.</param>
    /// <param name="title">The title's text by Speech.str id, asked only for a title it has (<see cref="SpeechText"/>).</param>
    public void Begin(CampaignProfile profile, string install, Func<uint, string?> title, float now)
    {
        if (title is null)
        {
            throw new ArgumentNullException(nameof(title));
        }

        Cutscene = CampaignCutscene.AfterLevel(profile);
        if (Cutscene == null)
        {
            return;
        }

        _install = install;
        Title = Cutscene.TitleTextId != 0 ? title(Cutscene.TitleTextId) : null;
        _titlePhase = Title == null ? 3 : 0;
        _titleEnds = now + CampaignCutscene.PauseMilliseconds / 1000f;
        _movie = -1;
    }

    /// <summary>An entry of the install's Speech.str, or null when the table or the entry is missing.</summary>
    public static string? SpeechText(string install, uint id)
    {
        string table = Path.Combine(LevelSubtitles.TextFolder(install), "Speech.str");
        return File.Exists(table) && Dk2StringTable.Open(table).TryResolve(id, out string text) ? text : null;
    }

    /// <summary>Runs the card and then each movie in turn.</summary>
    /// <returns>False once the front end may run: with nothing playing, or on the frame the last movie has ended.</returns>
    public bool Update(float now)
    {
        if (Cutscene == null)
        {
            return false;
        }

        if (_titlePhase < 3)
        {
            if (now < _titleEnds)
            {
                return true;
            }

            if (_titlePhase == 0)
            {
                _host.QueueSpeech(TitleSpeech!.Value);
                _titlePhase = 1;
                return true;
            }
            if (_titlePhase == 1)
            {
                if (_host.SpeechBusy)
                {
                    return true;
                }

                _titlePhase = 2;
                _titleEnds = now + CampaignCutscene.PauseMilliseconds / 1000f;
                return true;
            }
            _titlePhase = 3;
        }
        if (_host.MoviePlaying)
        {
            return true;
        }

        if (++_movie < Cutscene.Movies.Count)
        {
            _host.PlayMovie(Cutscene.Movies[_movie], _install!);
            return true;
        }
        Cutscene = null;
        return false;
    }
}
