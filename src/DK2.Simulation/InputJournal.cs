// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text;

namespace DK2.Simulation;

/// <summary>
/// Writes the input journal (ADR-0552, crash-and-debug plan item 1): a header, then for each tick
/// the complete <see cref="InputFrame"/> the simulation was given (when it has commands) and the
/// state hash after it, then an end record. The simulation reads nothing else from outside, so
/// header plus frames reproduce a run, and the hashes say where a replay first diverges.
///
/// <para>Crash survival: the header and every frame with commands are flushed when written, and
/// hashes at least every <see cref="FlushEveryTicks"/> ticks. A killed process loses no command
/// and at most that many trailing hashes; <see cref="InputJournal.Read"/> keeps every whole record.</para>
/// </summary>
public sealed class InputJournalWriter : IDisposable
{
    public const int FlushEveryTicks = 256;

    private readonly Stream _stream;
    private readonly BinaryWriter _w;
    private long _lastHashTick;
    private long _lastFlushTick;

    public InputJournalWriter(Stream stream, IReadOnlyDictionary<string, string> header)
    {
        _stream = stream;
        _w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        _w.Write(InputJournal.Magic);
        _w.Write(InputJournal.FormatVersion);
        _w.Write(GameSimulation.CurrentHashVersion);
        _w.Write(header.Count);
        foreach ((string key, string value) in header.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            _w.Write(key);
            _w.Write(value);
        }
        Flush();
    }

    /// <summary>The frame given to the tick that starts at <paramref name="tick"/>; empty frames are not written.</summary>
    public void WriteFrame(long tick, in InputFrame frame)
    {
        if (frame.All.Count == 0) return;
        // Encoded whole before any byte reaches the journal: a command the codec refuses must
        // not leave half a frame for the end record to follow.
        using var buffer = new MemoryStream();
        using (var b = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
            foreach (PlayerCommand command in frame.All) CommandCodec.Write(b, command);
        _w.Write(InputJournal.FrameRecord);
        _w.Write(tick);
        _w.Write(frame.All.Count);
        _w.Write(buffer.GetBuffer(), 0, (int)buffer.Length);
        Flush();
    }

    /// <summary>The state hash once <paramref name="tick"/> ticks have run (0 is the starting world).</summary>
    public void WriteHash(long tick, ulong hash)
    {
        _w.Write(InputJournal.HashRecord);
        _w.Write(tick);
        _w.Write(hash);
        _lastHashTick = tick;
        if (tick - _lastFlushTick >= FlushEveryTicks) Flush();
    }

    /// <summary>Closes the journal with how the run ended: "completed", or a failure's description.</summary>
    public void WriteEnd(long ticksRun, string reason)
    {
        _w.Write(InputJournal.EndRecord);
        _w.Write(ticksRun);
        _w.Write(reason);
        Flush();
    }

    private void Flush()
    {
        _w.Flush();
        _stream.Flush();
        if (_stream is FileStream file) file.Flush(flushToDisk: false);
        _lastFlushTick = _lastHashTick;
    }

    public void Dispose()
    {
        _w.Flush();
        _w.Dispose();
    }
}

/// <summary>A journal read back: its header, frames by tick, hashes by tick, and how it ended.</summary>
public sealed class InputJournal
{
    internal static readonly uint Magic = 0x4A324B44; // "DK2J"
    internal const int FormatVersion = 1;
    internal const byte FrameRecord = 1;
    internal const byte HashRecord = 2;
    internal const byte EndRecord = 3;

    private readonly Dictionary<long, InputFrame> _frames;

    private InputJournal(
        ulong hashVersion, IReadOnlyDictionary<string, string> header, Dictionary<long, InputFrame> frames,
        IReadOnlyDictionary<long, ulong> hashes, long? ticksRun, string? endReason)
    {
        HashVersion = hashVersion;
        Header = header;
        _frames = frames;
        Hashes = hashes;
        TicksRun = ticksRun;
        EndReason = endReason;
    }

    public ulong HashVersion { get; }
    public IReadOnlyDictionary<string, string> Header { get; }
    public IReadOnlyDictionary<long, ulong> Hashes { get; }

    /// <summary>Ticks run, from the end record; null when the journal was cut short (a killed run).</summary>
    public long? TicksRun { get; }

    /// <summary>"completed" or the failure the run recorded; null when there is no end record.</summary>
    public string? EndReason { get; }

    public bool Truncated => TicksRun is null;

    /// <summary>The last tick count with a recorded hash, which a cut-short journal is replayable up to.</summary>
    public long LastHashedTick => Hashes.Count == 0 ? -1 : Hashes.Keys.Max();

    public IEnumerable<long> FrameTicks => _frames.Keys.OrderBy(t => t);

    public InputFrame FrameAt(long tick) => _frames.TryGetValue(tick, out InputFrame f) ? f : InputFrame.Empty;

    /// <summary>Reads every whole record. Fails on a wrong magic or format version, a cut-off header
    /// and an unknown record or command; a record cut off by the end of the stream ends the read.</summary>
    public static InputJournal Read(Stream stream)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        ulong hashVersion;
        var header = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            if (r.ReadUInt32() != Magic) throw new InvalidDataException("Not an input journal.");
            int format = r.ReadInt32();
            if (format != FormatVersion)
                throw new InvalidDataException($"Journal format {format}; this build reads {FormatVersion}.");
            hashVersion = r.ReadUInt64();
            for (int i = r.ReadInt32(); i > 0; i--) header[r.ReadString()] = r.ReadString();
        }
        catch (EndOfStreamException)
        {
            throw new InvalidDataException("The journal ends inside its header: the run never reached tick 0.");
        }

        var frames = new Dictionary<long, InputFrame>();
        var hashes = new Dictionary<long, ulong>();
        long? ticksRun = null;
        string? reason = null;
        while (ticksRun is null)
        {
            int kind = stream.ReadByte();
            if (kind < 0) break;
            try
            {
                switch (kind)
                {
                    case FrameRecord:
                        long tick = r.ReadInt64();
                        var commands = new PlayerCommand[r.ReadInt32()];
                        for (int i = 0; i < commands.Length; i++) commands[i] = CommandCodec.Read(r);
                        frames[tick] = new InputFrame(commands);
                        break;
                    case HashRecord:
                        long at = r.ReadInt64();
                        hashes[at] = r.ReadUInt64();
                        break;
                    case EndRecord:
                        long run = r.ReadInt64();
                        reason = r.ReadString();
                        ticksRun = run;
                        break;
                    default:
                        throw new InvalidDataException($"Unknown journal record {kind}.");
                }
            }
            catch (EndOfStreamException)
            {
                break;
            }
        }

        return new InputJournal(hashVersion, header, frames, hashes, ticksRun, reason);
    }
}
