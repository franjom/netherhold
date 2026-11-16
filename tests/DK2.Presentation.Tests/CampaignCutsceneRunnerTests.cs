// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Media;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

/// <summary><see cref="CampaignCutsceneRunner"/>, moved out of Unity's DungeonRunner.Cutscene: the title after a pause, its
/// voiced line, a pause once it ends, then each movie in turn.</summary>
public sealed class CampaignCutsceneRunnerTests
{
    private sealed class Host : ICampaignCutsceneHost
    {
        public readonly List<string> Calls = new();
        public bool SpeechBusy { get; set; }
        public bool MoviePlaying { get; set; }
        public void QueueSpeech(SoundId speech) => Calls.Add("speech " + speech.Value);
        public void PlayMovie(string relativePath, string install)
        {
            Calls.Add("movie " + relativePath + " from " + install);
            MoviePlaying = true;
        }
    }

    private static CampaignProfile Won(int slot)
    {
        var statuses = new CampaignLevelStatus[40];
        statuses[slot - 1] = CampaignLevelStatus.Won;
        return new CampaignProfile { LevelNumber = (byte)slot, PlayerLevelStatus = statuses };
    }

    [Fact]
    public void WithoutAWonMapNothingPlays()
    {
        var host = new Host();
        var runner = new CampaignCutsceneRunner(host);
        runner.Begin(new CampaignProfile(), "install", _ => "title", 0f);

        Assert.False(runner.Playing);
        Assert.False(runner.Update(1f));
        Assert.False(runner.Blackout(movieHasFrame: false));
        Assert.Empty(host.Calls);
    }

    [Fact]
    public void TheTitleWaitsAPauseThenItsLineThenAPauseThenTheMovie()
    {
        var host = new Host();
        var runner = new CampaignCutsceneRunner(host);
        uint asked = 0;
        runner.Begin(Won(1), "install", id => { asked = id; return "The Lord"; }, 10f);
        CampaignCutscene cutscene = runner.Cutscene!;
        Assert.Equal(cutscene.TitleTextId, asked);
        Assert.Equal("The Lord", runner.VisibleTitle);
        Assert.True(runner.Blackout(movieHasFrame: false));

        Assert.True(runner.Update(10.2f));
        Assert.Empty(host.Calls);
        Assert.True(runner.Update(10.25f));
        Assert.Equal(new[] { "speech " + (cutscene.TitleTextId + 1) }, host.Calls);

        host.SpeechBusy = true;
        Assert.True(runner.Update(20f));
        host.SpeechBusy = false;
        Assert.True(runner.Update(21f));        // the line ended: the second pause starts
        Assert.True(runner.Update(21.2f));
        Assert.Single(host.Calls);
        Assert.Equal("The Lord", runner.VisibleTitle);

        Assert.True(runner.Update(21.25f));     // the card ends and the movie starts in the same frame
        Assert.Null(runner.VisibleTitle);
        Assert.Equal("movie Data/Movies/CutSceneLevel1.tgq from install", host.Calls[1]);
        Assert.True(runner.Update(30f));
        Assert.False(runner.Blackout(movieHasFrame: true));

        host.MoviePlaying = false;
        Assert.False(runner.Update(31f));
        Assert.False(runner.Playing);
        Assert.False(runner.Update(32f));
    }

    [Fact]
    public void WithoutATitleTheMoviesPlayInTurnAtOnce()
    {
        var host = new Host();
        var runner = new CampaignCutsceneRunner(host);
        runner.Begin(Won(1), "install", _ => null, 0f);
        Assert.Null(runner.VisibleTitle);

        Assert.True(runner.Update(0f));
        Assert.Equal(new[] { "movie Data/Movies/CutSceneLevel1.tgq from install" }, host.Calls);
        Assert.True(runner.Blackout(movieHasFrame: false));
        host.MoviePlaying = false;
        Assert.False(runner.Update(1f));
    }

    [Fact]
    public void TheLastMapPlaysBothOutrosWithoutATitle()
    {
        var host = new Host();
        var runner = new CampaignCutsceneRunner(host);
        bool asked = false;
        runner.Begin(Won(24), "install", _ => { asked = true; return "x"; }, 0f);
        Assert.False(asked);

        Assert.True(runner.Update(0f));
        host.MoviePlaying = false;
        Assert.True(runner.Update(1f));
        host.MoviePlaying = false;
        Assert.False(runner.Update(2f));
        Assert.Equal(new[] { "movie Data/Movies/Outro.tgq from install", "movie Data/Movies/BullfrogOutro.tgq from install" },
            host.Calls);
    }
}
