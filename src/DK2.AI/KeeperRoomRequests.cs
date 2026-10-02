// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.AI;

/// <summary>The room messages' arms of the keeper's message walk (<c>0x004FA400</c>): 41 asks for a
/// room, 42 first removes every gold-dig tag and then asks for more space by
/// <c>BuildNewRoomWhenMoreSpaceRequired</c>. A request becomes <c>NextRoomToBuild</c>, which the room
/// planner takes on its next pass (docs/KEEPER_AI.md §5.3, computer-keeper-messages.md §3).</summary>
/// <remarks>Each arm returns whether DK2 marks the message handled. An unavailable room is handled
/// and dropped; a request already waiting leaves the message unhandled, so the walk retries it on a
/// later tick while the queue retains it.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/computer-keeper-messages.md")]
public sealed class KeeperRoomRequests
{
    /// <summary><c>NextRoomToBuild</c>'s bit 7: enlarge an existing room of the type.</summary>
    public const byte ExpandBit = 0x80;

    private readonly KeeperView _view;
    private readonly KeeperAIState _state;
    private readonly KeeperAIParameters _p;
    private readonly Func<byte, bool> _isAvailable;
    private readonly List<PlayerCommand> _commands;
    private bool _goldUntagged;

    /// <param name="goldUntagged">Whether this tick already removed every gold-dig tag, so another
    /// removal would find no task left to untag.</param>
    public KeeperRoomRequests(KeeperView view, KeeperAIState state, KeeperAIParameters parameters,
        Func<byte, bool> isAvailable, List<PlayerCommand> commands, bool goldUntagged = false)
    {
        _view = view;
        _state = state;
        _p = parameters;
        _isAvailable = isAvailable;
        _commands = commands;
        _goldUntagged = goldUntagged;
    }

    /// <summary>Message 41: the room in Data1, as asked.</summary>
    public bool RoomNeeded(PlayerMessage message)
    {
        ReleasePortalPlaceholder();
        return Request((byte)message.Data1);
    }

    /// <summary>Message 42. The gold untag runs before the room is looked at, for every room type. Its
    /// failure ends a Treasury's request, still handled; any other room goes on to the policy: 0 asks
    /// to expand, 2 drops the message, 1 and 3 ask for a new room.</summary>
    /// <param name="untagRefused">What <c>0x004FA7C0</c> reports: an untag the action table refused on
    /// Impenetrable terrain. Action 41's handler <c>0x005129E0</c> always returns 1, so in DK2 1.70
    /// this is false; the parameter lets the branch be tested.</param>
    public bool RoomFull(PlayerMessage message, bool untagRefused = false)
    {
        ReleasePortalPlaceholder();
        byte request = (byte)message.Data1;
        UntagGoldDigs();
        if (untagRefused && request == (byte)RoomTypeId.Treasury) return true;
        switch (_p.BuildNewRoomWhenMoreSpaceRequired & 3)
        {
            case 0: request |= ExpandBit; break;
            case 2: return true;
        }
        return Request(request);
    }

    /// <summary>A waiting Portal request is the planner's all-built placeholder: a room message
    /// replaces it rather than waiting behind it.</summary>
    private void ReleasePortalPlaceholder()
    {
        if (_state.NextRoomToBuildSet && _state.NextRoomToBuild == (byte)RoomTypeId.Portal)
            _state.NextRoomToBuildSet = false;
    }

    /// <summary>Availability 3 of the room without bit 7, then a free request slot. The room being
    /// built now is not asked for again.</summary>
    private bool Request(byte request)
    {
        if (!_isAvailable((byte)(request & ~ExpandBit))) return true;
        if (_state.NextRoomToBuildSet) return false;
        if (((_state.CurrentRoomToBuild ^ request) & ~ExpandBit & 0xFF) != 0)
        {
            _state.NextRoomToBuild = request;
            _state.NextRoomToBuildSet = true;
        }
        return true;
    }

    /// <summary><c>0x004FA7C0</c>: action 41 on every task of kind 2, the keeper's gold-dig tags.</summary>
    private void UntagGoldDigs()
    {
        if (_goldUntagged) return;
        _goldUntagged = true;
        var tiles = new List<TilePos>();
        for (int i = 0; i < _view.TileCount; i++)
        {
            TilePos p = _view.FromIndex(i);
            if (_view.IsTaggedByMe(p) && _view.IsGoldRock(p)) tiles.Add(p);
        }
        if (tiles.Count > 0) _commands.Add(new TagTilesCommand(_view.PlayerId, tiles, false));
    }
}
