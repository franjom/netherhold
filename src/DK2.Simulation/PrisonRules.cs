// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// The Prison: capture, withering, skeletons and the jail break.
///
/// <para>The other branch of DK2's imp task <b>kind 13</b>. An imp that finds an
/// unconscious creature carries its keeper's own to a Lair and somebody else's
/// to a <b>Prison</b> — room 11, the pair of room ids read straight out of the
/// original's task handler (<c>imp-task-priority.md</c> §4). ADR-0063 built the
/// first branch and stopped at the second because there was no prison; this is
/// the second.</para>
///
/// <para><b>Only an imp captures.</b> CONFIRMED by the owner (2026-09-05):
/// <i>"only an imp can drag an enemy creature to prison, a hand of evil can then
/// pick up that creature from prison and drop it around the dungeon, back in
/// prison, or onto a torture rack in the torture chamber"</i>. So the keeper's
/// hand is not a way of taking a prisoner — it is a way of moving one that has
/// already been taken, and <see cref="HandRules.CanPickUp"/> says exactly that.
/// </para>
///
/// <para><b>What DK2 keeps, in its own words.</b> Its room debug dump names the
/// prison's fields: <c>SafeCreatureCapacity</c>, <c>PrisonJailBroken</c>,
/// <c>DoorTileX</c>/<c>DoorTileY</c>, <c>DoorOpen</c>,
/// <c>DelayAutoCloseDoorCount</c>, on top of every room's <c>TotalCapacity</c>,
/// <c>UsedCapacity</c> and <c>CreatureList</c>. Its creature dump names
/// <c>PrisonOwner</c> at offset <c>0x3cb</c> — the keeper whose prison holds
/// this creature, which is a different thing from who owns the creature, and is
/// the field this project reproduces as
/// <see cref="CreatureState.PrisonOwnerPlayerId"/>. Facts about the binary in
/// our own words, ADR-0046.</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/prison.md")]
public static class PrisonRules
{
    /// <summary>Nobody's prisoner — the value <see cref="CreatureState.PrisonOwnerPlayerId"/> starts at.</summary>
    public const int NoPrison = -1;

    /// <summary>
    /// The chance, <b>per tick</b>, that a prisoner standing still takes
    /// another step — <b>20 in 100</b>.
    ///
    /// <para>Read out of DK2's state 116 (<c>0x74</c>), the prisoner's
    /// walking state: it rolls <c>random(100)</c> and calls
    /// <c>SetState(115)</c> — the decision state — when the roll is under 20.
    /// A second, narrower path takes rolls 10..19 instead when a per-creature
    /// flag is set, which halves it to 10 in 100; that flag has not been read,
    /// so the plain 20 is what is modelled and the narrower case is Q188.</para>
    ///
    /// <para>Twenty per cent a tick sounds enormous and is not: it only rolls
    /// while the prisoner is <i>not already walking</i>, so it is the length of
    /// the pause between steps, about five ticks on average.</para>
    /// </summary>
    public const int WanderPercentPerTick = 20;

    /// <summary>
    /// The four orthogonal steps <b>in DK2's own table order</b> — north, east,
    /// south, west — as stored at <c>0x006BC350</c>. The order is not cosmetic:
    /// the original picks a random starting index and then walks this table
    /// forwards, so the order decides which way a prisoner goes when its first
    /// choice is a wall.
    /// </summary>
    private static readonly (int Dx, int Dy)[] Orthogonal =
        [(0, -1), (1, 0), (0, 1), (-1, 0)];

    /// <summary>How many directions a prisoner considers. DK2 passes 4.</summary>
    public const int WanderDirections = 4;

