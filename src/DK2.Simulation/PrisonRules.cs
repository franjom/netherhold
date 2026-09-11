// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The Prison: capture, withering, skeletons and the jail break — the other
/// branch of DK2's imp task <b>kind 13</b>, where an imp carries its keeper's own
/// unconscious creature to a Lair and somebody else's to a Prison (room 11, both ids
/// from the original's task handler; imp-task-priority.md §4, ADR-0063).
/// <para>Only an imp captures: CONFIRMED (owner, 2026-09-05) — the keeper's hand moves
/// a prisoner already taken, never takes one, which is what
/// <see cref="HandRules.CanPickUp"/> says. DK2's creature dump names <c>PrisonOwner</c>
/// at <c>0x3cb</c> — the keeper whose prison holds the creature, not who owns it —
/// reproduced as <see cref="CreatureState.PrisonOwnerPlayerId"/> (ADR-0046,
/// docs/research/prison.md).</para></summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/prison.md")]
public static class PrisonRules
{
    /// <summary>Nobody's prisoner — the value <see cref="CreatureState.PrisonOwnerPlayerId"/> starts at.</summary>
    public const int NoPrison = -1;

    /// <summary>The chance, <b>per tick</b>, that a prisoner standing still takes
    /// another step — <b>20 in 100</b>. Read from DK2's state 116 (<c>0x74</c>), which
    /// rolls <c>random(100)</c> and enters the decision state 115 under 20. It rolls
    /// only while the prisoner is not already walking, so it is the pause between
    /// steps, about five ticks. A narrower path takes rolls 10..19 when an unread
    /// per-creature flag is set, halving it to 10 in 100 — not modelled, Q188.</summary>
    public const int WanderPercentPerTick = 20;

    /// <summary>The four orthogonal steps <b>in DK2's own table order</b> — north,
    /// east, south, west — as stored at <c>0x006BC350</c>. The order is behaviour, not
    /// cosmetics: the original draws a random starting index and walks the table
    /// forwards, so it decides which way a prisoner goes when blocked.</summary>
    private static readonly (int Dx, int Dy)[] Orthogonal =
        [(0, -1), (1, 0), (0, 1), (-1, 0)];

    /// <summary>How many directions a prisoner considers. DK2 passes 4.</summary>
    public const int WanderDirections = 4;

    /// <summary>Where a prisoner shuffles to next: <b>one tile, orthogonally, and never
    /// out of its own cell</b>. STRONGLY INFERRED from the original's instructions
    /// (<c>Room.cpp:1717</c>, which the decompiler garbles): it draws
    /// <c>random(count)</c> as a starting index, scans <c>(start + i) % count</c>
    /// forwards through the offset table, and <b>the first candidate that passes
    /// wins</b> — non-zero room id, same room instance, one terrain flag clear. The
    /// room-instance test is what keeps a prisoner in its cell, so no separate "do not
    /// leave" rule is needed, and <b>two prisoners may stand on one tile</b> as in the
    /// original, capacity never having been one per tile
    /// (<see cref="Capacity"/>).</summary>
    /// <param name="startDirection">The drawn starting index, <c>0</c>..<c>3</c>.</param>
    public static bool TryWanderStep(
        WorldState world,
        GameDatabase database,
        CreatureState creature,
        int startDirection,
        out TilePos step)
    {
        step = default;

        TilePos here = creature.Tile;
        RoomInstance? cell =
            RoomIndex.RoomAt(world, database, (here.Y * world.Width) + here.X);

        // A prisoner whose cell has stopped being a cell has bigger problems,
        // and PrisonSystem frees it on the same second.
        if (cell is null) return false;

        for (int i = 0; i < Orthogonal.Length; i++)
        {
            (int dx, int dy) = Orthogonal[
                ((startDirection % Orthogonal.Length) + Orthogonal.Length + i) % Orthogonal.Length];

            int x = here.X + dx;
            int y = here.Y + dy;
            if (x < 0 || y < 0 || x >= world.Width || y >= world.Height) continue;

            // Inside the bars, not merely inside the room (ADR-0139).
            if (!cell.IsInterior((y * world.Width) + x)) continue;

            step = new TilePos(x, y);
            return true;
        }

        return false;
    }

