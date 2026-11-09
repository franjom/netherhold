// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation;

/// <summary>
/// Decodes a command that arrived from another player (ADR-0724). On top of the codec's bounded
/// decode (R1) it enforces two rules the journal readers do not need:
/// <list type="bullet">
/// <item><b>R2:</b> the command's player must be the sender the transport authenticated, so a peer
/// cannot act for another keeper.</item>
/// <item><b>R4:</b> the command must be one a human can issue (<see cref="IsAllowedFromPeer"/>).</item>
/// </list>
/// Every refusal throws <see cref="InvalidDataException"/>; a cut-short packet still throws
/// <see cref="EndOfStreamException"/>. The session treats both as one rejection of the packet.
/// </summary>
public static class PeerCommands
{
    /// <summary>Decodes one command sent by <paramref name="sender"/>, refusing one issued in another
    /// player's name or one no human may send.</summary>
    public static PlayerCommand Read(BinaryReader reader, PlayerId sender)
    {
        PlayerCommand command = CommandCodec.Read(reader);
        if (command.PlayerId != sender)
            throw new InvalidDataException(
                $"Player {sender.Value} sent a {command.GetType().Name} for player {command.PlayerId.Value}.");
        if (!IsAllowedFromPeer(command))
            throw new InvalidDataException($"{command.GetType().Name} is not accepted from another player.");
        return command;
    }

    /// <summary>
    /// The network allow-list (R4). A command type added later is refused until it is listed here;
    /// <c>PeerCommandsTests</c> fails until each type is either listed or named as refused.
    /// <para>Refused: <see cref="GrantCreatureLevelCommand"/> and <see cref="MintWageCommand"/> are the
    /// computer keeper's (no human action issues them); <see cref="RevealMapCommand"/> is the result
    /// screen's and a cheat's. If a multiplayer result screen needs it, the relay issues it, not a peer.</para>
    /// </summary>
    public static bool IsAllowedFromPeer(PlayerCommand command) => command is
        TagTilesCommand or BuildRoomCommand or SellRoomCommand or GuiButtonCommand or CreateImpCommand
        or PickUpCreatureCommand or PickUpObjectCommand or PickUpStoreGoldCommand or DropCommand
        or SlapCreatureCommand or CastSpellAtPlaceCommand or CastSpellOnCreatureCommand
        or RemoveCallToArmsCommand or PlaceDoorCommand or PlaceTrapCommand or LockDoorCommand
        or PullCasinoLeverCommand or SetAllianceCommand or SkipCameraPathCommand
        or AcknowledgePlayerMessageCommand or LeavePossessionCommand or ActPossessedCreatureCommand
        or DismissFollowersCommand or ToggleSneakCommand or HypnotiseCommand or SetSniperModeCommand
        or MovePossessedCreatureCommand or SetRescuePriorityCommand or ContinueAfterResultCommand
        or ActivateSpecialCommand or WithdrawCounterGoldCommand;
}