    /// <summary>
    /// Where a prisoner shuffles to next: <b>one tile, orthogonally, and never
    /// out of its own cell</b>.
    ///
    /// <para><b>Read from the original's instructions rather than its
    /// decompilation</b>, because the decompiler garbles this one. DK2's
    /// <c>Room.cpp:1717</c> takes a neighbour count, draws
    /// <c>random(count)</c> as a starting index, and then scans
    /// <c>(start + i) % count</c> forwards through a table of offset pairs —
    /// the four orthogonal ones at <c>0x006BC350</c> when the count is 4, the
    /// eight at <c>0x006BC370</c> otherwise. <b>The first candidate that
    /// passes wins.</b> A candidate must have a non-zero room id, must belong
    /// to the <i>same room instance</i> the creature is standing in, and must
    /// have a terrain flag clear.</para>
    ///
    /// <para>The room-instance test is what keeps a prisoner in its cell, and
    /// it is why nothing here needs a separate "do not leave the prison" rule:
    /// a step out of the room is never a candidate in the first place. The
    /// terrain flag has no counterpart here because every tile of a room
    /// instance is already a floor tile of that room.</para>
    ///
    /// <para><b>Two prisoners may stand on one tile</b>, and that is the
    /// original's behaviour rather than an oversight of ours: the scan tests
    /// the room, not who is on the tile. It costs nothing, because a prison's
    /// capacity is its inner tiles plus one (<see cref="Capacity"/>) and never
    /// was one-per-tile; <see cref="FreeTileIn"/> avoids occupied tiles only
    /// when placing a new arrival.</para>
    /// </summary>
    /// <param name="startDirection">
    /// The drawn starting index into the table, <c>0</c>..<c>3</c>.
    /// </param>
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

            if (!cell.TileIndices.Contains((y * world.Width) + x)) continue;