    /// <summary>How much anger a prisoner takes each time it searches its cell for
    /// food and finds none: <b><c>AngerNoFood</c> divided by four</b>, floored at 1
    /// for any non-zero file value (the Imp, the Dwarf and Horny carry 1 and keep it).
    /// <para>The divide is the loader's, not the mechanic's: DK2 stores each
    /// <c>angerNo*</c> field as file value over
    /// <see cref="SimulationTiming.OriginalTicksPerGameSecond"/>, so the engine holds anger per DK2 tick and a per-tick adder delivers the tabled figure a second.
    /// This one is not per tick — state 117 adds one unit per failed search, gated to
    /// one a game second — so a hungry prisoner angers at a <b>quarter</b> of a
    /// bedless creature's rate. Shared arithmetic:
    /// <see cref="SimulationTiming.LoaderRatePerTick"/> (ADR-0083). The gate is
    /// <see cref="CreatureAngerRules.CanBeAngered"/> on
    /// <see cref="CreatureFlags2.NeverAngered"/>, where DK2 reads an unmapped flag bit: same intent, both excluding the Skeleton and Jack (Q210).</para></summary>
    public static int AngerPerFailedFoodSearch(CreatureDef creature)
    {
        if (!CreatureAngerRules.CanBeAngered(creature)) return 0;

        return SimulationTiming.LoaderRatePerTick(creature.AngerNoFood);
    }

    /// <summary>How many prisoners a prison holds: <b>the tiles the rim does not
    /// take, plus one</b> — so DK2's smallest working prison, a 3x3, holds <b>two</b>,
    /// and below 3x3 the count is zero, the "+1" never fires, and the room is not a
    /// prison. CONFIRMED off the game's status bar (owner, 2026-09-06): "Captives
    /// held: 0 / 2 … Maximum Skeletons: 2" on a 3x3 (Q166; this used to hold one per tile).
    /// <para>Read, not fitted: DK2's per-room-type capacity routine counts the tiles a
    /// "nothing is standing here" predicate accepts and adds one if the count is
    /// non-zero, into the field its dump calls <c>SafeCreatureCapacity</c>; the
    /// furniture takes the rim. Not a per-tile rule, so <see cref="FreeTileIn"/> giving
    /// each arrival its own tile is about where a body is dropped, not what limits the
    /// room. The rim is counted rather than derived from the bounding box
    /// (<see cref="RoomInstance.InteriorTileCount"/>) so an L-shape answers sensibly; whether DK2 furnishes one the same way is UNKNOWN.</para></summary>
    public static int Capacity(RoomInstance prison) =>
        prison.InteriorTileCount == 0 ? 0 : prison.InteriorTileCount + 1;

    /// <summary>Whether this creature is somebody's prisoner.</summary>
    public static bool IsImprisoned(CreatureState creature) =>
        creature.PrisonOwnerPlayerId != NoPrison;

    /// <summary>Whether <paramref name="room"/> is a prison belonging to <paramref name="playerId"/>.</summary>
    public static bool IsPrisonOf(RoomInstance room, byte playerId) =>
        room.OwnerPlayerId == playerId && room.Definition.TypeId == RoomTypeId.Prison;

    /// <summary>A free tile in <paramref name="playerId"/>'s nearest prison, or -1
    /// when every prison is full and -1 when there is no prison at all. Full means no
    /// delivery, the manual's rule read back ("imps look for the next nearest prison
    /// with space in it"; with none, "imps stop collecting prisoners"), and because
    /// <see cref="CarryRules.TryDestinationFor"/> is asked first an imp never sets off
    /// for a creature it has nowhere to put. Prisons rank by straight-line distance
    /// from <paramref name="from"/> (Q25) and the lowest free tile index wins inside
    /// one — row-major, stable, the tie-break <see cref="LairRules"/> uses.</summary>
    public static int FreeTileIn(
        WorldState world, GameDatabase database, byte playerId, TilePos from)
    {
        int best = -1;
        long bestDistance = long.MaxValue;

        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (!IsPrisonOf(room, playerId)) continue;
            if (PrisonerCount(world, room) >= Capacity(room)) continue;

            foreach (int tileIndex in room.InteriorTileIndices)
            {
                if (PrisonerOn(world, tileIndex) is not null) continue;

                long dx = (tileIndex % world.Width) - from.X;
                long dy = (tileIndex / world.Width) - from.Y;
                long distance = (dx * dx) + (dy * dy);
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                best = tileIndex;
            }
        }

