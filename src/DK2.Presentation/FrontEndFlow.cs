// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

public enum FrontEndPage : ushort
{
    None = 0, Main = 1, SinglePlayer = 2, Multiplayer = 3, Options = 4, Movies = 5, Extras = 6,
    Briefing = 7, Load = 8, Lobby = 9, Sessions = 10, JoinSessions = 11, HighScores = 18,
    Graphics = 15, Sound = 16, Controls = 17,
    CampaignResults = 26, MultiplayerResults = 27, PetDungeonResults = 42, SkirmishResults = 45,
    CampaignMap = 37, PetDungeon = 38, AddressSessions = 39, Credits = 40, Dialog = 44, Cursor = 47,
}

public enum FrontEndPath : ushort
{
    Intro = 250, EntryToTable, TableToEntry, TableToBriefing, BriefingToTable, Spare,
    EntryToOptions, OptionsToEntry, StaticOptions, EntryToExtras, ExtrasToEntry, StaticExtras,
    EntryToPetDungeon, PetDungeonToEntry, StaticPetDungeon, ExtrasToCredits, CreditsToExtras,
    EntryToPetTable, PetTableToEntry, PetTableToBriefing, BriefingToPetTable,
    EntryToMultiplayer, MultiplayerToEntry, StaticMultiplayer, StaticEntry,
}

public enum FrontEndState : byte
{
    Idle = 0, Intro = 1, CampaignMap = 2, ResultsFourth = 3, PrepareCampaign = 4,
    MultiplayerReady = 5, LoadReady = 6, SkirmishReady = 7, OptionsReady = 8, HighScoresReady = 9,
    TrailerReady = 10, MoviesReady = 11, MainReady = 12, ExtrasReady = 13, SinglePlayerReady = 14,
    OptionsReturn = 15, PetDungeonReady = 16, CreditsReady = 17, CampaignMapEnter = 18,
    CampaignMapAlternate = 19, PreparePetDungeon = 20, ResultsSecond = 22, ResultsThird = 23,
    ExtrasReturn = 25, SkirmishEnter = 26, ResultsFirst = 27, ReturnMain = 28, ReturnSinglePlayer = 29,
}

public enum FrontEndOperation
{
    None, CampaignMapFrame, EnterCampaignMap, PrepareCampaign, PreparePetDungeon,
    InitializeMultiplayer, InitializeSkirmish, InitializePetDungeon, InitializeMovies, InitializeHighScores,
    PlayTrailer, ResultsFirst, ResultsSecond, ResultsThird, ResultsFourth,
}

public readonly record struct FrontEndTransition(FrontEndPath Path, FrontEndState AfterPath);
public readonly record struct FrontEndStep(FrontEndPath? StartPath = null, FrontEndPage? ShowPage = null,
    FrontEndOperation Operation = FrontEndOperation.None);

/// <summary>The station machine of 0x005384F0. Widgets and level/profile operations are consumers.</summary>
public sealed class FrontEndFlow(FrontEndPage returnPage = FrontEndPage.None)
{
    private bool _routeReturn = true;
    public FrontEndState State { get; private set; } = FrontEndState.Intro;

    public FrontEndStep BeginTransition(FrontEndTransition transition)
    {
        if ((ushort)transition.Path is < 250 or > 274 || !Enum.IsDefined(typeof(FrontEndState), transition.AfterPath))
            throw new ArgumentOutOfRangeException(nameof(transition));
        State = transition.AfterPath;
        return new FrontEndStep(StartPath: transition.Path);
    }

