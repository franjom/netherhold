// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text;
using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// Binary save/load for the simulation.
///
/// The format is entirely our own. DK2's save layout is not a target
/// (ADR/plan risk R13): reproducing it buys nothing behavioural, and our state
/// is not shaped like the original's anyway. What matters is the round-trip
/// invariant:
/// <para>
/// <b>Snapshot a run, restore it into a fresh simulation, and both continue to
/// produce identical state hashes forever after.</b>
/// </para>
/// That is a stronger check than "the bytes match": it proves the snapshot
/// captures everything the simulation actually depends on, RNG position
/// included. Asserted by <c>SnapshotTests</c>.
///
/// The static <see cref="GameDatabase"/> is deliberately NOT stored — it is
/// derived from the original game's files and is supplied on restore.
/// </summary>
public static class WorldSnapshot
{
    private static readonly byte[] MagicBytes = Encoding.ASCII.GetBytes("DK2S");
    private const int FormatVersion = 20;

    public static void Write(Stream stream, GameSimulation simulation)
    {
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        w.Write(MagicBytes);
        w.Write(FormatVersion);

        WorldState world = simulation.World;
        w.Write(world.CurrentTick.Value);

        (ulong s0, ulong s1, ulong s2, ulong s3) = simulation.Rng.SaveState();
        w.Write(s0);
        w.Write(s1);
        w.Write(s2);
        w.Write(s3);

        w.Write(world.Width);
        w.Write(world.Height);
        w.Write(world.TileCount);
        for (int i = 0; i < world.TileCount; i++)
        {
            ref TileState t = ref world.TileAtIndex(i);
            w.Write(t.TerrainId);
            w.Write(t.OwnerPlayerId);
            w.Write(t.Flags);
            w.Write(t.Health);
            w.Write(t.GoldHeld);
            w.Write(t.WorkRemainder);
            w.Write(t.UpwardWork);
            w.Write(t.BurnTicksRemaining);
            w.Write(t.EntranceIdleTicks);
            w.Write(t.TaggedBy);
            w.Write(t.FreshlyDug);
        }

        w.Write(world.PlayerCount);
        for (int i = 0; i < world.PlayerCount; i++)
        {
            ref PlayerState p = ref world.PlayerAtIndex(i);
            w.Write(p.PlayerId);
            w.Write(p.Mana);
            w.Write(p.NextFreeImpTick.Value);
        }

        // Normally empty at a tick boundary — TileWorkSystem drains it every
        // tick — but it is hashed state, so it is snapshotted state. Anything
        // reachable from WorldState has to survive the round trip or the
        // restore-and-continue invariant is a lie.
        w.Write(world.PendingWork.Count);
        foreach (TileWorkOrder o in world.PendingWork)
        {
            w.Write(o.TileIndex);
            w.Write((int)o.Kind);
            w.Write(o.PlayerId);
            w.Write(o.WorkerCreatureId);
        }

        w.Write(world.NextCreatureId);
        w.Write(world.Creatures.Count);
        foreach (CreatureState c in world.Creatures)
        {
            w.Write(c.Id);
            w.Write(c.CreatureTypeId);
            w.Write(c.OwnerPlayerId);
            w.Write(c.PosX);
            w.Write(c.PosY);
            w.Write(c.MoveRemainder);
            w.Write(c.GoldHeld);
            w.Write(c.Health);
            w.Write(c.HungerTicks);
            w.Write(c.ChickensNeeded);
            w.Write(c.AwakeTicks);
            w.Write(c.SleepTicks);
            w.Write(c.DyingTicks);
            w.Write(c.Anger);
            w.Write(c.Level);
            w.Write(c.ExperiencePoints);
            w.Write((int)c.AngryChoice);
            w.Write((int)c.Activity);
            w.Write(c.TargetTileIndex);
            w.Write((int)c.TargetWork);
            w.Write(c.TimerTicks);
            w.Write(c.PathIndex);

            // The path is state, not a cache: a creature restored without it
            // would re-plan and could pick a different equally short route, so
            // the restored run would diverge from the one it claims to continue.
            w.Write(c.Path.Count);
            foreach (TilePos p in c.Path)
            {
                w.Write(p.X);
                w.Write(p.Y);
            }
        }

        w.Write(world.NextObjectId);
        w.Write(world.Objects.Count);
        foreach (ObjectState o in world.Objects)
        {
            w.Write(o.Id);
            w.Write(o.ObjectTypeId);
            w.Write(o.OwnerPlayerId);
            w.Write(o.TileIndex);
            w.Write(o.CreatureId);
            w.Write(o.GoldHeld);
        }
    }

    public static byte[] ToBytes(GameSimulation simulation)
    {
        using var ms = new MemoryStream();
        Write(ms, simulation);
        return ms.ToArray();
    }

    public static GameSimulation Read(
        Stream stream,
        GameDatabase database,
        IEnumerable<ISimulationSystem>? systems = null)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        byte[] magic = r.ReadBytes(MagicBytes.Length);
        if (magic.Length != MagicBytes.Length || !magic.SequenceEqual(MagicBytes))
            throw new InvalidDataException("Not a DK2 simulation snapshot (bad magic).");