        return best;
    }

    /// <summary>How many prisoners stand in <paramref name="room"/>.</summary>
    public static int PrisonerCount(WorldState world, RoomInstance room)
    {
        int count = 0;
        foreach (CreatureState creature in world.Creatures)
        {
            if (!IsImprisoned(creature)) continue;
            if (creature.PrisonOwnerPlayerId != room.OwnerPlayerId) continue;

            int tileIndex = (creature.Tile.Y * world.Width) + creature.Tile.X;
            if (room.TileIndices.Contains(tileIndex)) count++;
        }
        return count;
    }

    /// <summary>The prisoner standing on one tile, or null.</summary>
    public static CreatureState? PrisonerOn(WorldState world, int tileIndex)
    {
        foreach (CreatureState creature in world.Creatures)
        {
            if (!IsImprisoned(creature)) continue;
            if (HandRules.IsHeld(creature)) continue;
            if ((creature.Tile.Y * world.Width) + creature.Tile.X == tileIndex) return creature;
        }
        return null;
    }

    /// <summary>Puts <paramref name="creature"/> in <paramref name="playerId"/>'s
    /// prison on <paramref name="tileIndex"/>, and says whether it took. A prisoner
    /// arrives unconscious — the only way it can, since only an unconscious creature
    /// is carried — and imprisonment ends the dying window rather than continuing it.
    /// <para>It wakes at <see cref="CreatureHealthRules.KnockoutHealth"/>, <b>a sixth
    /// of maximum</b>, which is what DK2's damage path writes at the knockout (below
    /// zero and not already dying stores <c>max / 6</c> and the <c>CreatureDying</c>
    /// flag) rather than anything the prison does at the door. So a Goblin arrives at
    /// 250 of 1500 and lasts <b>83</b> game seconds. CONFIRMED (owner, 2026-09-05,
    /// Q167): "its flower gets two petals, and then that creature starts loosing
    /// health". Open, display only: a sixth is one petal of six, the owner counts
    /// two (Q167, Q171).</para></summary>
    public static bool Imprison(
        WorldState world, GameDatabase database, CreatureState creature,
        byte playerId, int tileIndex)
    {
        if (tileIndex < 0 || tileIndex >= world.Width * world.Height) return false;

        LairRules.ReleaseBed(world, database, creature);

        creature.PrisonOwnerPlayerId = playerId;
        creature.PosX = CreatureState.CentreOf(tileIndex % world.Width);
        creature.PosY = CreatureState.CentreOf(tileIndex / world.Width);
        creature.MoveRemainder = 0;
        creature.Health = CreatureHealthRules.KnockoutHealth(
            database, database.CreatureById(creature.CreatureTypeId), creature);
        creature.DyingTicks = 0;
        creature.SleepTicks = 0;
        creature.AwakeTicks = 0;
        creature.Activity = CreatureActivity.Imprisoned;
        creature.TargetTileIndex = -1;
        creature.TargetWork = default;
        creature.TimerTicks = 0;
        creature.Path.Clear();
        creature.PathIndex = 0;
        return true;
    }

    /// <summary>Lets a prisoner out — used by the jail break, by losing the prison
    /// under it, and by the keeper dropping one somewhere that is not a prison. The
    /// creature keeps its keeper and everything else, becoming a hostile creature
    /// standing in somebody's dungeon again: the manual's "losing a prison frees every
    /// enemy in it".</summary>
    public static void Release(CreatureState creature)
    {
        creature.PrisonOwnerPlayerId = NoPrison;
        if (creature.Activity == CreatureActivity.Imprisoned) creature.ClearJob();
    }

    /// <summary>The health a prison takes off a prisoner each game second:
    /// <c>PrisonModifyCreatureHealthPerSecond</c>, which ships at <b>-3</b>. The
    /// manual says it in words — "whilst in Prison, creatures gradually lose health as
    /// they wither away".
    /// <para>It is hunger: STRONGLY INFERRED (binary, 2026-09-06, Q173) — the prison
    /// state category's per-tick hook <c>FUN_0047b7c0</c> takes the health off only
    /// when <c>HungerFill</c> is non-zero, so the four creatures that never eat never
    /// wither and a <b>Skeleton</b> in a cell is stable for ever, which matters because
    /// a Skeleton is what a prison makes. Two deviations: the original applies -12
    /// every four game seconds (<c>ticksPerSecond &lt;&lt; 2</c>, same average, coarser
    /// granularity), and it also applies
    /// <c>ModifyAngerInCompanyOfHatedCreaturesPerSecond</c> (150) under anger reason 9 in the same breath, which this does not (prison.md §4b). <b>A campaign map can switch the drain off</b> to hold a creature for scripted release; that flag's home is unknown (Q308).</para></summary>
    public static int WitherPerSecond(
        GameDatabase database, CreatureDef creature) =>
        CreatureHealthRules.CanGetHungry(creature)
            ? database[VariableTypeId.PrisonModifyCreatureHealthPerSecond]
            : 0;

    /// <summary>The chance in percent that an overcrowded prison breaks out this game
    /// second: <c>OvercrowdingJailBreakPercentagePerExtraCreature</c>, which ships at
    /// <b>50</b>, once per prisoner over capacity. The cadence is ours — the variable
    /// says nothing about how often it is rolled, and this rolls it once a game second
    /// (ADR-0017). Q168. A prison only becomes overcrowded because a keeper made it so,
    /// imps refusing to deliver into a full one (the manual's "manual overcrowding"),
    /// or because part of a prison in use was sold under its prisoners.</summary>
    public static int JailBreakPercent(GameDatabase database, int over) =>
        over <= 0 ? 0 : over * database[VariableTypeId.OvercrowdingJailBreakPercentagePerExtraCreature];
}