    public FrontEndStep Update(bool cameraPathPlaying)
    {
        if (_routeReturn)
        {
            _routeReturn = false;
            FrontEndTransition? initial = returnPage switch
            {
                FrontEndPage.CampaignMap => new(FrontEndPath.EntryToTable, FrontEndState.CampaignMap),
                FrontEndPage.Lobby => new(FrontEndPath.EntryToExtras, FrontEndState.SkirmishReady),
                FrontEndPage.Sessions or FrontEndPage.JoinSessions or FrontEndPage.AddressSessions
                    => new(FrontEndPath.EntryToOptions, FrontEndState.MultiplayerReady),
                FrontEndPage.PetDungeon => new(FrontEndPath.StaticPetDungeon, FrontEndState.PetDungeonReady),
                _ => null,
            };
            FrontEndState? results = returnPage switch
            {
                FrontEndPage.CampaignResults => FrontEndState.ResultsFourth,
                FrontEndPage.MultiplayerResults => FrontEndState.ResultsSecond,
                FrontEndPage.SkirmishResults => FrontEndState.ResultsThird,
                FrontEndPage.PetDungeonResults => FrontEndState.ResultsFirst,
                _ => null,
            };
            if (results.HasValue)
            {
                // 0x00534F70 shows the statistics page at once; the station machine takes the state next frame.
                State = results.Value;
                return new(ShowPage: returnPage);
            }
            if (initial.HasValue)
            {
                FrontEndStep routed = BeginTransition(initial.Value);
                return returnPage == FrontEndPage.CampaignMap
                    ? routed with { Operation = FrontEndOperation.EnterCampaignMap }
                    : routed;
            }
        }
        if (cameraPathPlaying) return default;
        FrontEndStep step;
        switch (State)
        {
            case FrontEndState.Intro: return BeginTransition(new(FrontEndPath.Intro, FrontEndState.MainReady));
            case FrontEndState.ReturnMain: return BeginTransition(new(FrontEndPath.StaticEntry, FrontEndState.MainReady));
            case FrontEndState.ReturnSinglePlayer: return BeginTransition(new(FrontEndPath.StaticEntry, FrontEndState.SinglePlayerReady));
            case FrontEndState.SkirmishEnter: State = FrontEndState.SkirmishReady; return default;
            case FrontEndState.CampaignMap:
            case FrontEndState.CampaignMapAlternate: return new(Operation: FrontEndOperation.CampaignMapFrame);
            case FrontEndState.CampaignMapEnter:
                State = FrontEndState.CampaignMap;
                return new(Operation: FrontEndOperation.EnterCampaignMap);
            // 0x0053C3C0: a static path (264 for My Pet Dungeon, else 255) leaves state 0; the page stays up.
            case FrontEndState.ResultsFirst: return Results(FrontEndPath.StaticPetDungeon, FrontEndPage.PetDungeonResults, FrontEndOperation.ResultsFirst);
            case FrontEndState.ResultsSecond: return Results(FrontEndPath.Spare, FrontEndPage.MultiplayerResults, FrontEndOperation.ResultsSecond);
            case FrontEndState.ResultsThird: return Results(FrontEndPath.Spare, FrontEndPage.SkirmishResults, FrontEndOperation.ResultsThird);
            case FrontEndState.ResultsFourth: return Results(FrontEndPath.Spare, FrontEndPage.CampaignResults, FrontEndOperation.ResultsFourth);
            case FrontEndState.PrepareCampaign: return new(Operation: FrontEndOperation.PrepareCampaign);
            case FrontEndState.PreparePetDungeon: return new(Operation: FrontEndOperation.PreparePetDungeon);
            case FrontEndState.MainReady: step = new(ShowPage: FrontEndPage.Main); break;
            case FrontEndState.SinglePlayerReady: step = new(ShowPage: FrontEndPage.SinglePlayer); break;
            case FrontEndState.OptionsReady:
            case FrontEndState.OptionsReturn: step = new(ShowPage: FrontEndPage.Options); break;
            case FrontEndState.ExtrasReady:
            case FrontEndState.ExtrasReturn: step = new(ShowPage: FrontEndPage.Extras); break;
            case FrontEndState.LoadReady: step = new(ShowPage: FrontEndPage.Load); break;
            case FrontEndState.MultiplayerReady: step = new(ShowPage: FrontEndPage.Multiplayer, Operation: FrontEndOperation.InitializeMultiplayer); break;
            case FrontEndState.SkirmishReady: step = new(ShowPage: FrontEndPage.Lobby, Operation: FrontEndOperation.InitializeSkirmish); break;
            case FrontEndState.PetDungeonReady: step = new(ShowPage: FrontEndPage.PetDungeon, Operation: FrontEndOperation.InitializePetDungeon); break;
            case FrontEndState.HighScoresReady: step = new(ShowPage: FrontEndPage.HighScores, Operation: FrontEndOperation.InitializeHighScores); break;
            case FrontEndState.MoviesReady: step = new(ShowPage: FrontEndPage.Movies, Operation: FrontEndOperation.InitializeMovies); break;
            case FrontEndState.TrailerReady: step = new(Operation: FrontEndOperation.PlayTrailer); break;
            case FrontEndState.CreditsReady: step = new(ShowPage: FrontEndPage.Credits); break;
            default: return default;
        }
        State = FrontEndState.Idle;
        return step;
    }

    private FrontEndStep Results(FrontEndPath path, FrontEndPage page, FrontEndOperation operation)
        => BeginTransition(new(path, FrontEndState.Idle)) with { ShowPage = page, Operation = operation };
}