            step = new TilePos(x, y);
            return true;
        }

        return false;
    }

    /// <summary>
    /// How many prisoners a prison holds: <b>the tiles the rim does not take,
    /// plus one</b> — so DK2's smallest working prison, a 3x3, holds
    /// <b>two</b>.
    ///
    /// <para><b>CONFIRMED by the owner off the game's own status bar
    /// (2026-09-06)</b> — <i>"Captives held: 0 / 2 … Maximum Skeletons: 2"</i>
    /// on a 3x3 — together with the floor underneath it: <i>"minimum usable size
    /// is 3x3 … any smaller size and it doesn't render like a prison, it only
    /// occupies those tiles."</i> This project held <b>one per tile</b>, which
    /// on that room would have been nine (Q166).</para>
    ///
    /// <para><b>And the shape is read rather than fitted.</b> DK2's room
    /// capacity routine has a case per room type; the prison's walks the room's
    /// own tiles, counts the ones a general <i>"nothing is standing here"</i>
    /// predicate accepts, and <b>adds one if the count is non-zero</b>, into the
    /// field its debug dump calls <c>SafeCreatureCapacity</c>. A prison's
    /// furniture — the bars and the gate — takes the rim, which leaves a 3x3
    /// with exactly one tile and a capacity of two. Below 3x3 there is no such
    /// tile at all, the count is zero, the <i>"add one"</i> never fires, and the
    /// room is not a prison: which is the owner's second sentence, arrived at
    /// from the arithmetic rather than added to it.</para>
    ///
    /// <para><b>Note that it is not a per-tile rule any more.</b> Two prisoners
    /// on a room with one inner tile is DK2's own arithmetic, and it is why the
    /// <i>"+1"</i> is worth keeping rather than smoothing away.
    /// <see cref="FreeTileIn"/> still puts each arrival on a tile of its own,
    /// because that is where a body gets dropped and not what limits the
    /// room.</para>
    ///
    /// <para>The rim is counted rather than computed from the bounding box
    /// (<see cref="RoomInstance.InteriorTileCount"/>), so an L-shaped prison
    /// answers sensibly. Whether DK2 furnishes an L-shape's rim the same way is
    /// <b>UNKNOWN</b>; for the rectangles anybody builds the two agree.</para>
    /// </summary>
    public static int Capacity(RoomInstance prison) =>
        prison.InteriorTileCount == 0 ? 0 : prison.InteriorTileCount + 1;

    /// <summary>Whether this creature is somebody's prisoner.</summary>
    public static bool IsImprisoned(CreatureState creature) =>
        creature.PrisonOwnerPlayerId != NoPrison;

    /// <summary>
    /// Whether <paramref name="room"/> is a prison belonging to
    /// <paramref name="playerId"/>.
    /// </summary>
    public static bool IsPrisonOf(RoomInstance room, byte playerId) =>
        room.OwnerPlayerId == playerId && room.Definition.TypeId == RoomTypeId.Prison;

    /// <summary>
    /// A free tile in <paramref name="playerId"/>'s nearest prison, or -1 when
    /// every prison is full and -1 when there is no prison at all.
    ///
    /// <para><b>Full means no delivery</b>, which is the manual's own rule read
    /// back: <i>"imps look for the next nearest prison with space in it"</i>,
    /// and when there is none <i>"imps stop collecting prisoners"</i>. Because
    /// <see cref="CarryRules.TryDestinationFor"/> is asked before the walk
    /// starts, an imp does not set off across the dungeon for a creature it has
    /// nowhere to put.</para>
    ///
    /// <para>Prisons are ranked by straight-line distance from
    /// <paramref name="from"/>, as every other room walk here ranks them (Q25),
    /// and within a prison the lowest free tile index wins — row-major order,
    /// stable, and the same tie-break <see cref="LairRules"/> uses.</para>
    /// </summary>
    public static int FreeTileIn(
        WorldState world, GameDatabase database, byte playerId, TilePos from)
    {
        int best = -1;
        long bestDistance = long.MaxValue;

        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (!IsPrisonOf(room, playerId)) continue;
            if (PrisonerCount(world, room) >= Capacity(room)) continue;

            foreach (int tileIndex in room.TileIndices)
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

    /// <summary>
    /// Puts <paramref name="creature"/> in <paramref name="playerId"/>'s prison
    /// on <paramref name="tileIndex"/>, and says whether it took.
    ///
    /// <para><b>A prisoner is alive, and barely.</b> It arrives unconscious —
    /// the only way it can arrive, because only an unconscious creature can be
    /// carried — and imprisonment ends the dying window rather than continuing
    /// it. It wakes at
    /// <see cref="CreatureHealthRules.KnockoutHealth"/>: <b>a sixth of its
    /// maximum</b>, which is what DK2's damage path writes at the moment of the
    /// knockout rather than anything the prison does at the door.
    ///
    /// <para><b>CONFIRMED by the owner</b> (2026-09-05, Q167): <i>"its flower
    /// gets two petals, and then that creature starts loosing health at some
    /// standard rate"</i>. A first draft of this gave a prisoner <i>full</i>
    /// health on nothing better than the shape of the mechanic, which would
    /// have made a Goblin take 500 game seconds to wither where it should take
    /// a hundred — the difference between the prison being a slow decision and
    /// an urgent one.</para>
    ///
    /// <para><b>The number is read, and it replaced one this project invented.</b>
    /// It used to be <c>CreatureCriticalHealthPercentageOfMax</c> = 20, on the
    /// reading <i>"two petals of ten"</i>. Both halves failed on 2026-09-06: the
    /// flower has <b>six</b> petals (Q171, screenshot), and a raw scan of
    /// <c>.text</c> finds <b>one</b> reference to that variable's global, the
    /// loader writing it. DK2's own damage path is unambiguous instead —
    /// below zero and not already dying means <c>max / 6</c> and the
    /// <c>CreatureDying</c> flag — so a Goblin arrives at 250 of 1500 and
    /// lasts <b>83</b> game seconds, not the 100 this file used to claim.</para>
    ///
    /// <para><b>One loose end, and it is a display question.</b> A sixth is
    /// exactly <i>one</i> petal of six, and the owner counts <i>two</i> on a
    /// creature an imp has just dragged in (Q167). So either the flower is not a
    /// plain proportion, or something between the knockout and the cell adds a
    /// little. Neither moves the health number, which is read.</para>
    /// </summary>
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

    /// <summary>
    /// Lets a prisoner out — used by the jail break, by losing the prison under
    /// it, and by the keeper dropping one somewhere that is not a prison.
    ///
    /// <para>The creature keeps its own keeper and everything else it had. It is
    /// simply a hostile creature standing in somebody's dungeon again, which is
    /// what the manual means by <i>"losing a prison frees every enemy in
    /// it"</i>.</para>
    /// </summary>
    public static void Release(CreatureState creature)
    {
        creature.PrisonOwnerPlayerId = NoPrison;
        if (creature.Activity == CreatureActivity.Imprisoned) creature.ClearJob();
    }

    /// <summary>
    /// The health a prison takes off a prisoner each game second:
    /// <c>PrisonModifyCreatureHealthPerSecond</c>, which ships at <b>-3</b>.
    ///
    /// <para>The manual says it in words — <i>"whilst in Prison, creatures
    /// gradually lose health as they wither away"</i> — and the variable is
    /// named for exactly this, which is the fourth time on this project that an
    /// owner-facing sentence and a shipped variable name have met.</para>
    ///
    /// <para><b>And it is hunger</b> (Q173, read 2026-09-06). The routine that
    /// applies it — <c>FUN_0047b7c0</c>, now known to be the prison state
    /// category's own per-tick hook and therefore the prison's beyond doubt —
    /// takes the health off <b>only when the creature's <c>HungerFill</c> is
    /// non-zero</b>. So the four creatures that never eat never wither: a
    /// <b>Skeleton</b> in a cell is stable for ever, which matters because a
    /// Skeleton is what a prison <i>makes</i>. The owner guessed this
    /// (2026-09-05: <i>"probably from hunger"</i>) and said he did not know; the
    /// binary says he was right.</para>
    ///
    /// <para><b>What is still ours is the cadence.</b> The original reloads a
    /// counter with <c>ticksPerSecond &lt;&lt; 2</c> and applies
    /// <c>PrisonModifyCreatureHealthPerSecond &lt;&lt; 2</c> — <b>-12 every four
    /// game seconds</b> rather than -3 every one. The average is identical and
    /// the granularity is not: a prisoner at 10 health survives three more
    /// seconds here and none there. It also applies
    /// <c>ModifyAngerInCompanyOfHatedCreaturesPerSecond</c> (150) under anger
    /// reason 9 in the same breath, which this project does not
    /// (<c>prison.md</c> §4b).</para>
    /// </summary>
    public static int WitherPerSecond(
        GameDatabase database, CreatureDef creature) =>
        CreatureHealthRules.CanGetHungry(creature)
            ? database[VariableTypeId.PrisonModifyCreatureHealthPerSecond]
            : 0;

    /// <summary>
    /// The chance in percent that an overcrowded prison breaks out this game
    /// second: <c>OvercrowdingJailBreakPercentagePerExtraCreature</c> — which
    /// ships at <b>50</b> — once per prisoner over capacity.
    ///
    /// <para><b>The cadence is ours.</b> The variable names a percentage per
    /// extra creature and says nothing about how often it is rolled; this rolls
    /// it once a game second, which is the clock every other repeated chance
    /// here rides (ADR-0043). Q168.</para>
    ///
    /// <para>A prison only becomes overcrowded because a keeper made it so —
    /// imps refuse to deliver into a full one — which is the manual's <i>"manual
    /// overcrowding"</i>, and it is also what happens when part of a prison in
    /// use is sold out from under its prisoners.</para>
    /// </summary>
    public static int JailBreakPercent(GameDatabase database, int over) =>
        over <= 0 ? 0 : over * database[VariableTypeId.OvercrowdingJailBreakPercentagePerExtraCreature];
}
