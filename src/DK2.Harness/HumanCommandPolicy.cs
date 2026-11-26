// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Harness;

/// <summary>Why a command type is or is not issued by the fuzzer (ADR-0552 §4).</summary>
public enum HumanCommandClass
{
    /// <summary>A human issues it in ordinary play, through a path named in <see cref="HumanCommandPolicy.Sources"/>.</summary>
    Play,

    /// <summary>A human issues it only outside play: an intro camera path or the result screen.</summary>
    OutsidePlay,

    /// <summary>DK2's human can do it, but no input path of ours issues it yet (the possessed
    /// alliances, the casino lever, message acknowledgement and rescue priority).</summary>
    NoHumanPathYet,

    /// <summary>Only the computer keeper issues it; no human action corresponds.</summary>
    ComputerKeeperOnly,
}

/// <summary>
/// The explicit, versioned list of which <see cref="PlayerCommand"/> types a human player can issue,
/// derived from the input paths that build them. <c>HumanCommandPolicyTests</c> fails when a
/// command type is unclassified, or when any file under <see cref="SourceRoots"/> names a command
/// that is not <see cref="HumanCommandClass.Play"/> or <see cref="HumanCommandClass.OutsidePlay"/>.
/// The Godot files cannot be referenced from <c>src/</c>, so they are read as source text.
/// </summary>
public static class HumanCommandPolicy
{
    /// <summary>Bumped whenever a classification changes; the fuzzer's name carries it.</summary>
    public const int Version = 4;

    /// <summary>Where human input becomes commands, relative to the repository root; every
    /// <c>.cs</c> file below them is scanned, including the shared input rules and adapter input handlers.</summary>
    public static readonly IReadOnlyList<string> SourceRoots = ["src/DK2.Presentation", "godot/DK2.Godot"];

    public static readonly IReadOnlyDictionary<Type, HumanCommandClass> Classes = new Dictionary<Type, HumanCommandClass>
    {
        // PlayerInputTranslator: drags (tag, untag, build, sell), clicks (hand, slap, create imp), drops.
        [typeof(TagTilesCommand)] = HumanCommandClass.Play,
        [typeof(BuildRoomCommand)] = HumanCommandClass.Play,
        [typeof(SellRoomCommand)] = HumanCommandClass.Play,
        [typeof(CreateImpCommand)] = HumanCommandClass.Play,
        [typeof(PickUpCreatureCommand)] = HumanCommandClass.Play,
        [typeof(PickUpObjectCommand)] = HumanCommandClass.Play,
        [typeof(PickUpStoreGoldCommand)] = HumanCommandClass.Play,
        [typeof(DropCommand)] = HumanCommandClass.Play,
        [typeof(SlapCreatureCommand)] = HumanCommandClass.Play,
        [typeof(ActivateSpecialCommand)] = HumanCommandClass.Play,
        // PlacementCursor and the HUD: spells, doors, traps; the resource counter's withdrawal.
        [typeof(CastSpellAtPlaceCommand)] = HumanCommandClass.Play,
        [typeof(CastSpellOnCreatureCommand)] = HumanCommandClass.Play,
        [typeof(PlaceDoorCommand)] = HumanCommandClass.Play,
        [typeof(PlaceTrapCommand)] = HumanCommandClass.Play,
        [typeof(WithdrawCounterGoldCommand)] = HumanCommandClass.Play,
        // The bottom panel's clicks: action 0x72, a trigger event (ADR-0670).
        [typeof(GuiButtonCommand)] = HumanCommandClass.Play,
        // The Hand's press on a door, Hand_Click's hover-kind-5 arm (action 93).
        [typeof(LockDoorCommand)] = HumanCommandClass.Play,
        // The Battle tab's Call to Arms cancel, a left click (0x004268A0, action 0x81; version 2).
        [typeof(RemoveCallToArmsCommand)] = HumanCommandClass.Play,
        [typeof(SkipCameraPathCommand)] = HumanCommandClass.OutsidePlay,
        [typeof(RevealMapCommand)] = HumanCommandClass.OutsidePlay,
        [typeof(ContinueAfterResultCommand)] = HumanCommandClass.OutsidePlay,
        [typeof(PullCasinoLeverCommand)] = HumanCommandClass.NoHumanPathYet,
        [typeof(SetAllianceCommand)] = HumanCommandClass.NoHumanPathYet,
        [typeof(AcknowledgePlayerMessageCommand)] = HumanCommandClass.NoHumanPathYet,
        [typeof(SetRescuePriorityCommand)] = HumanCommandClass.NoHumanPathYet,
        // The first-person view, PossessionSession over PossessionControls (version 4), including the readable special abilities.
        [typeof(LeavePossessionCommand)] = HumanCommandClass.Play,
        [typeof(ActPossessedCreatureCommand)] = HumanCommandClass.Play,
        [typeof(DismissFollowersCommand)] = HumanCommandClass.Play,
        [typeof(SetSniperModeCommand)] = HumanCommandClass.Play,
        [typeof(SetCreepingCommand)] = HumanCommandClass.Play,
        [typeof(MovePossessedCreatureCommand)] = HumanCommandClass.Play,
        [typeof(ToggleSneakCommand)] = HumanCommandClass.Play,
        [typeof(TogglePossessionFormCommand)] = HumanCommandClass.Play,
        [typeof(TogglePossessionPrayerCommand)] = HumanCommandClass.Play,
        [typeof(OpenPossessedPrisonCommand)] = HumanCommandClass.Play,
        [typeof(FrightenPossessedCreatureCommand)] = HumanCommandClass.Play,
        [typeof(HypnotiseCommand)] = HumanCommandClass.Play,
        [typeof(GrantCreatureLevelCommand)] = HumanCommandClass.ComputerKeeperOnly,
        [typeof(MintWageCommand)] = HumanCommandClass.ComputerKeeperOnly,
        [typeof(TakeCreatureCommand)] = HumanCommandClass.ComputerKeeperOnly,
    };

    /// <summary>True when a human could issue this command in ordinary play: its type is
    /// <see cref="HumanCommandClass.Play"/> and it takes the form the human path builds (a room
    /// is built from a dragged rectangle, never placed tile by tile).</summary>
    public static bool AllowsInPlay(PlayerCommand command) =>
        Classes.TryGetValue(command.GetType(), out HumanCommandClass c) && c == HumanCommandClass.Play
        && command is not BuildRoomCommand { Mode: not RoomBuildMode.PlayerWave };
}
