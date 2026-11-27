// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Presentation;

/// <summary>One frame's inputs to <see cref="CampaignResultFlow.Update"/>, read by the adapter: the viewer, whether the
/// scripted camera path plays, the wall clock in milliseconds, the speech state the win screen waits on (null with
/// speech audio off or no player) and the Space and Escape presses of this frame.</summary>
public readonly record struct CampaignResultFrame(
    PlayerId Viewer, bool PathPlaying, double NowMilliseconds, bool? SpeechPlaying, bool SpacePressed, bool EscapePressed);

/// <summary>A campaign level started from the front end: its verdict and return to the front end. Each frame:
/// the carry, the statistics once per player in verdict order, the HUD's result state, the result once, nothing while a
/// camera path plays, the win keys, then Space leaves. The adapter switches to the front end when
/// <see cref="Update"/> returns true.</summary>
public sealed class CampaignResultFlow
{
    private readonly SessionContext _context;
    private readonly CampaignProfileStore _profiles;
    private readonly Action _loadProfile;
    private readonly ClassicHudControls _hud;
    private readonly bool[] _statisticsRecorded = new bool[8];
    /// <summary>The player indices sampled this frame, reused so a frame allocates nothing.</summary>
    private readonly List<int> _sampled = new(8);

    /// <param name="loadProfile">Loads the live profile into <paramref name="profiles"/> as the adapter does it.</param>
    public CampaignResultFlow(SessionContext context, CampaignProfileStore profiles, Action loadProfile, ClassicHudControls hud)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _loadProfile = loadProfile ?? throw new ArgumentNullException(nameof(loadProfile));
        _hud = hud ?? throw new ArgumentNullException(nameof(hud));
    }

    /// <summary>The session's <see cref="SessionContext.LaunchedFromFrontEnd"/>: only such a level has a result here.</summary>
    public bool LaunchedFromFrontEnd
    {
        get
        {
            return _context.LaunchedFromFrontEnd;
        }
    }

    /// <summary>The level's ticks per game second, which the statistics' times are read in.</summary>
    public int LevelTicksPerSecond { get; set; }

    /// <summary>The win screen whose continue the win keys wait on; null for none.</summary>
    public WinScreen? WinScreen { get; set; }

    /// <summary>The verdict has been seen (and, with a played slot, written to the profile).</summary>
    public bool ResultRecorded { get; private set; }

    /// <summary>Escape asked to carry on playing and the win screen has not finished.</summary>
    public bool ContinueRequested { get; private set; }

    /// <summary>At level start, before the first turn: the profile and the played slot; a restored save counts its carry
    /// as written, and a fresh start brings the stored transfer creature in and clears it from the profile.</summary>
    public void Prepare(GameSimulation simulation, PlayerId viewer, bool resumed)
    {
        if (simulation is null)
        {
            throw new ArgumentNullException(nameof(simulation));
        }

        if (!_context.LaunchedFromFrontEnd)
        {
            return;
        }

        _loadProfile();
        _context.PlaySlotOf(_profiles.Profile!);
        if (resumed) { _context.CarryApplied = simulation.World.Carry; return; }
        if (_profiles.Profile!.TransferCreatureId == 0)
        {
            return;
        }
        // 0x0050C67D: the stored creature arrives before the first turn; both values clear whatever happens.
        TransferCreatureRules.Arrive(simulation, viewer, _profiles.Profile.TransferCreatureId, _profiles.Profile.TransferCreatureLevel);
        _profiles.Profile = _profiles.Profile with { TransferCreatureId = 0, TransferCreatureLevel = 0 };
        _profiles.Save();
    }

    /// <summary>0x004BE630's local-player arms when the verdict lands. Space then leaves for the front end. The old
    /// runner's comment said Space posts action 1, whose world flag (+0x723B, 0x00510E90) ends the session; the code
    /// switches the session directly, and this keeps the code's behaviour.</summary>
    /// <returns>True when the adapter must switch to the front end now.</returns>
    public bool Update(WorldState world, GameDatabase database, in CampaignResultFrame frame, List<PlayerCommand> pending)
    {
        if (world is null)
        {
            throw new ArgumentNullException(nameof(world));
        }

        if (!_context.LaunchedFromFrontEnd)
        {
            return false;
        }

        if (_context.PlayedSlot != CampaignSlot.None)
        {
            _context.ApplyCarry(_profiles, world.Carry);
        }

        RecordLevelStatistics(world, database);
        byte status = world.Player(frame.Viewer).Status;
        bool ended = PlayerStatus.HasEnded(status);
        _hud.UpdateResult(new HudResultState(HasResult: ended), frame.PathPlaying ? CameraModeId.ScriptedPath : default);
        if (!ended)
        {
            return false;
        }

        if (!ResultRecorded && _context.PlayedSlot != CampaignSlot.None)
        {
            _profiles.Profile = CampaignProgression.RecordResult(_profiles.Profile!, _context.PlayedSlot, won: status == PlayerStatus.Won);
            _profiles.Save();
        }
        ResultRecorded = true;
        if (frame.PathPlaying)
        {
            return false;
        }

        if (status == PlayerStatus.Won && UpdateWinKeys(frame, pending))
        {
            return false;
        }

        return !ContinueRequested && frame.SpacePressed;
    }

    /// <summary>The remake's placeholder line under the result (DK2's in-level result presentation is unread, front-end
    /// plan F5), or null while none shows; the adapter draws the win screen with it.</summary>
    public string? Prompt(WorldState world, PlayerId viewer, bool pathPlaying)
    {
        if (!_context.LaunchedFromFrontEnd || !ResultRecorded || pathPlaying)
        {
            return null;
        }

        byte status = (world ?? throw new ArgumentNullException(nameof(world))).Player(viewer).Status;
        if (!PlayerStatus.HasEnded(status))
        {
            return null;
        }

        return status != PlayerStatus.Won || _profiles.Profile!.SecretLevelNumber != 0 ? "Press Space to leave"
            : ContinueRequested ? "Continuing..." : "Press Space to leave, Escape to keep playing";
    }

    /// <summary>0x004BE630's shared tail for each player as its verdict is sampled, in verdict order (the turn it
    /// ended, then the player id).</summary>
    private void RecordLevelStatistics(WorldState world, GameDatabase database)
    {
        if (_context.PlayedSlot == CampaignSlot.None)
        {
            return;
        }

        _sampled.Clear();
        for (int i = 0; i < world.PlayerCount; i++)
        {
            ref PlayerState candidate = ref world.PlayerAtIndex(i);
            if (!candidate.Statistics.Sampled || candidate.PlayerId.Value >= _statisticsRecorded.Length
                || _statisticsRecorded[candidate.PlayerId.Value])
            {
                continue;
            }
            // A stable insertion by (StatusTick, PlayerId), as the old OrderBy/ThenBy sorted them.
            int at = _sampled.Count;
            while (at > 0 && Before(ref candidate, ref world.PlayerAtIndex(_sampled[at - 1])))
            {
                at--;
            }

            _sampled.Insert(at, i);
        }
        if (_sampled.Count == 0)
        {
            return;
        }

        foreach (int index in _sampled)
        {
            ref PlayerState player = ref world.PlayerAtIndex(index);
            _statisticsRecorded[player.PlayerId.Value] = true;
            LevelStatisticsBlock block = LevelStatisticsRules.Block(player, LevelTicksPerSecond);
            uint? rank = player.Status is PlayerStatus.Won or PlayerStatus.Lost
                ? KeeperRank.Compute(block, LevelStatisticsRules.NativeEndTurn(player.Statistics), LevelTicksPerSecond,
                    database[VariableTypeId.AverageTime], database[VariableTypeId.LevelRating])
                : (uint?)null;
            _profiles.Profile = CampaignProgression.RecordStatistics(_profiles.Profile!, player.PlayerId, block, _context.PlayedSlot, rank);
        }
        _profiles.Save();
    }

    private static bool Before(ref PlayerState a, ref PlayerState b)
    {
        return a.StatusTick.Value < b.StatusTick.Value || a.StatusTick.Value == b.StatusTick.Value && a.PlayerId.Value < b.PlayerId.Value;
    }

    /// <summary>The won campaign result's keys (<c>0x004054F0</c>) and the win screen's update (<c>0x0042C390</c>).
    /// Space leaves unless a continue is pending; Escape continues at once when the screen is done (or there is none),
    /// else reveals the map and waits for it; on a secret level Escape does nothing. Returns true when the frame's keys
    /// were taken.</summary>
    private bool UpdateWinKeys(in CampaignResultFrame frame, List<PlayerCommand> pending)
    {
        PlayerId player = frame.Viewer;
        bool secret = _profiles.Profile!.SecretLevelNumber != 0;
        if (WinScreen != null && WinScreen.Update(frame.NowMilliseconds, frame.PathPlaying, ContinueRequested, frame.SpeechPlaying))
        {
            Continue(player, pending);
            return true;
        }
        if (secret || ContinueRequested || !frame.EscapePressed)
        {
            return false;
        }

        if (WinScreen == null || WinScreen.Started && WinScreen.Done)
        {
            // The done arm (0x004055E3): action 134 at 0x00405664, +0x13DA at 0x00405668, then action 75 at 0x00405697.
            Continue(player, pending);
            pending.Add(new RevealMapCommand(player));
        }
        else
        {
            // The waiting arm: action 75 only, and the continue waits for the screen (+0x109E bit 0x100000).
            pending.Add(new RevealMapCommand(player));
            ContinueRequested = true;
        }
        return true;
    }

    private void Continue(PlayerId player, List<PlayerCommand> pending)
    {
        pending.Add(new ContinueAfterResultCommand(player));
        WinScreen?.Close();
        ContinueRequested = false;
    }
}
