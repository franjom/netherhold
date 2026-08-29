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
    private const int FormatVersion = 2;

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
            w.Write(t.TaggedBy);
        }

        w.Write(world.PlayerCount);
        for (int i = 0; i < world.PlayerCount; i++)
        {
            ref PlayerState p = ref world.PlayerAtIndex(i);
            w.Write(p.PlayerId);
            w.Write(p.Gold);
            w.Write(p.Mana);
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
                TaggedBy = r.ReadUInt16(),
            };
        }

        int playerCount = r.ReadInt32();
        var players = new PlayerState[playerCount];
        for (int i = 0; i < playerCount; i++)
        {
            players[i] = new PlayerState
            {
                PlayerId = r.ReadByte(),
                Gold = r.ReadInt32(),
                Mana = r.ReadInt32(),
            };
        }

        WorldState world = WorldState.FromParts(width, height, tiles, players);
        world.CurrentTick = tick;

        int workCount = r.ReadInt32();
        for (int i = 0; i < workCount; i++)
        {
            world.QueueWork(new TileWorkOrder(
                r.ReadInt32(), (TileWorkKind)r.ReadInt32(), r.ReadByte()));
        }

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
