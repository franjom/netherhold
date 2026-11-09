// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Tests;

/// <summary>ADR-0724 R2 and R4: a command from another player is accepted only in the sender's own
/// name and only when a human may issue it.</summary>
public class PeerCommandsTests
{
    /// <summary>Command types a peer may never send, with the reason. Every other type is allowed.</summary>
    private static readonly HashSet<Type> Refused =
    [
        typeof(GrantCreatureLevelCommand), // computer keeper only
        typeof(MintWageCommand), // computer keeper only
        typeof(RevealMapCommand), // result screen and cheat
    ];

    [Fact]
    public void EveryCommandType_IsEitherAllowedOrNamedAsRefused()
    {
        // CommandCodecTests.EveryCommandType_HasASample keeps the samples complete, so a new
        // command type lands here and needs a decision.
        foreach (PlayerCommand command in CommandCodecTests.Samples())
            Assert.True(PeerCommands.IsAllowedFromPeer(command) != Refused.Contains(command.GetType()),
                $"{command.GetType().Name}: allowed and refused lists disagree");
    }

    [Fact]
    public void AnAllowedCommand_FromItsOwnPlayer_DecodesUnchanged()
    {
        foreach (PlayerCommand command in CommandCodecTests.Samples().Where(PeerCommands.IsAllowedFromPeer))
            Assert.Equal(Encode(command), Encode(Decode(command, command.PlayerId)));
    }

    [Fact]
    public void ACommand_InAnotherPlayersName_IsRefused()
    {
        foreach (PlayerCommand command in CommandCodecTests.Samples().Where(PeerCommands.IsAllowedFromPeer))
        {
            var other = new PlayerId((byte)(command.PlayerId.Value + 1));
            Assert.Throws<InvalidDataException>(() => Decode(command, other));
        }
    }

    [Fact]
    public void ARefusedCommand_IsRefusedEvenFromItsOwnPlayer()
    {
        foreach (PlayerCommand command in CommandCodecTests.Samples().Where(c => Refused.Contains(c.GetType())))
            Assert.Throws<InvalidDataException>(() => Decode(command, command.PlayerId));
    }

    private static PlayerCommand Decode(PlayerCommand command, PlayerId sender) =>
        PeerCommands.Read(new BinaryReader(new MemoryStream(Encode(command))), sender);

    private static byte[] Encode(PlayerCommand command)
    {
        var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream)) CommandCodec.Write(w, command);
        return stream.ToArray();
    }
}