        int version = r.ReadInt32();
        if (version != FormatVersion)
            throw new InvalidDataException(
                $"Snapshot format version {version} is not supported (this build reads {FormatVersion}).");

        var tick = new GameTick(r.ReadInt64());
        SimRandom rng = SimRandom.Restore((r.ReadUInt64(), r.ReadUInt64(), r.ReadUInt64(), r.ReadUInt64()));

        int width = r.ReadInt32();
        int height = r.ReadInt32();
        int tileCount = r.ReadInt32();
        if (tileCount != width * height)
            throw new InvalidDataException(
                $"Snapshot claims {width}x{height} but carries {tileCount} tiles.");

        var tiles = new TileState[tileCount];
        for (int i = 0; i < tileCount; i++)
        {
            tiles[i] = new TileState
            {
                TerrainId = r.ReadByte(),
                OwnerPlayerId = r.ReadByte(),
                Flags = r.ReadByte(),
                Health = r.ReadInt32(),
                GoldHeld = r.ReadInt32(),
                WorkRemainder = r.ReadSByte(),
                UpwardWork = r.ReadInt32(),
                BurnTicksRemaining = r.ReadInt32(),
                EntranceIdleTicks = r.ReadInt32(),
                TaggedBy = r.ReadUInt16(),
                FreshlyDug = r.ReadBoolean(),
            };
        }

        int playerCount = r.ReadInt32();
        var players = new PlayerState[playerCount];
        for (int i = 0; i < playerCount; i++)
        {
            players[i] = new PlayerState
            {
                PlayerId = r.ReadByte(),
                Mana = r.ReadInt32(),
                NextFreeImpTick = new GameTick(r.ReadInt64()),
            };
        }

        WorldState world = WorldState.FromParts(width, height, tiles, players);
        world.CurrentTick = tick;

        int workCount = r.ReadInt32();
        for (int i = 0; i < workCount; i++)
        {
            world.QueueWork(new TileWorkOrder(
                r.ReadInt32(), (TileWorkKind)r.ReadInt32(), r.ReadByte(), r.ReadInt32()));
        }

        int nextCreatureId = r.ReadInt32();
        int creatureCount = r.ReadInt32();
        for (int i = 0; i < creatureCount; i++)
        {
            var creature = new CreatureState
            {
                Id = r.ReadInt32(),
                CreatureTypeId = r.ReadByte(),
                OwnerPlayerId = r.ReadByte(),
                PosX = r.ReadInt32(),
                PosY = r.ReadInt32(),
                MoveRemainder = r.ReadInt32(),
                GoldHeld = r.ReadInt32(),
                Health = r.ReadInt32(),
                HungerTicks = r.ReadInt32(),
                ChickensNeeded = r.ReadInt32(),
                AwakeTicks = r.ReadInt32(),
                SleepTicks = r.ReadInt32(),
                DyingTicks = r.ReadInt32(),
                Anger = r.ReadInt32(),
                Level = r.ReadInt32(),
                ExperiencePoints = r.ReadInt32(),
                AngryChoice = (JobType)r.ReadInt32(),
                Activity = (CreatureActivity)r.ReadInt32(),
                TargetTileIndex = r.ReadInt32(),
                TargetWork = (TileWorkKind)r.ReadInt32(),
                TimerTicks = r.ReadInt32(),
                PathIndex = r.ReadInt32(),
            };

            int pathLength = r.ReadInt32();
            for (int p = 0; p < pathLength; p++)
                creature.Path.Add(new TilePos(r.ReadInt32(), r.ReadInt32()));

            world.RawCreatures.Add(creature);
        }

        // Creatures are put back with their stored ids rather than re-spawned,
        // so the counter is restored rather than derived: a level that has had
        // creatures die must not hand out an id it has already used.
        world.NextCreatureId = nextCreatureId;

        int nextObjectId = r.ReadInt32();
        int objectCount = r.ReadInt32();
        for (int i = 0; i < objectCount; i++)
        {
            world.RawObjects.Add(new ObjectState
            {
                Id = r.ReadInt32(),
                ObjectTypeId = r.ReadByte(),
                OwnerPlayerId = r.ReadByte(),
                TileIndex = r.ReadInt32(),
                CreatureId = r.ReadInt32(),
                GoldHeld = r.ReadInt32(),
            });
        }

        // Restored rather than re-derived, for the reason the creature counter
        // is: a bed destroyed when its owner died must not have its id handed
        // out again to the next bed.
        world.NextObjectId = nextObjectId;

        return new GameSimulation(database, world, rng, systems);
    }

    public static GameSimulation FromBytes(
        byte[] bytes,
        GameDatabase database,
        IEnumerable<ISimulationSystem>? systems = null)
    {
        using var ms = new MemoryStream(bytes, writable: false);
        return Read(ms, database, systems);
    }
}
