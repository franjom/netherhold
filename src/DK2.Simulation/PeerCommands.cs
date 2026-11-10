// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
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
    /// <summary>Decodes one command sent by <paramref name="sender"/> in a match on a map of
    /// <paramref name="mapWidth"/> by <paramref name="mapHeight"/> tiles, refusing one issued in another
    /// player's name, one no human may send, and one whose tiles lie off the map. The map bound is
    /// tighter than the codec's <see cref="CommandCodec.MaxTiles"/>: a peer cannot make every machine
    /// walk more tiles than the map has (R1).</summary>
    public static PlayerCommand Read(BinaryReader reader, PlayerId sender, int mapWidth, int mapHeight)
    {
        PlayerCommand command = CommandCodec.Read(reader);
        if (command.PlayerId != sender)
            throw new InvalidDataException(
                $"Player {sender.Value} sent a {command.GetType().Name} for player {command.PlayerId.Value}.");
        if (!IsAllowedFromPeer(command))
            throw new InvalidDataException($"{command.GetType().Name} is not accepted from another player.");
        CheckTiles(command, mapWidth, mapHeight);
        return command;
    }

    private static void CheckTiles(PlayerCommand command, int width, int height)
    {
        IReadOnlyList<TilePos>? tiles = command switch
        {
            TagTilesCommand c => c.Tiles,
            BuildRoomCommand c => c.Tiles,
            SellRoomCommand c => c.Tiles,
            _ => null,
        };
        if (command is SellRoomCommand { Mode: RoomSellMode.PlayerWave } wave
            && !(OnMap(wave.Min, width, height) && OnMap(wave.Max, width, height)))
            throw new InvalidDataException("A sell wave reaches off the map.");
        if (tiles is null) return;
        if (tiles.Count > (long)width * height)
            throw new InvalidDataException($"{tiles.Count} tiles exceed the map's {width}x{height}.");
        foreach (TilePos tile in tiles)
            if (!OnMap(tile, width, height))
                throw new InvalidDataException($"Tile {tile.X},{tile.Y} is off the {width}x{height} map.");
    }

    private static bool OnMap(TilePos tile, int width, int height) =>
        (uint)tile.X < (uint)width && (uint)tile.Y < (uint)height;

    /// <summary>
    /// The network allow-list (R4). A command type added later is refused until it is listed here;
    /// <c>PeerCommandsTests</c> fails until each type is either listed or named as refused.
    /// <para>Refused: <see cref="GrantCreatureLevelCommand"/> and <see cref="MintWageCommand"/> are the
    /// computer keeper's (no human action issues them); <see cref="RevealMapCommand"/> is the result
    /// screen's and a cheat's. If a multiplayer result screen needs it, the relay issues it, not a peer.
    /// <see cref="MovePossessedCreatureCommand"/> is refused until the simulation validates a pose
    /// (speed, walls, bounds): <c>PossessionRules.Move</c> installs whatever position it is sent, as
    /// DK2 trusted its own client to.</para>
    /// </summary>
    public static bool IsAllowedFromPeer(PlayerCommand command) => command is
        TagTilesCommand or BuildRoomCommand or SellRoomCommand or GuiButtonCommand or CreateImpCommand
        or PickUpCreatureCommand or PickUpObjectCommand or PickUpStoreGoldCommand or DropCommand
        or SlapCreatureCommand or CastSpellAtPlaceCommand or CastSpellOnCreatureCommand
        or RemoveCallToArmsCommand or PlaceDoorCommand or PlaceTrapCommand or LockDoorCommand
        or PullCasinoLeverCommand or SetAllianceCommand or SkipCameraPathCommand
        or AcknowledgePlayerMessageCommand or LeavePossessionCommand or ActPossessedCreatureCommand
        or DismissFollowersCommand or ToggleSneakCommand or HypnotiseCommand or SetSniperModeCommand
        or SetRescuePriorityCommand or ContinueAfterResultCommand
        or ActivateSpecialCommand or WithdrawCounterGoldCommand;
}
