// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Tools;

/// <summary>
/// The replay's <c>--blueprints retry</c> (ADR-0570). A recorded door or trap placement was made on a
/// tile DK2's keeper had claimed (STRONGLY INFERRED: DK2's tile tests refuse any other). Ours may not
/// have claimed it yet, and the same tests (<c>0x00450DB0</c> door, <c>0x00450EB0</c> trap;
/// <c>BlueprintPlacementRules</c>) then refuse it, leaving the dungeon without a door or trap the
/// player built for the rest of the run, which no player does. The placement is held and sent on
/// the first later turn ours owns the tile. Every other refusal (solid, a room, occupied, walls,
/// gold) is the simulation's, as recorded. "Sent" is not "placed": a sent placement can still be
/// refused by those.
/// </summary>
internal sealed class BlueprintRetry(Func<PlayerCommand, bool> ownsTile, TextWriter? events = null)
{
    private readonly List<(long Turn, PlayerCommand Command)> pending = [];

    /// <summary>Placements held for a tile ours did not own when they were recorded.</summary>
    public int Held { get; private set; }

    /// <summary>Held placements sent once ours owned the tile.</summary>
    public int SentLater { get; private set; }

    /// <summary>Held placements still waiting.</summary>
    public int Pending => pending.Count;

    /// <summary>The frame to send on <paramref name="turn"/>: <paramref name="frame"/> without the
    /// placements on a tile ours does not own, then the held ones whose tile ours now owns, in the
    /// order they were held. The frame comes back unchanged when nothing is held or sent.</summary>
    public InputFrame Filter(long turn, InputFrame frame)
    {
        List<PlayerCommand>? kept = null;
        IReadOnlyList<PlayerCommand> all = frame.All;
        for (int i = 0; i < all.Count; i++)
        {
            PlayerCommand command = all[i];
            if (command is PlaceDoorCommand or PlaceTrapCommand && !ownsTile(command))
            {
                kept ??= [.. all.Take(i)];
                pending.Add((turn, command));
                Held++;
                events?.WriteLine($"{turn}\treplay\tblueprint_held\t{Describe(command)}");
                continue;
            }
            kept?.Add(command);
        }
        for (int i = 0; i < pending.Count; i++)
        {
            (long heldAt, PlayerCommand command) = pending[i];
            if (!ownsTile(command)) continue;
            (kept ??= [.. all]).Add(command);
            pending.RemoveAt(i--);
            SentLater++;
            events?.WriteLine($"{turn}\treplay\tblueprint_sent_later\t{Describe(command)} recorded={heldAt}");
        }
        return kept is null ? frame : kept.Count == 0 ? InputFrame.Empty : new InputFrame(kept);
    }

    private static string Describe(PlayerCommand command) => command switch
    {
        PlaceDoorCommand d => $"player={d.PlayerId} kind=door type={d.DoorId} at=({d.Tile.X},{d.Tile.Y})",
        PlaceTrapCommand t => $"player={t.PlayerId} kind=trap type={t.TrapId} at=({t.Tile.X},{t.Tile.Y})",
        _ => command.ToString() ?? string.Empty,
    };
}
